# Phase 6K – Implementation Notes & Q&A (v2)

**Purpose**

This doc answers your Phase 6K questions *in the context of the existing codebase* and slightly refines the original 6K spec so we **reuse what already exists**:

- ✅ `StructureNode` already exists (with `McidReferences`)
- ✅ `McidRewritePlanBuilder` already exists and builds plans from `McidReferences`
- ✅ `ITextPdfStructureWriter` already calls the external MCID rewriter
- ✅ `RemediationOptions.EnableMcidContentRewrite` is already wired

We are **not** redoing remediation. We are finishing the MCID wiring by:

1. Making sure MCIDs / `McidReferences` are populated deterministically in reading order.
2. Building **MCR kids** in the structure tree that point to those MCIDs.
3. Ensuring everything uses the **same MCID mapping** that `McidRewritePlanBuilder` sends to Python.

You can give this file to Claude along with the main Phase 6K spec and say:

> “Use `PHASE-6K-STRUCTURE-TREE-MCR-INTEGRATION-v1.md` and `PHASE-6K-IMPLEMENTATION-NOTES-v2.md` as the authoritative spec for Phase 6K.”

---

## 0. Restating Your Summary

Your current summary is accurate:

- ✅ `StructureNode` exists with `McidReferences` list  
- ✅ `McidRewritePlanBuilder` already builds plans from `McidReferences`  
- ✅ `ITextPdfStructureWriter` already calls external rewriter  
- ✅ `EnableMcidContentRewrite` flag exists  

**Missing:**

1. `McidReferences` are not being populated.
2. Structure elements are not created with **MCR kids** pointing to `(Page, MCID)`.
3. There is no **deterministic MCID assignment** over the structure tree in reading order.

Phase 6K = fix those 3 things, reusing what we have.

---

## 1. IContentMcidMarker

> **Q1**:  
> *IContentMcidMarker interface – What is this? It's injected into ITextPdfStructureWriter but I haven't seen its implementation. Should I ignore it for Phase 6K?*

### Answer

- Treat `IContentMcidMarker` as **legacy/stub** from the earlier “do MCID marking inside iText” approach.
- For Phase 6K (external Python rewriter is the canonical implementation), you should:

  - **Do not depend** on `IContentMcidMarker` for MCID assignment or plan building.
  - Leave the interface in place to avoid breaking DI.
  - If there is a concrete implementation:
    - Do **not** call it for MCID assignment.
    - If it’s currently used in a way that conflicts with the new flow, remove or no-op that usage.

**In short:** Ignore `IContentMcidMarker` for 6K. MCID assignment and linking should be driven by `StructureNode` + `McidReferences` + `McidRewritePlanBuilder` + external rewriter.

---

## 2. Current MCID Allocation (`AllocateMcids`)

> **Q2:**  
> *There’s an `AllocateMcids` method called at line 111 of `ITextPdfStructureWriter`. Should I replace this with the new Phase 6K approach, or keep it and add Phase 6K on top?*

### Answer

- **Keep the method, change what it does.**

`AllocateMcids` is the right *hook* for Phase 6K – we just want it to:

1. Walk the `StructureNode` tree in reading order.
2. Allocate MCIDs per page.
3. Populate `StructureNode.McidReferences` (or whatever structure is already in place).
4. Let `McidRewritePlanBuilder` use those references to build the plan.

So for Phase 6K:

- **Do not introduce a second, competing MCID allocator**.
- Instead:

  - Implement the 6K algorithm *inside* `AllocateMcids` (or have it delegate to a new `McidAssignmentService`, but `AllocateMcids` remains the call site).
  - Ensure that any previous ad-hoc or placeholder MCID logic in `AllocateMcids` is replaced, not layered.

**In practice:**

- The steps from the 6K spec (per-page counters, reading order enumeration, assigning MCIDs) live in or under `AllocateMcids`.
- `McidRewritePlanBuilder` stays as-is; it just finally gets real, deterministic `McidReferences` to work with.

---

## 3. PageIndex: StructureNode vs McidReference

> **Q3:**  
> *PageIndex convention – `StructureNode.Bounds` doesn't have PageIndex, McidReference has PageIndex, spec says StructureNode should have PageIndex. Should I add PageIndex to StructureNode, or rely on `McidReference.PageIndex`?*

### Answer

To minimize churn and respect the existing model:

- **Canonical page index source = `McidReference.PageIndex`.**
- **You do *not* have to add `PageIndex` to `StructureNode`** if:
  - `McidReferences` already carry `PageIndex`.
  - `McidRewritePlanBuilder` already uses it.

Recommended approach:

- Use `StructureNode` as the **logical parent** of one or more `McidReference`s.
- Each `McidReference` should include:
  - `PageIndex`
  - `Mcid`
  - Optional geometry (if applicable)
- When building the iText structure tree:
  - For each `StructureNode`, iterate its `McidReferences`.
  - For each reference, attach an MCR kid pointing to:
    - `pdfDoc.GetPage(ref.PageIndex)`  
    - `ref.Mcid`

This way:

- We don’t refactor the model to shoehorn `PageIndex` directly into `StructureNode`.
- We follow the existing pattern where `McidReference` is the “leaf-level binding” from logical node → content segment.

If it’s trivial to add a cached `PageIndex` to `StructureNode` and you find it simplifies code, that’s fine – but **the spec does not require it**. Treat that as an optional convenience, not a must.

---

## 4. Reading Order

> **Q4:**  
> *Where is reading order currently determined? I need to find how nodes are ordered for MCID assignment.*

### Answer

For Phase 6K, **do not invent a new reading-order mechanism**. Instead:

1. **Use whatever ordering the `StructureNode` tree already has.**

   The earlier phases (layout analysis, structure building) should already establish a logical order when populating children:

   - Tables: left-to-right, top-to-bottom.
   - Paragraphs/headings: in visual reading order.
   - Sections: root → children → grandchildren in doc order.

   In other words, `node.Children` is already in reading order for that subtree.

2. **Provide a simple walker:**

   Implement something like:

   ```csharp
   private IEnumerable<StructureNode> EnumerateInReadingOrder(StructureNode root)
   {
       // Depth-first, or breadth-first, as long as it respects the existing Children order.
       yield return root;
       foreach (var child in root.Children)
       {
           foreach (var desc in EnumerateInReadingOrder(child))
               yield return desc;
       }
   }
   ```

   Then, when assigning MCIDs / populating `McidReferences`, you:

   - Call `EnumerateInReadingOrder(root)` and filter to nodes that should have MCIDs.
   - Use this sequence as your `SequenceIndex` ordering.

3. **If there is an explicit LayoutPlan / PageLayoutPlan:**

   - If you find a `LayoutPlan` or similar already in use (e.g., for Phase 3), you can refine the reading order by:
     - Using the plan to order per-page nodes.
     - Then walking nodes in that plan order.
   - But **this is optional for 6K v1**. You can start with child-order traversal and only upgrade to LayoutPlan if needed.

---

## 5. What To Do with Your TODO List

You wrote:

> Todos  
> ☐ Add Mcid property to StructureNode model  
> ☐ Create McidAssignmentService with AssignMcids and BuildRewritePlan methods  
> ☐ Update ITextPdfStructureWriter to create MCR kids for nodes with MCIDs  
> ☐ Register McidAssignmentService in DI (Program.cs or equivalent)  
> ☐ Integrate McidAssignmentService into StructureRebuildService  
> ☐ Test end-to-end with synthetic PDF

Here’s how I’d tweak that list based on the current reality:

### 5.1 Updated TODOs

1. **MCID storage**  
   - ✅ `StructureNode` already has `McidReferences`.  
   - ☐ **Ensure `McidReferences` has everything needed**:
     - `PageIndex`
     - `Mcid`
     - (Optionally) bounds and sequence index used by `McidRewritePlanBuilder`.

   > *You do not have to add a separate `Mcid` property to `StructureNode` unless you want it as sugar.*

2. **MCID assignment logic (Phase 6K core)**  
   - ☐ Implement deterministic MCID assignment in **one place**:
     - Either:
       - Implement `McidAssignmentService.AssignMcids(StructureNode root)` and call it from `AllocateMcids`.
       - Or:
       - Implement the new algorithm directly inside `AllocateMcids`.
   - ☐ For each leaf node that should map to content:
     - Create one or more `McidReference`s with:
       - Per-page counters (MCID 0,1,2… per page).
       - Reading-order-consistent `SequenceIndex`.

3. **McidRewritePlanBuilder**  
   - ✅ Already exists.  
   - ☐ Confirm it uses `McidReferences` to build:
     - `PageIndex`
     - `Mcid`
     - `X`, `Y`, `Width`, `Height`
     - `SequenceIndex`, `Role`.

   - ☐ Adjust if needed to match the Phase 6H/6J microservice contract, but do **not** create a new plan type.

4. **Structure tree MCR kids (`ITextPdfStructureWriter`)**  
   - ☐ When building the iText structure tree:
     - For each `StructureNode`:
       - Create a `PdfStructElem` for the node’s role.
       - For each `McidReference` attached to that node:
         - Get page via `pdfDoc.GetPage(mcidRef.PageIndex)`.
         - Create MCR (via `PdfMcrNumber` / equivalent / manual dict).
         - Add as kid to the `PdfStructElem`.

5. **DI & pipeline integration**  
   - ☐ If you implement a separate `McidAssignmentService`, register it in DI and:
     - Call it from `StructureRebuildService` or `ITextPdfStructureWriter` before plan building.
   - ☐ Ensure `EnableMcidContentRewrite` gates the **full flow**:
     - If false → no MCID assignment, no external rewriter call.
     - If true → run assignment + plan build + external rewriter.

6. **End-to-end test**  
   - ☐ Use a small synthetic test PDF through the **entire pipeline**:
     - Preflight (Aspose/Artifact-fix).
     - Structure rebuild (with struct tree + MCR kids).
     - External MCID rewriter (Python).
   - ☐ Confirm:
     - Non-zero BDC/EMC in final output.
     - Struct tree tags in PDFix/Acrobat are linked to content (click tag → highlight).
     - No post-phase is rewriting content streams.

---

## 6. Answer to “Proceed or Clarify?”

> *Would you like me to proceed with the implementation based on my best understanding, or would you like to clarify these points first?*

**Answer:** Proceed now, using this doc + the main 6K spec as your guide.

Key constraints to keep in mind:

- Reuse `McidReferences` and `McidRewritePlanBuilder`. Don’t invent a new model.
- Use `AllocateMcids` (or a new `McidAssignmentService` called from it) as the **central place** that populates `McidReferences`.
- Ignore `IContentMcidMarker` for 6K.
- Don’t let any post-MCID phase touch content streams.

Once you’ve wired that up, we can look at a log excerpt plus a sample remediated PDF and iterate if needed – but you don’t need more clarification before starting. This *is* the clarification. :-)

---

**Filename:**  
`PHASE-6K-IMPLEMENTATION-NOTES-v2.md`
