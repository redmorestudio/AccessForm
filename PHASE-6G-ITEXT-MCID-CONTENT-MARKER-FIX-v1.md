# Phase 6G – Fixing ITextMcidContentMarker Stream Persistence (v1)

**Goal:**  
MCID content rewrite is now running and reporting success (e.g., `Rewrote content stream with 24 BDC/EMC pairs`), but the **PDF bytes returned by `StructureRebuildService` still have 0 BDC/EMC**. Logs show:

- ✅ `[MCID-MARKER-6b] Rewrote content stream with 24 BDC/EMC pairs`
- ❌ `[STRUCTURE-REBUILD-DEBUG] PDF has 0 BDC and 0 EMC markers`

This means:

> `ITextMcidContentMarker` is correctly computing the new content streams in memory, but those changes are **not actually being persisted into the `PdfDocument` that is ultimately written out.** They are effectively discarded when iText closes the document.

This spec defines how to:

1. Make `ITextMcidContentMarker` **apply** MCID-wrapped content back into the `PdfDocument`.
2. Avoid accidental “shadow documents” whose changes are never returned.
3. Ensure the final PDF bytes **actually contain** the modified content streams.

Give this file to Claude and say:

> “Implement everything in `PHASE-6G-ITEXT-MCID-CONTENT-MARKER-FIX-v1.md`.”

---

## 0. Likely Failure Modes (What’s Going Wrong Now)

Based on the behavior, one (or more) of these is happening inside `ITextMcidContentMarker`:

1. **Working on a separate `PdfDocument` instance** created from the original bytes, then returning the *unmodified* main document.
2. **Modifying in-memory operator lists** but never writing the rewritten sequence back into the actual page content streams.
3. **Writing to a different `PdfStream`** that is not plugged into the page’s `/Contents` entry.
4. Closing a document backed by a different `MemoryStream` than the one whose bytes are returned to the caller.

Net effect: MCIDs are computed and “rewritten” conceptually, but never actually stored in the `PdfDocument` the pipeline is using.

---

## 1. Desired Ownership Model

`ITextMcidContentMarker` should be responsible for:

- Taking an existing **reader+writer** `PdfDocument` (the same one used for structure rebuild).
- For each page:
  - Read that page’s content stream(s).
  - Rewrite them with BDC/EMC.
  - Write the new bytes **back into that page’s `/Contents`**.
- NOT closing or disposing the document.
- Returning control to the caller, which will later close the document and extract bytes.

High-level signature:

```csharp
public interface IITextMcidContentMarker
{
    void RewriteContentStreamsWithMcids(
        PdfDocument pdfDoc,
        StructureTree structure,
        PageLayoutPlan layoutPlan,
        AccessibilityRemediationSettings settings,
        StructureRebuildContext context);
}
```

Note:

- Return type is `void` – the **document is modified in-place**.
- The caller (e.g., `ITextPdfStructureWriter` / `StructureRebuildService`) owns:
  - Creating `PdfDocument(reader, writer)`
  - Closing it
  - Returning bytes to the pipeline

---

## 2. Correct Page-Level Rewrite Pattern

Inside `ITextMcidContentMarker`, for each page:

### 2.1 Get the page and its content streams

In iText 7 / C#:

```csharp
var pageCount = pdfDoc.GetNumberOfPages();
for (int pageIndex = 1; pageIndex <= pageCount; pageIndex++)
{
    var page = pdfDoc.GetPage(pageIndex);

    // Option A: if single stream
    var originalBytes = page.GetContentBytes();

    // Option B: if multiple content streams
    // var streams = page.GetContentStreams();
    // Concatenate or handle each separately, depending on your design.
}
```

The exact API may vary slightly depending on your iText version; Claude should match what you already use elsewhere.

### 2.2 Parse → transform → serialize

Your existing Phase 6b logic likely already:

1. Parses `originalBytes` into a sequence of `PdfOp` / operator tokens.
2. Groups them into segments and associates them with MCIDs.
3. Inserts BDC/EMC operators around those segments.
4. Serializes that back into a `byte[] rewrittenBytes`.

The key missing step is to **attach `rewrittenBytes` back to the page.**

### 2.3 Write back to the page’s `/Contents`

You must replace the page’s content stream(s) with the rewritten data. There are two main ways.

#### Option A – Replace the single content stream

If you treat each page as having one unified stream:

```csharp
var pageDict = page.GetPdfObject();

// Create a new stream with rewritten bytes
var newStream = new PdfStream(rewrittenBytes);
newStream.MakeIndirect(pdfDoc);

// Replace /Contents entry
pageDict.Put(PdfName.Contents, newStream);
```

Or, if your version has a helper:

```csharp
page.GetContentStream(0).SetData(rewrittenBytes);
```

Claude should choose the approach that best matches your existing usage of `PdfStream` elsewhere in the repo.

#### Option B – Split across multiple streams

If you decided to keep multiple content streams per page (e.g., one per logical segment), you must:

1. Create a `PdfArray` of `PdfStream` objects.
2. Put that array into the `PdfPage`’s `/Contents`:

```csharp
var contentsArray = new PdfArray();
foreach (var streamBytes in perStreamBytes)
{
    var s = new PdfStream(streamBytes);
    s.MakeIndirect(pdfDoc);
    contentsArray.Add(s);
}

page.GetPdfObject().Put(PdfName.Contents, contentsArray);
```

But in most cases, **a single rewritten stream per page is simplest** and safest for now.

---

## 3. Ensure You Don’t Write to a Shadow Document

A common pattern that causes this symptom is:

```csharp
using (var ms = new MemoryStream())
{
    using (var pdfDoc = new PdfDocument(new PdfReader(new MemoryStream(originalBytes)), new PdfWriter(ms)))
    {
        // Modify pdfDoc...
    }

    return ms.ToArray();
}
```

This is OK **if** the caller returns those bytes directly.

However, if your actual flow does:

1. `StructureRebuildService` opens one `PdfDocument(reader, writer)` over `jobPdfStream`.
2. `ITextMcidContentMarker` secretly opens a **second** `PdfDocument` inside itself using `new PdfReader(originalBytes)`.
3. It modifies **that second doc** and closes it.
4. But the pipeline returns bytes from the **first doc**, which was never modified.

Then MCID markers are effectively thrown away.

**Fix:**

- `ITextMcidContentMarker` should **never** create its own `PdfDocument` from raw bytes for Phase 6b.
- It should **only** operate on the `PdfDocument` given to it by the caller.

If you truly need to construct a secondary document (e.g., for tests), make sure that in the real remediation pipeline you use the shared document.

---

## 4. Caller Responsibilities (StructureRebuildService / Writer)

The caller (e.g., `ITextPdfStructureWriter`) should:

1. Create a reader/writer `PdfDocument` around the **current input PDF bytes**:

   ```csharp
   using var reader = new PdfReader(new MemoryStream(inputPdf));
   using var ms = new MemoryStream();
   using var writer = new PdfWriter(ms);
   using var pdfDoc = new PdfDocument(reader, writer);
   ```

2. Build the structure tree, etc.
3. Call `ITextMcidContentMarker.RewriteContentStreamsWithMcids(pdfDoc, structure, layoutPlan, settings, context);`
4. Close the `pdfDoc` (disposing the writer and flushing)
5. Return `ms.ToArray()` as the output PDF bytes for that step:

   ```csharp
   pdfDoc.Close();
   return ms.ToArray();
   ```

Important:

- `ITextMcidContentMarker` **must not** close the document; it just mutates it.
- There must be exactly **one** `PdfDocument` per pass through this step.

---

## 5. Diagnostics & Logging to Prove It’s Fixed

Add some logging around the key points.

### 5.1 Inside `ITextMcidContentMarker`

For each page before and after writing:

```csharp
_logger.LogDebug(
    "[MCID-MARKER-6b] Page {PageIndex}: original content length={Original}, rewritten length={Rewritten}",
    pageIndex,
    originalBytes.Length,
    rewrittenBytes.Length);
```

After you write the new stream:

- Optional sanity check:

  ```csharp
  var verifyBytes = page.GetContentBytes();
  _logger.LogDebug(
      "[MCID-MARKER-6b] Page {PageIndex}: verify content length={Len}.",
      pageIndex,
      verifyBytes.Length);
  ```

### 5.2 After closing the PdfDocument (caller)

In `StructureRebuildService` or equivalent:

```csharp
_logger.LogInformation(
    "[STRUCTURE-REBUILD-DEBUG] After MCID rewrite and close: PDF size={Size} bytes.",
    ms.Length);
```

Optionally, you can even scan the output bytes in debug builds:

```csharp
var finalBytes = ms.ToArray();
var bdcCount = CountSubstring(finalBytes, Encoding.ASCII.GetBytes("BDC"));
var emcCount = CountSubstring(finalBytes, Encoding.ASCII.GetBytes("EMC"));

_logger.LogInformation(
    "[STRUCTURE-REBUILD-DEBUG] After close: PDF has {BdcCount} BDC and {EmcCount} EMC markers.",
    bdcCount,
    emcCount);
```

---

## 6. Testing Checklist

Claude should:

1. Update `ITextMcidContentMarker` to:
   - Operate on the **shared `PdfDocument`** passed in.
   - For each page: get content bytes → rewrite → write back to `/Contents`.
   - Not create/close its own `PdfDocument`.
2. Confirm that `StructureRebuildService`:
   - Creates exactly one reader/writer `PdfDocument` for the step.
   - Calls `RewriteContentStreamsWithMcids` on that document.
   - Closes it and returns the bytes from the same stream.
3. Run the Alexandria remediation again.
4. Check logs for:

   - `[MCID-MARKER-6b] Rewrote content stream with 24 BDC/EMC pairs`
   - `[STRUCTURE-REBUILD-DEBUG] After close: PDF has {BdcCount} BDC and {EmcCount} EMC markers.`

5. Validate final output:

   ```bash
   strings alexandria_school_remediated.pdf | grep -c "BDC"
   strings alexandria_school_remediated.pdf | grep -c "EMC"
   ```

   Expected: both > 0.

6. Open in Acrobat/PDFix:
   - Tags navigate to content.
   - MCID-linked navigation works.

---

## 7. Summary for Claude

> - Treat `ITextMcidContentMarker` as an **in-place mutator** of the existing `PdfDocument`, not as an independent document pipeline.
> - For each page, read content, rewrite with BDC/EMC, then **write the new bytes back to the page’s `/Contents`**.
> - Do not create a shadow `PdfDocument` from original bytes; use the shared one from `StructureRebuildService`.
> - The caller is responsible for closing the document and returning the bytes.
> - Add logging to confirm that after close, the final PDF bytes contain BDC/EMC markers.

---

**Filename:**  
`PHASE-6G-ITEXT-MCID-CONTENT-MARKER-FIX-v1.md`
