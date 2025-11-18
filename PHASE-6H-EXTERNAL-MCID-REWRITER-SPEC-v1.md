# Phase 6H – External MCID Content Rewriter Microservice (v1)

**Goal**  
Implement a separate, low-level PDF rewriting stage that inserts BDC/EMC + MCID markers **outside** of iText7, operating purely on PDF bytes.  
This avoids iText7’s internal tagging model limitations while preserving the structure tree that iText generated.

You can give this file to Claude and say:

> “Implement everything in `PHASE-6H-EXTERNAL-MCID-REWRITER-SPEC-v1.md`.”

---

## 0. Context & Constraints

### 0.1 What we already have

From previous phases (0–6G), we already have:

- A working remediation pipeline with:
  - Preflight (Aspose, ARTIFACT-FIX moved before structure rebuild)
  - AI structure rebuild via iText7
  - A clean semantic **StructureTree** (P/H1/H2/Table/TD/TH/Figure/etc.)
  - A **mapping of logical nodes → MCID numbers** (from Phase 6b)
- Correct configuration wiring:
  - `EnableMcidLinking`, `EnableMcidContentRewrite` flow through options and job context.
- Working orchestration:
  - Only one structure rebuild per job
  - MCID work runs at the right time in the pipeline

### 0.2 What went wrong with iText7

- Using `PdfStream.SetData()` to inject BDC/EMC + `/MCID` into page content streams:
  - Works **in memory** before `pdfDoc.Close()` (non-zero BDC/EMC counts).
  - After `pdfDoc.Close()`, the final bytes have **0 BDC / 0 EMC**.
- This matches known iText7 behavior:
  - Tagged content is governed by an in-memory tagging model.
  - Direct content stream replacement is not integrated into that model.
  - At close, iText may regenerate or restore content streams, discarding our edits.

**Conclusion:**  
We cannot rely on iText7 for low-level MCID stream rewriting. We must perform MCID insertion in a separate tool that:

- Works directly on PDF bytes
- Does not maintain a competing internal tag model
- Does not overwrite our modified streams at close

---

## 1. High-Level Architecture

### 1.1 New stage in the pipeline

Add a new **Phase 6H – External MCID Rewriter** at the end of the structural pipeline:

1. Preflight (Aspose font fix, ARTIFACT-FIX, etc.)
2. AI Structure Rebuild (iText7)
3. Post-structure cleanups (that do NOT modify content streams)
4. **Phase 6H – External MCID Content Rewriter (new)**
5. Final compliance checks & output

### 1.2 Responsibilities split

- **iText7 + .NET side:**
  - Build structure tree and semantics.
  - Assign MCID numbers to structure nodes.
  - Create a **McidRewritePlan** describing:
    - Which segments of content should be associated with which MCID numbers.

- **External MCID microservice (Python, using `pikepdf` or similar):**
  - Accept the tagged PDF bytes from iText.
  - Accept the `McidRewritePlan`.
  - Rewrite page content streams:
    - Insert `/Span <</MCID n>> BDC` … `EMC` around the segments.
  - Return new PDF bytes with MCID-wrapped content streams.

The external service does **not** need to understand iText’s internal tag model; it just manipulates raw PDF content streams according to the plan from .NET.

---

## 2. Data Contracts

### 2.1 McidSegment

Represents a single content segment that should be wrapped with a specific MCID.

Properties (C# model, JSON-serializable):

- `int PageIndex`  
  - 0-based or 1-based index; pick one and be consistent.  
  - **Recommended:** 1-based to match PDF page numbering.

- `int Mcid`  
  - The MCID value to apply.

- `string Role`  
  - Optional, e.g. `"P"`, `"H1"`, `"TD"`, `"TH"`, `"Figure"`.  
  - Mostly for debugging and validation; not required for rewriting.

- `double X`, `double Y`, `double Width`, `double Height`  
  - Bounding box in PDF user units (72 dpi).  
  - Used by the microservice to find which text/image operators belong to this segment.

- `int? SequenceIndex`  
  - Optional ordering index if needed (e.g., reading order position).  
  - Useful if geometry is ambiguous.

Example JSON snippet:

{
  "PageIndex": 1,
  "Mcid": 12,
  "Role": "P",
  "X": 72.0,
  "Y": 500.0,
  "Width": 450.0,
  "Height": 60.0,
  "SequenceIndex": 5
}

### 2.2 McidRewritePlan

Represents the full mapping for a document.

Properties:

- `string DocumentId`  
  - Optional – for tracing back to a remediation job.

- `string Version`  
  - e.g. `"6H-1"` – to allow future evolution.

- `List<McidSegment> Segments`  
  - All segments across all pages.

- `bool Debug`  
  - If true, microservice can add extra diagnostics (e.g., draw visual boxes as artifacts).

Example JSON (top-level):

{
  "DocumentId": "alexandria_school_request",
  "Version": "6H-1",
  "Debug": false,
  "Segments": [
    {
      "PageIndex": 1,
      "Mcid": 0,
      "Role": "H1",
      "X": 72.0,
      "Y": 650.0,
      "Width": 468.0,
      "Height": 40.0,
      "SequenceIndex": 0
    },
    {
      "PageIndex": 1,
      "Mcid": 1,
      "Role": "P",
      "X": 72.0,
      "Y": 600.0,
      "Width": 468.0,
      "Height": 50.0,
      "SequenceIndex": 1
    }
  ]
}

---

## 3. Service API (External MCID Rewriter)

### 3.1 HTTP Endpoint

- **Method:** `POST`
- **Path:** `/api/mcid-rewrite`
- **Content-Type:** `application/json`

### 3.2 Request body

{
  "pdfBase64": "<base64-encoded PDF bytes>",
  "plan": {
    "DocumentId": "alexandria_school_request",
    "Version": "6H-1",
    "Debug": false,
    "Segments": [ /* array of McidSegment objects */ ]
  }
}

Notes:

- PDF is passed as base64 to keep things simple and self-contained.
- Plan is embedded as JSON inside.

### 3.3 Response body

On success:

{
  "success": true,
  "message": "MCID rewrite complete",
  "pdfBase64": "<base64-encoded rewritten PDF>",
  "stats": {
    "pagesProcessed": 4,
    "segmentsProcessed": 78,
    "bdcCount": 78,
    "emcCount": 78
  }
}

On failure:

{
  "success": false,
  "message": "Error description",
  "stats": null
}

---

## 4. Microservice Implementation (Python Sketch)

### 4.1 Technology choice

- **Language:** Python 3.x
- **Framework:** FastAPI or Flask (FastAPI recommended)
- **PDF library:** `pikepdf` (wrapper around qpdf) or similar.

Why `pikepdf`:

- Gives you direct access to low-level PDF objects.
- You control when/where to rewrite streams.
- No competing internal tagging model that overwrites your changes.

### 4.2 High-level flow

1. Decode `pdfBase64` to bytes.
2. Load PDF with pikepdf:
   - `pdf = pikepdf.open(io.BytesIO(pdf_bytes))`
3. Group `McidSegment`s by page.
4. For each page:
   - Get its content stream(s).
   - Decode into text/byte operators.
   - Tokenize into a list of `(operands, operator)` items.
   - Assign operators to segments based on geometry.
   - Insert BDC/EMC markers in the token list.
   - Re-serialize token list into a new content stream.
   - Replace page’s `/Contents` with the new stream.
5. Save the modified PDF to bytes.
6. Return base64 of the modified bytes plus stats.

### 4.3 Tokenization strategy

At minimum:

- Split content stream into tokens separated by whitespace.
- Recognize operators like:
  - `BT`, `ET`, `Tf`, `Tm`, `Td`, `TD`, `Tj`, `TJ`, `cm`, `Do`, `q`, `Q`.
- Maintain current text matrix / position approximations to map text to coordinates.

For simplicity in v1:

- Focus on text (`Tj`, `TJ`) and basic transforms (`Tm`, `Td`, `TD`).
- Use approximate bounding boxes:
  - Use `McidSegment` geometry as a guide.
  - If an operator’s effective position falls inside segment box → assign to that segment.

### 4.4 Inserting BDC/EMC

For each segment (per page):

- Identify the first operator index belonging to that segment (`startIndex`).
- Identify the last operator index belonging to that segment (`endIndex`).
- Insert:

  - Before `startIndex`:  
    - `/Span <</MCID n>> BDC`
  - After `endIndex`:  
    - `EMC`

If content streams already contain BDC/EMC:

- Either:
  - Strip them first (if they’re broken/incorrect), or
  - Nest carefully (more complex; phase 2+).

For v1, assume we are rewriting MCIDs from scratch and can:

- Remove any existing BDC/EMC.
- Insert our clean ones according to the plan.

---

## 5. .NET Integration

### 5.1 Producing McidRewritePlan

On the .NET side, after AI structure rebuild and layout pass:

1. Have a step (service) that:
   - Walks the `StructureTree`.
   - For each leaf-like node (P, H1–H6, TD, TH, Figure, Form fields, etc.):
     - Compute its page index.
     - Get its bounding box (`Bounds`).
     - Get assigned MCID number (from Phase 6b).
     - Create a `McidSegment`.

2. Aggregate all segments into a `McidRewritePlan`.

3. Serialize plan to JSON.

### 5.2 Calling the microservice

From C#:

- Call the external service via `HttpClient`.
- Send `pdfBase64` + plan JSON.
- On success:
  - Replace current PDF bytes with the returned rewritten bytes.
  - Log stats (`bdcCount`, `emcCount`).

Pseudo-code:

var request = new
{
    pdfBase64 = Convert.ToBase64String(currentPdfBytes),
    plan = mcidRewritePlan
};

var response = await _httpClient.PostAsJsonAsync("/api/mcid-rewrite", request);
var payload = await response.Content.ReadFromJsonAsync<McidRewriteResponse>();

if (!payload.Success)
{
    _logger.LogError("MCID rewrite failed: {Message}", payload.Message);
    // Decide: fail job or continue without MCIDs
}

var rewrittenBytes = Convert.FromBase64String(payload.PdfBase64);
return rewrittenBytes;

### 5.3 Pipeline placement

In the remediation pipeline:

- Run MCID rewrite **after**:
  - Preflight
  - AI structure rebuild
  - Any structure enhancements that might modify text or layout
- Run MCID rewrite **before**:
  - Final compliance checks
  - Returning the “best” iteration to the user

This step should be skipped if:

- `EnableMcidContentRewrite == false`, or
- There is no valid `McidRewritePlan`.

---

## 6. Diagnostics & Testing

### 6.1 Minimal test flow (Alexandria PDF)

1. Run remediation with:
   - MCID rewrite enabled.
   - External rewriter configured.
2. Confirm logs show:
   - iText structure rebuild completed.
   - MCID rewrite microservice called.
   - Non-zero `bdcCount`, `emcCount` from microservice.
3. Verify output PDF:
   - `strings output.pdf | grep -c "BDC"`
   - `strings output.pdf | grep -c "EMC"`
   - Expect both > 0.

4. Open in Acrobat/PDFix:
   - Tags panel: tags should link to highlightable content.
   - Screen reader: should be able to navigate by headings/paragraphs.

### 6.2 Fallback behavior

If the microservice fails:

- Option A: Mark the job as partially successful:
  - Tags exist, but MCID/content linking incomplete.
- Option B: Fail the job and surface error.

This can be controlled via config (e.g., `McidRewriteRequired: true/false`).

---

## 7. Summary for Claude

> - Implement an external MCID rewriter microservice as described.
> - It takes:
>   - Base64-encoded tagged PDF from iText.
>   - A `McidRewritePlan` describing which segments get which MCID.
> - It rewrites page content streams using BDC/EMC markers and returns new PDF bytes.
> - Integrate this as Phase 6H at the end of the structural pipeline, ensuring no later services overwrite streams.
> - Test with the Alexandria PDF and verify BDC/EMC presence and screen reader behavior.

---

**Filename:**  
`PHASE-6H-EXTERNAL-MCID-REWRITER-SPEC-v1.md`
