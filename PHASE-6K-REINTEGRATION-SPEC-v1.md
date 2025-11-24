# Phase 6K Reintegration Spec — Bolt MCID Flow Back Into Full Remediation Pipeline (v1)

**Purpose:**  
Reattach the completed MCID pipeline (6F–6K + external microservice) **into the full remediation pipeline** without disturbing any existing remediation logic.  
This spec tells Claude *exactly* what to do.

---

# 1. Goals

1. **Keep 100% of existing remediation code. No rewrites.**
2. **Reinsert MCID assignment + MCR creation + external rewrite into the existing structure rebuild path.**
3. **Ensure MCID markers survive the pipeline.**
4. **Ensure AI remediation + structure building + form/figure enrichment remain untouched.**
5. **Guarantee ordering so no later service overwrites content streams.**

---

# 2. Integration Points

## 2.1 Existing Services That Stay AS-IS
- LogicalLayoutAnalysisService  
- ClaudeVisionLayoutAnalysis  
- Table/figure/form enrichment  
- StructureTreeBuilder  
- StructureTreeCleaner  
- FormFieldEnrichmentService  
- StructureEnhancementService  
- Preflight (Aspose, ArtifactFix)  
- Post-remediation cleanup  
- Compliance iteration pipeline  

**These remain unchanged.**

---

# 3. MCID Integration Points (Phase 6K)

## 3.1 Insert MCID Assignment Into StructureRebuildService

Inside `StructureRebuildService`, after structure tree is built and cleaned:

```
structureTree = builder.Build(logicalDoc)
cleaner.Clean(structureTree)

if options.EnableMcidContentRewrite:
    mcidAssignment.AssignMcids(structureTree)
```

Requirements for `AssignMcids`:
- Walk structure tree in *reading order* (child order unless LayoutPlan exists).
- Track MCIDs per page.
- For each leaf node, create one or more `McidReference` entries with:
  - PageIndex
  - Mcid
  - Optional bounds
  - SequenceIndex
  - Role

These populate the existing `StructureNode.McidReferences` list.  
**Do NOT create new models.**

---

# 4. Structure Writer Integration

## 4.1 Update ITextPdfStructureWriter

Inside the structure writer:

```
if options.EnableMcidContentRewrite:
    AllocateMcids(structureTree)          # now uses mcidAssignment
    CreateMcrKids(structureTree)          # see below
    rewritePlan = planBuilder.Build(structureTree)
    pdfBytes = externalRewriter.Rewrite(pdfBytes, rewritePlan)
```

## 4.2 CreateMcrKids (new)

For each `StructureNode`:

```
elem = CreateStructElem(role)

foreach (ref in node.McidReferences):
    page = pdfDoc.GetPage(ref.PageIndex)
    mcr = new PdfMcrNumber(page, ref.Mcid)
    elem.AddKid(mcr)
```

**No other changes to writer. No content stream manipulation here.**

---

# 5. External MCID Rewriter Integration

## 5.1 Keep existing ExternalMcidRewriterService

But ensure:

- It executes **after** MCR kids are created.
- It receives a complete `McidRewritePlan` from the existing builder.

## 5.2 Ordering Rules

1. Preflight (font fixes, artifact fixes) → OK  
2. Structure rebuild (assign MCIDs + build struct tree) → OK  
3. External MCID rewrite (Python) → **MUST be last**  
4. Any service after rewrite that touches content streams → **DISABLED or guarded**

---

# 6. Guard Clauses to Prevent Overwrites

After external rewrite runs, set:

```
context.McidContentRewriteExecuted = true;
```

In any service that touches content streams:

```
if (context.McidContentRewriteExecuted)
    return pdfBytes;    // DO NOT alter content
```

This applies ONLY to:
- Artifact fix (content stream mutator)
- Any post-processing modifying streams
- Any structure enhancement unit touching streams

All other remediation stays intact.

---

# 7. Delete / Ignore Legacy MCID Code

## 7.1 IContentMcidMarker
- Leave interface for DI compatibility
- Ensure it is **not used** for MCID assignment or rewriting

## 7.2 Old AllocateMcids logic
- Replace body with Phase 6K logic (or delegate to a new `McidAssignmentService`)

---

# 8. DI Registration

Register:

```
services.AddScoped<IMcidAssignmentService, McidAssignmentService>();
```

Ensure ITextPdfStructureWriter and StructureRebuildService receive it via constructor injection.

---

# 9. Testing Flow

### Run a full remediation:

```
AI → LogicalLayout → StructureTreeBuilder → Cleaner → AssignMcids → Build MCR → External Rewrite → Return PDF
```

### Test validation:
- PDFix: clicking tags highlights correct content
- Acrobat: tags show MCID-linked content
- strings output: BDC+EMC present
- No corruption
- All other remediation preserved (alt text, tables, forms, headings)

---

# 10. Requirements Summary for Claude

When implementing:

- **DO NOT modify existing remediation logic**
- **ONLY add MCID assignment + MCR creation + external rewrite wiring**
- **Preserve entire existing pipeline**
- **Prevent any post-MCID rewrite content stream mutations**
- **Use existing `McidReferences`, `McidRewritePlan`, and external Python service**
- **Follow this spec exactly**

---

**Filename:**  
`PHASE-6K-REINTEGRATION-SPEC-v1.md`
