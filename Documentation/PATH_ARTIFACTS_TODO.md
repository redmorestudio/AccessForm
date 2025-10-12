# Path Artifacts Issue - TODO

## Problem
When uploading pure PDFs (not Word docs), getting "path object not tagged" accessibility errors. Decorative graphics (paths, lines, boxes) need to be marked as artifacts for PDF/UA compliance.

## Root Cause
- PDF pathway doesn't have working autotag service
- Logs show: "Both Adobe and Aspose autotag services failed or unavailable!"
- PyMuPDF can't properly create PDF/UA tags (disabled in `pdf_complete_rebuild.py`)

## Solutions (pick one)

### Option 1: Fix Autotag Services (Recommended)
Ensure Adobe/Aspose/PassportPDF autotag services succeed - they automatically mark decorative paths as artifacts.

**Files to check:**
- `Program.cs` - lines around 4211-4265 (PassportPDF autotag calls)
- Why are autotag services failing?

### Option 2: Manual Aspose Artifact Marking
Add post-processing step after Python rebuild to manually mark paths using Aspose.PDF:

```csharp
// Aspose.PDF example
foreach (var page in pdfDocument.Pages)
{
    foreach (var artifact in page.Artifacts)
    {
        if (artifact.Type == ArtifactType.Background ||
            artifact.Type == ArtifactType.Layout)
        {
            artifact.SetAsArtifact(); // Mark as artifact
        }
    }
}
```

**Files to modify:**
- Create new service: `Services/PdfArtifactMarker.cs`
- Call after Python rebuild in `Program.cs`

### Option 3: PassportPDF Autotag API
Use PassportPDF's autotag API which has artifact detection built-in.

**Reference:**
- Already using PassportPDF in `Services/PassportPdfService.cs`
- Has autotag capabilities

## Quick Fix
In Acrobat: Right-click paths → "Create Artifact" (manual workaround)

## Priority
Lower - coordinate fix is more critical. Revisit after coordinate chaos is resolved.
