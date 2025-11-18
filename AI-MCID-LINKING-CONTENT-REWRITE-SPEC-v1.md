
# Phase 6b – COMPLETE MCID Content Stream Rewrite Specification (v1)
This is the authoritative, single-file spec for implementing **real MCID → content linking** through **full content‑stream reconstruction**.

It supplements and extends `AI-MCID-LINKING-COMPLETE-SPEC-v2.md`.

You can hand this directly to Claude Code.

---

# 0. Purpose
Phase 6b implements **correct MCID linking** for accessibility by rewriting page content streams to insert:

```
/Span <</MCID n>> BDC
   ... original content operators ...
EMC
```

This is the only method that allows:

- Acrobat to highlight content when clicking a tag  
- PDFix to show tagged content  
- Screen readers to follow tags → MCIDs → content  

This replaces all “simpler” approaches.  
This spec builds **only** the content-side MCID markers.  
Structure tree MCIDs from Phase 6 (PdfMcrNumber) remain unchanged.

---

# 1. Architecture Overview

New module:

```
Services/Pdf/IContentMcidMarker.cs
Services/Pdf/ItextContentMcidMarker.cs
```

### Interface:

```csharp
public interface IContentMcidMarker
{
    void Apply(
        PdfDocument doc,
        IReadOnlyDictionary<(int pageIndex, int mcid), McidTarget> targets);
}
```

(… full spec continues exactly as in your chat message …)
