# Phase 6K – Q&A and Integration Guide (v1)

**Context**

This doc is an addendum to `PHASE-6K-STRUCTURE-TREE-MCR-INTEGRATION-v1.md`. It answers Claude’s integration questions and pins down *where* to hook things in the existing codebase.

Give this file to Claude along with the main 6K spec and say:

> “Use `PHASE-6K-STRUCTURE-TREE-MCR-INTEGRATION-v1.md` plus `PHASE-6K-6K-QA-AND-INTEGRATION-GUIDE-v1.md` to implement Phase 6K.”

---

## 1. Existing Structure Tree Code

> **Q1:**  
> - Where is the current structure tree building code? The spec mentions `ITextPdfStructureWriter` or `StructureTreeBuilder` – which one exists and where?  
> - What does it currently do? Does it already create structure elements, or does it do something else?

### Answer

1. **Use `ITextPdfStructureWriter` as the structure builder.**

   In this repo, structure rebuild is implemented via the iText-based writer. You should find it under a path like:

   - `Services/Pdf/ITextPdfStructureWriter.cs`

   If you’re unsure, search the repo for:

   - `class ITextPdfStructureWriter`
   - or the log strings you’ve already seen in logs, e.g. `"[ITEXT-STRUCTURE]"`

2. There is **no separate `StructureTreeBuilder` type** right now – that name in the spec was conceptual. The actual builder is the iText writer.

3. What it currently does (high level):

   - Takes a `StructureTree` / logical representation (built earlier in the pipeline).
   - Opens an iText `PdfDocument` (often wrapping an existing PDF or building new).
   - Creates tagged output (tags enabled) and writes a structure tree / roles, sometimes in a simplified way.
   - Writes content or passes through content, depending on phase.

   **Phase 6K changes go into this writer**:

   - Add MCID-aware structure building: for each leaf node with a `Mcid`, create a `PdfStructElem` and attach a corresponding MCR kid for (Page, MCID).

---

## 2. StructureNode Model

> **Q2:**  
> - Where is `StructureNode` defined? The spec shows a record with Role, Bounds, PageIndex, Children, Parent – does this already exist?  
> - If it exists, what properties does it currently have? Do I just need to add `Mcid`, or are other properties missing too?

### Answer

1. **Where it lives**

   There is already a structural model used by the AI structure rebuild. Look for a file like:

   - `Models/Structure/StructureNode.cs`
   - or search for: `record StructureNode` or `sealed record StructureNode`.

2. **What it currently has**

   It should already have fields similar to:

   - `Role` (string) – logical role like `"P"`, `"H1"`, `"TD"`, etc.
   - `Bounds` (a `Rect` or similar) – logical bounding box on the page.
   - `PageIndex` – which page the node belongs to.
   - `Children` – list of child nodes.
   - `Parent` – parent node.

   If some of these are missing, **add them now** so the spec requirements are satisfied. You will need at least:

   - `Role`
   - `Bounds` (or equivalent geometry)
   - `PageIndex`
   - `Children`

3. **What to add for Phase 6K**

   Add a nullable MCID property:

   ```csharp
   public sealed record StructureNode
   {
       public string Role { get; init; }
       public Rect? Bounds { get; init; }
       public int? PageIndex { get; init; }
       public int? Mcid { get; init; }  // NEW for Phase 6K

       public IReadOnlyList<StructureNode> Children { get; init; } = Array.Empty<StructureNode>();
       public StructureNode? Parent { get; init; }
       // existing props...
   }
   ```

   Use `init` (record-style immutability) and assign MCIDs during tree construction / mapping, or via `with` expressions. If the existing code already uses mutable properties, stay consistent with that style.

---

## 3. Reading Order

> **Q3:**  
> - How is reading order currently determined? The spec mentions `EnumerateInReadingOrder(root)` – does this method exist?  
> - Where is the `LayoutPlan` with reading order that the spec mentions?

### Answer

1. **Reading order source**

   You already have a layout/reading-order concept from earlier phases (the AI layout model you’re using in Word→PDF and in structure rebuild). Look for:

   - `LayoutPlan`
   - `PageLayoutPlan`
   - or methods like `BuildLayoutPlan`, `GetReadingOrder` in:

     - `Services/Remediation/StructureRebuildService.cs`
     - or any `Layout` or `PageLayout` services.

   If you can’t find `LayoutPlan` literally, you likely have **something equivalent** that defines the order of blocks or nodes per page and is used by `ITextPdfStructureWriter`.

2. **`EnumerateInReadingOrder`**

   This method probably does **not** exist yet – it was used in the spec as a conceptual helper.

   Add a helper in whatever class is most convenient (for example, in `McidAssignmentService` or the structure model):

   ```csharp
   private IEnumerable<StructureNode> EnumerateInReadingOrder(StructureNode root)
   {
       // If you have a LayoutPlan, use it here.
       // Otherwise, fall back to a depth-first traversal that respects
       // whatever ordering is already baked into Children.
   }
   ```

   **For v1**, you can:

   - If there is a `LayoutPlan` (e.g., containing a list of nodes per page in order), use that to drive enumeration.
   - Otherwise, rely on the existing `Children` list ordering, assuming previous phases already sorted children in reading order.

3. **If `LayoutPlan` doesn’t exist in code**

   - You don’t have to invent a full layout engine right now.
   - Just:

     - Ensure that when `StructureNode` trees are created, children are added in reading order.
     - Use a simple in-order traversal for `EnumerateInReadingOrder`.

---

## 4. Pipeline Integration Points

> **Q4:**  
> - Where does structure rebuild happen in the remediation pipeline? Is it in `RemediationOrchestrator`?  
> - What is Phase 6D? The spec mentions "Aspose font fixes, ARTIFACT-FIX etc. (Phase 6D)" – where is this in the codebase?  
> - Where should I hook in the `McidAssignmentService`? What's the exact integration point?

### Answer

1. **Where structure rebuild happens**

   Structure rebuild is already wired in the remediation pipeline via a dedicated service. Look for:

   - `Services/Remediation/StructureRebuildService.cs`

   This service is invoked by the main orchestrator, which is usually:

   - `Services/Remediation/RemediationOrchestrator.cs`
   - or a similarly named class.

   In logs you’ve already seen messages like:

   - `"Phase 'AI Structure Rebuild'"`  
   - `"AI-STRUCTURE-REBUILD complete"`  
   - `"ITEXT-STRUCTURE"` logs.

   That code path is **where you integrate 6K**.

2. **Phase 6D (preflight)**

   Phase 6D is your “preflight” step, which now includes:

   - Aspose Cloud Font Optimization (through `FontEmbeddingServiceAdapter` / `Aspose` adapter).
   - ARTIFACT-FIX service (unmarked XObject wrapping).

   These are wired in a dedicated preflight service, e.g.:

   - `Services/Remediation/PdfPreflightService.cs`
   - `IPdfPreflightService` implementation.

   This preflight runs **before** structure rebuild in the remediation pipeline.

3. **Where to hook `McidAssignmentService`**

   The correct place is inside the structure rebuild path:

   - In `StructureRebuildService`, after you’ve constructed the logical `StructureNode` tree and (if applicable) layout information.
   - **Before** calling `ITextPdfStructureWriter`.

   Rough sequence (in `StructureRebuildService`):

   ```csharp
   // 1. Build logical document / structure tree
   StructureNode root = structureTreeBuilder.Build(logicalDocument, layoutPlan, ...);

   // 2. Assign MCIDs and build rewrite plan
   _mcidAssignmentService.AssignMcids(root);
   var rewritePlan = _mcidAssignmentService.BuildRewritePlan(root, jobContext.DocumentId);

   // 3. Build tagged PDF with structure tree + MCRs
   var taggedPdfBytes = _iTextPdfStructureWriter.BuildTaggedPdf(originalPdfBytes, root, rewritePlan, jobContext);

   // 4. If MCID content rewrite is enabled, call external microservice
   if (jobContext.Options.EnableMcidContentRewrite)
   {
       resultPdfBytes = _externalMcidRewriter.Rewrite(taggedPdfBytes, rewritePlan, jobContext);
   }
   else
   {
       resultPdfBytes = taggedPdfBytes;
   }

   return resultPdfBytes;
   ```

   The exact method names may differ, but **this is the integration pattern**.

---

## 5. Configuration

> **Q5:**  
> - Where is `EnableMcidContentRewrite` defined? Is it in `appsettings.json` or somewhere else?  
> - Are there other feature flags I should be aware of?

### Answer

1. **Where it is defined**

   - There is already a `EnableMcidContentRewrite` flag wired in from previous phases (6E, 6F, 6G).
   - It starts in `appsettings.json` under something like `RemediationOptions` or `Remediation:Options`.

     Example (conceptual):

     ```json
     "RemediationOptions": {
       "EnableMcidLinking": true,
       "EnableMcidContentRewrite": true
     }
     ```

   - It flows into:

     - `WordToPdfConverter.Configuration.RemediationOptions` (or similar class), and then
     - Into `RemediationJobContext.Options`.

2. **How to use it**

   - Use `jobContext.Options.EnableMcidContentRewrite` inside `StructureRebuildService` and/or `ITextPdfStructureWriter` to decide whether to:
     - Build MCID-aware structure tree + rewrite plan + call Python, or
     - Skip entirely.

3. **Other flags**

   Related flags you should be aware of:

   - `EnableMcidLinking` – may exist as a separate “phase 6” feature flag.
   - Preflight / Aspose mode flags – they control whether Aspose + ARTIFACT-FIX run and in which modes.

   You **do not** need new flags for 6K. Reuse `EnableMcidContentRewrite` as the master toggle for “full MCID story”.

---

## 6. Models (Phase 6H)

> **Q6:**  
> - Are the existing `Models/Phase6H/` models (`McidRewritePlan`, `McidSegment`, etc.) the same ones referenced in the spec? Or do I need to create new versions for Phase 6K?

### Answer

- **Reuse the existing models.**  
  Do **not** create new types with different names.

- If they live in a namespace like:

  - `WordToPdfConverter.Models.Phase6H`
  - or `Models/Phase6/McidRewritePlan.cs`

  then:

  - Ensure the properties match what the 6K spec expects:
    - `PageIndex`
    - `Mcid`
    - `Role`
    - `X`, `Y`, `Width`, `Height`
    - `SequenceIndex`
    - plus `DocumentId`, `Version` on the root plan.

- If something is missing, **extend those existing classes** rather than introducing `Phase6K` duplicates.

The Python microservice is already expecting this shape; keep it consistent.

---

## 7. iText7 API

> **Q7:**  
> - What version of iText7 is being used?  
> - Does the current version support `PdfMcrNumber` or `PdfMcrDictionary`? The API may have changed across versions.

### Answer

1. **Determine version**

   - Open the `.csproj` where iText is referenced (likely in `WordToPdfConverter`).
   - Look for package references like:

     ```xml
     <PackageReference Include="itext7" Version="7.x.y" />
     <PackageReference Include="itext7.pdfkernel" Version="7.x.y" />
     ```

   - Whatever version you see there is the authoritative one.

2. **MCR APIs**

   - For iText7, MCID references are typically created via:

     - `PdfMcrNumber`
     - or `PdfMcrDictionary`
     - or older style direct dictionary manipulation.

   - If your version doesn’t expose `PdfMcrNumber`, you can:

     - Create a `PdfDictionary` with `/Type /MCR`, `/Pg` (page reference), `/MCID` (int), and add it to the struct element’s `/K` array manually.

   Example pattern if helpers exist (pseudo-API):

   ```csharp
   var page = pdfDoc.GetPage(pageIndex);
   var mcr = new PdfMcrNumber(page, mcid);
   structElem.AddKid(mcr);
   ```

   If not, Claude should:

   - Inspect the current iText version’s API docs (already on disk via IntelliSense) and choose the appropriate way to add MCR kids.
   - The high-level goal stays the same: **child of struct element must be an MCR that points to (Pg, MCID)**.

---

## 8. Post-Structure Phases Warning

> **Q8:**  
> - The spec warns "MUST NOT rewrite or replace page content streams" after the Python rewriter runs. What are these post-structure phases and where are they defined?  
> - How do I ensure they don't touch content streams?

### Answer

1. **What post-structure phases are**

   After structure rebuild, your pipeline has additional phases such as:

   - Structure enhancement
   - Font fixes
   - Artifact cleanup
   - PDF/A optimization, etc.

   These are implemented as remediation services and adapters under:

   - `Services/Remediation/*`
   - and orchestrated from `RemediationOrchestrator`.

   You’ve already moved:

   - Aspose font optimization and artifact fix into **preflight** (Phase 6D) specifically to avoid content stream changes later.

2. **Ensuring they don’t touch content streams**

   - **Rule:** After `ExternalMcidRewriterService` returns the MCID-updated PDF, **no other service** should:

     - Load the PDF and re-save it in a way that rewrites page content.
     - Call into any library that might regenerate content streams.

   Implementation guideline:

   - In `RemediationOrchestrator`, for any phase that runs after structure rebuild + MCID rewrite:

     - Either:
       - Operate only on metadata / document-level info, or
       - Be disabled when `EnableMcidContentRewrite == true`.

   - If any post-phase currently takes and returns `byte[] pdf`, inspect it:
     - If it uses Aspose / Syncfusion / iText to “optimize”, “flatten”, or “rewrite” pages, it must **not** run after MCID rewrite.

   The simplest enforcement is:

   - Have a clear phase ordering.
   - For all phases after MCID rewrite, assert that they either:
     - Are no-ops on content streams, or
     - Are skipped under MCID mode.

---

## 9. Testing

> **Q9:**  
> - Is there existing test infrastructure I should use? Or should I create new test files like `TestPhase6K.cs`?  
> - Where are synthetic test PDFs located?

### Answer

1. **Test infrastructure**

   There is likely an existing xUnit (or similar) test project, e.g.:

   - `WordToPdfConverter.Tests`
   - or `AccessForm.Tests`.

   Search for:

   - `.csproj` files under a `Tests` or `test` folder.
   - Classes with `[Fact]` or `[Test]` attributes.

   If such a project exists:

   - Add a new test class, e.g. `Phase6KTests.cs`.

   If not:

   - Create a new test project and wire it up minimally; but do not let that block implementation – you can also test manually via the existing CLI endpoint or API.

2. **Synthetic PDFs**

   Check for an assets folder already in use for tests, such as:

   - `StateAssets/`
   - `TestAssets/`
   - `SamplePdfs/`

   If none exists, create a simple folder under the test project:

   - `Tests/Assets/Phase6K/`

   and put:

   - A tiny 1-page PDF with:

     - One heading
     - One paragraph
     - One figure or form field

   Use that for:

   - Verifying structure tree + MCRs
   - Verifying MCID rewrite + BDC/EMC
   - Checking Acrobat/PDFix behaviour.

---

## 10. Coordination with Existing Phase 6J

> **Q10:**  
> - The Python microservice already exists and works. Do I need to modify it at all, or is this purely .NET changes?  
> - The `ExternalMcidRewriterService` already calls the microservice. Does it need any changes, or just use it as-is?

### Answer

1. **Python microservice**

   - If it already implements 6H/6I/6J as per the specs (geometry, images, segmentation, wrapping), you **do not need to change its public contract** for 6K.
   - It should already accept:

     - `pdfBase64`
     - `McidRewritePlan` (`segments` list with page, mcid, bbox, etc.)

   - The only requirement from 6K is that **the MCIDs in the plan now come from the .NET structure tree** instead of being hypothetical.

2. **`ExternalMcidRewriterService`**

   - The wiring is already there, but you should confirm:

     - It is called **after** iText structure rebuild has created the tag tree + MCRs.
     - It receives:
       - The **tagged PDF bytes** (output of `ITextPdfStructureWriter`), not the original input.
       - The same `McidRewritePlan` that was used when building the structure tree.

   - You might need to update its method signatures slightly so it explicitly takes the plan object:

     ```csharp
     byte[] Rewrite(byte[] taggedPdfBytes, McidRewritePlan plan, RemediationJobContext context);
     ```

   - Internally, it just:

     - Base64-encodes `taggedPdfBytes`
     - Serializes `plan`
     - Calls the Python endpoint
     - Returns the updated PDF bytes

   No other logic change is necessary on the .NET side for 6K.

---

## TL;DR for Claude

- **Use `ITextPdfStructureWriter`** as the structure builder integration point.
- **Extend `StructureNode`** with `Mcid` and ensure it has Role/Bounds/PageIndex.
- Implement `IMcidAssignmentService` that:
  - Walks the `StructureNode` tree in reading order.
  - Assigns per-page MCIDs.
  - Builds a `McidRewritePlan` using existing Phase 6H models.
- Update the iText writer to:
  - Build the full structure tree.
  - Attach MCR kids with `(Page, MCID)` for each leaf node with a `Mcid`.
- Ensure the external Python microservice is called **after** structure rebuild using that same `McidRewritePlan`.
- Ensure **no later phase** rewrites content streams after the microservice.

---

**Filename:**  
`PHASE-6K-6K-QA-AND-INTEGRATION-GUIDE-v1.md`
