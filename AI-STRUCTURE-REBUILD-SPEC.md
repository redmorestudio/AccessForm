
# AccessForm AI Structural Remediation Spec  
**Single-source spec for Claude Code / AI agents**  
_File: `AI-STRUCTURE-REBUILD-SPEC.md` (drop this into the repo root or `/Markdown Specs/ access-form/`)_

---

## 0. Purpose and Scope

This document is a **complete, single-artifact specification** for adding an **AI-driven structural remediation subsystem** to the AccessForm project (https://github.com/redmorestudio/accessform).

It is written so that an AI coding agent (Claude Code, OpenAI, etc.) can follow it step-by-step and produce a working implementation **inside the existing codebase**, without needing any additional context.

### High-level goal

Current state:

- AccessForm already performs PDF field detection and various accessibility fixes.
- Some PDFs (especially Illustrator-generated brochures, timetables, transit schedules, etc.) are technically “tagged” and pass checkers, but the **tag structure is trash** and screen readers behave badly.

Goal of this spec:

> Add a modular, AI-powered pipeline that can **analyze the visual layout of a PDF**, construct a **semantic logical document model**, and then **rebuild the PDF tag tree** from that model, integrating cleanly into the existing remediation loop.

Concretely, we want this pipeline in the code:

```text
PDF → AI Layout Analysis → LogicalDocument → StructureTree → PDF Tag Writer → Remediated PDF
```

The rest of the system (VeraPDF validation, metadata fixes, table scope adjustments, etc.) should continue to work as-is, but now can operate on a much cleaner structure.

---

## 1. Architectural Overview

This spec introduces three main abstractions:

1. **LogicalDocument** (semantic model)
   - A language- and backend-neutral representation of the document content:
     - pages, headings, paragraphs, tables, figures, etc.
   - This is the **output** of AI layout analysis.

2. **StructureTree** (PDF-neutral structure model)
   - A tree of semantic nodes: H1, P, Table, TR, TH, TD, Figure, etc.
   - This is the **input** to the PDF tag writer.

3. **IPdfStructureWriter** (PDF writer interface)
   - A single interface responsible for applying a `StructureTree` to a PDF:
     - creating `/StructTreeRoot`
     - role mapping
     - MCIDs
     - tagging artifacts
   - Implementation can be .NET-only, Python (pikepdf), or a combination.

A new remediation service, **StructureRebuildService**, plugs into the existing remediation pipeline under the “structure fixes” stage and drives the above pipeline for PDFs that need it.

---

## 2. New Models: Logical Document Layer

> **Task:** Create new C# record types to represent the semantic structure of a document.

### 2.1. File layout

Create a folder for logical models. If there is already a preferred place for models (e.g., `Models/`), follow that; otherwise:

- `Models/Logical/LogicalDocument.cs`
- `Models/Logical/LogicalPage.cs`
- `Models/Logical/LogicalBlock.cs`
- `Models/Logical/Rect.cs`
- `Models/Logical/HeadingBlock.cs`
- `Models/Logical/ParagraphBlock.cs`
- `Models/Logical/TableBlock.cs`
- `Models/Logical/TableRow.cs`
- `Models/Logical/TableCell.cs`
- `Models/Logical/FigureBlock.cs`

Adjust namespace to match the existing project (e.g., `namespace AccessForm.Logical;` or similar). For this spec we’ll use `AccessForm.Logical` as a placeholder; Claude should align it with the real namespaces used in the repo.

### 2.2. Rect.cs

```csharp
namespace AccessForm.Logical;

/// <summary>
/// Simple rectangle in PDF page coordinates (units consistent with your PDF library).
/// </summary>
public readonly record struct Rect(double X, double Y, double Width, double Height);
```

### 2.3. LogicalBlock.cs

```csharp
namespace AccessForm.Logical;

/// <summary>
/// Base type for all logical blocks on a page: headings, paragraphs, tables, figures, etc.
/// </summary>
public abstract record LogicalBlock(Rect Bounds);
```

### 2.4. LogicalPage.cs

```csharp
namespace AccessForm.Logical;

/// <summary>
/// Represents a single page of logical content produced by AI layout analysis.
/// </summary>
public record LogicalPage(
    int PageNumber,
    IReadOnlyList<LogicalBlock> Blocks);
```

### 2.5. LogicalDocument.cs

```csharp
namespace AccessForm.Logical;

/// <summary>
/// Represents a full document's logical structure across all pages.
/// </summary>
public record LogicalDocument(IReadOnlyList<LogicalPage> Pages);
```

### 2.6. HeadingBlock.cs

```csharp
namespace AccessForm.Logical;

/// <summary>
/// A heading (H1, H2, etc.) on the page.
/// </summary>
public record HeadingBlock(
    Rect Bounds,
    int Level,
    string Text
) : LogicalBlock(Bounds);
```

### 2.7. ParagraphBlock.cs

```csharp
namespace AccessForm.Logical;

/// <summary>
/// A semantic paragraph (one or more visual text runs merged together).
/// </summary>
public record ParagraphBlock(
    Rect Bounds,
    string Text
) : LogicalBlock(Bounds);
```

### 2.8. Table-related blocks

```csharp
namespace AccessForm.Logical;

/// <summary>
/// A single table cell. Row/Column indices are 0-based within the table.
/// </summary>
public record TableCell(
    int RowIndex,
    int ColumnIndex,
    bool IsHeader,
    string Text);

/// <summary>
/// A single row within a table, containing a set of cells.
/// </summary>
public record TableRow(IReadOnlyList<TableCell> Cells);

/// <summary>
/// A table block representing a logical table on the page.
/// </summary>
public record TableBlock(
    Rect Bounds,
    IReadOnlyList<TableRow> Rows
) : LogicalBlock(Bounds);
```

### 2.9. FigureBlock.cs

```csharp
namespace AccessForm.Logical;

/// <summary>
/// A figure or image on the page. May be decorative or have alt text.
/// </summary>
public record FigureBlock(
    Rect Bounds,
    string? AltTextSuggestion,
    bool IsLikelyDecorative
) : LogicalBlock(Bounds);
```

At this point, **no behavior** is implemented—these are pure data containers.

---

## 3. AI Layout Analysis Interface

> **Task:** Define a service that converts a PDF file into a `LogicalDocument` using AI/vision.

### 3.1. LogicalLayoutOptions.cs

Create `Services/Analysis/LogicalLayoutOptions.cs`:

```csharp
namespace AccessForm.Services.Analysis;

/// <summary>
/// Options controlling logical layout analysis behavior.
/// </summary>
public sealed class LogicalLayoutOptions
{
    /// <summary>
    /// Whether to attempt to detect and include figures.
    /// </summary>
    public bool IncludeFigures { get; init; } = true;

    /// <summary>
    /// Whether to attempt to detect and include tables.
    /// </summary>
    public bool IncludeTables { get; init; } = true;

    /// <summary>
    /// If true, prefer AI-determined heading levels even if a tag tree exists.
    /// </summary>
    public bool PreferAiHeadings { get; init; } = true;

    /// <summary>
    /// Maximum pages to analyze. 0 = all pages.
    /// </summary>
    public int MaxPages { get; init; } = 0;
}
```

### 3.2. ILogicalLayoutAnalysisService.cs

Create `Services/Analysis/ILogicalLayoutAnalysisService.cs`:

```csharp
using AccessForm.Logical;

namespace AccessForm.Services.Analysis;

/// <summary>
/// Analyzes a PDF's visual and textual layout to produce a logical document model.
/// Backed by AI/vision models in concrete implementations.
/// </summary>
public interface ILogicalLayoutAnalysisService
{
    Task<LogicalDocument> AnalyzeAsync(
        byte[] pdfBytes,
        LogicalLayoutOptions options,
        CancellationToken cancellationToken = default);
}
```

### 3.3. StubLogicalLayoutAnalysisService.cs

Create an initial stub implementation in `Services/Analysis/StubLogicalLayoutAnalysisService.cs`. This will be replaced later by a true AI-backed implementation, but is needed to wire the pipeline and compile.

```csharp
using AccessForm.Logical;

namespace AccessForm.Services.Analysis;

/// <summary>
/// Temporary stub implementation of ILogicalLayoutAnalysisService.
/// Returns a trivial LogicalDocument for plumbing and testing.
/// </summary>
public sealed class StubLogicalLayoutAnalysisService : ILogicalLayoutAnalysisService
{
    public Task<LogicalDocument> AnalyzeAsync(
        byte[] pdfBytes,
        LogicalLayoutOptions options,
        CancellationToken cancellationToken = default)
    {
        var page = new LogicalPage(
            PageNumber: 1,
            Blocks: new List<LogicalBlock>
            {
                new ParagraphBlock(
                    new Rect(0, 0, 100, 20),
                    "AI layout analysis not yet implemented.")
            });

        var doc = new LogicalDocument(new[] { page });
        return Task.FromResult(doc);
    }
}
```

Later, the stub implementation will be replaced with a real service that:

- Renders pages to images (e.g., via a PDF library or external tool).
- Calls Claude / OpenAI / other vision models.
- Interprets their output into `LogicalBlock` instances.

---

## 4. StructureTree: PDF-neutral Structural Model

> **Task:** Create a tree model that describes the tag structure independently of the PDF backend.

### 4.1. Files and namespaces

Create folder `Services/Remediation/Structure/` and add:

- `StructureTree.cs`
- `StructureNode.cs`
- `StructureTreeBuilder.cs`

Use namespace `AccessForm.Services.Remediation.Structure` (adjust if needed).

### 4.2. StructureNode.cs

```csharp
namespace AccessForm.Services.Remediation.Structure;

/// <summary>
/// Represents a node in the semantic structure tree that will be mapped to PDF tags.
/// </summary>
public record StructureNode(
    string Role,     // e.g. "Document", "H1", "P", "Table", "TR", "TH", "TD", "Figure"
    string? TextContent,
    IReadOnlyDictionary<string, string>? Attributes,
    IReadOnlyList<StructureNode>? Children);
```

### 4.3. StructureTree.cs

```csharp
namespace AccessForm.Services.Remediation.Structure;

/// <summary>
/// Root container for a document's semantic structure.
/// </summary>
public record StructureTree(
    IReadOnlyList<StructureNode> Nodes);
```

### 4.4. StructureTreeBuilder.cs (initial stub)

```csharp
using AccessForm.Logical;

namespace AccessForm.Services.Remediation.Structure;

/// <summary>
/// Converts a LogicalDocument into a StructureTree suitable for PDF tag writing.
/// This initial version is a stub; it will be enhanced to handle headings, tables, figures, etc.
/// </summary>
public static class StructureTreeBuilder
{
    public static StructureTree Build(LogicalDocument doc)
    {
        var nodes = new List<StructureNode>
        {
            new StructureNode(
                Role: "Document",
                TextContent: null,
                Attributes: null,
                Children: BuildChildren(doc))
        };

        return new StructureTree(nodes);
    }

    private static IReadOnlyList<StructureNode> BuildChildren(LogicalDocument doc)
    {
        var children = new List<StructureNode>();

        foreach (var page in doc.Pages)
        {
            foreach (var block in page.Blocks)
            {
                switch (block)
                {
                    case HeadingBlock heading:
                        children.Add(new StructureNode(
                            Role: $"H{heading.Level}",
                            TextContent: heading.Text,
                            Attributes: null,
                            Children: null));
                        break;

                    case ParagraphBlock paragraph:
                        children.Add(new StructureNode(
                            Role: "P",
                            TextContent: paragraph.Text,
                            Attributes: null,
                            Children: null));
                        break;

                    case TableBlock table:
                        children.Add(BuildTableNode(table));
                        break;

                    case FigureBlock figure:
                        children.Add(BuildFigureNode(figure));
                        break;

                    default:
                        // Ignore unknown block types for now.
                        break;
                }
            }
        }

        return children;
    }

    private static StructureNode BuildTableNode(TableBlock table)
    {
        var rowNodes = new List<StructureNode>();

        foreach (var row in table.Rows)
        {
            var cellNodes = new List<StructureNode>();
            foreach (var cell in row.Cells)
            {
                var role = cell.IsHeader ? "TH" : "TD";
                cellNodes.Add(new StructureNode(
                    Role: role,
                    TextContent: cell.Text,
                    Attributes: new Dictionary<string, string>
                    {
                        ["row"] = cell.RowIndex.ToString(),
                        ["col"] = cell.ColumnIndex.ToString()
                    },
                    Children: null));
            }

            rowNodes.Add(new StructureNode(
                Role: "TR",
                TextContent: null,
                Attributes: null,
                Children: cellNodes));
        }

        return new StructureNode(
            Role: "Table",
            TextContent: null,
            Attributes: null,
            Children: rowNodes);
    }

    private static StructureNode BuildFigureNode(FigureBlock figure)
    {
        var attributes = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(figure.AltTextSuggestion))
        {
            attributes["alt"] = figure.AltTextSuggestion!;
        }
        attributes["decorative"] = figure.IsLikelyDecorative ? "true" : "false";

        return new StructureNode(
            Role: "Figure",
            TextContent: null,
            Attributes: attributes,
            Children: null);
    }
}
```

Later you can refine this to add more grouping semantics (sections, lists, sidebars, etc.), but this is enough to drive a correct tag tree for many documents.

---

## 5. IPdfStructureWriter: PDF Backend Abstraction

> **Task:** Create an interface that takes a PDF and a `StructureTree`, and returns a new PDF with tags applied.

### 5.1. IPdfStructureWriter.cs

Create `Services/Pdf/IPdfStructureWriter.cs`:

```csharp
using AccessForm.Services.Remediation.Structure;

namespace AccessForm.Services.Pdf;

/// <summary>
/// Applies a StructureTree to a PDF and returns a new tagged PDF.
/// </summary>
public interface IPdfStructureWriter
{
    /// <summary>
    /// Rewrites the provided PDF bytes with a new tag structure.
    /// </summary>
    /// <param name="originalPdf">Original PDF bytes.</param>
    /// <param name="tree">Structure tree describing the desired tag structure.</param>
    /// <returns>New PDF bytes with tags updated.</returns>
    byte[] Rewrite(byte[] originalPdf, StructureTree tree);
}
```

### 5.2. StubPdfStructureWriter.cs

Create `Services/Pdf/StubPdfStructureWriter.cs` as a placeholder:

```csharp
using AccessForm.Services.Remediation.Structure;

namespace AccessForm.Services.Pdf;

/// <summary>
/// Stub implementation of IPdfStructureWriter that returns the original PDF unchanged.
/// </summary>
public sealed class StubPdfStructureWriter : IPdfStructureWriter
{
    public byte[] Rewrite(byte[] originalPdf, StructureTree tree)
    {
        // TODO: replace with real implementation using a PDF library or Python microservice.
        return originalPdf;
    }
}
```

Later, implement this using:

- A .NET PDF library (Syncfusion, iText 7, etc.), or
- A Python microservice using `pikepdf` and a JSON API, called from .NET.

---

## 6. StructureRebuildService: New Remediation Service

> **Task:** Integrate AI structural rebuild into the existing remediation pipeline as a service.

### 6.1. StructureRebuildService.cs

Create `Services/Remediation/StructureRebuildService.cs`:

```csharp
using AccessForm.Logical;
using AccessForm.Services.Analysis;
using AccessForm.Services.Pdf;
using AccessForm.Services.Remediation.Structure;

namespace AccessForm.Services.Remediation;

/// <summary>
/// Remediation service that rebuilds PDF tag structure based on AI-derived logical layout.
/// </summary>
public sealed class StructureRebuildService : IRemediationService
{
    private readonly ILogicalLayoutAnalysisService _layout;
    private readonly IPdfStructureWriter _writer;

    public StructureRebuildService(
        ILogicalLayoutAnalysisService layout,
        IPdfStructureWriter writer)
    {
        _layout = layout;
        _writer = writer;
    }

    public async Task<RemediationResult> RemediateAsync(
        RemediationContext context,
        CancellationToken ct = default)
    {
        // 1. Run AI layout analysis
        var logical = await _layout.AnalyzeAsync(
            context.CurrentPdfBytes,
            new LogicalLayoutOptions(),
            ct);

        if (!ShouldRebuild(logical, context))
        {
            return RemediationResult.NoChange();
        }

        // 2. Build semantic structure tree
        var structure = StructureTreeBuilder.Build(logical);

        // 3. Write new PDF tags
        var updated = _writer.Rewrite(context.CurrentPdfBytes, structure);

        return RemediationResult.Updated(updated);
    }

    private static bool ShouldRebuild(LogicalDocument logical, RemediationContext context)
    {
        // Initial heuristic: always rebuild.
        // Later, you can restrict this to documents that:
        // - have no meaningful structure, or
        // - come from certain pipelines (e.g., Illustrator exports), or
        // - fail certain structure-related checks.
        return true;
    }
}
```

> **Note:** The types `IRemediationService`, `RemediationResult`, and `RemediationContext` are assumed to already exist in the repo (per your architecture docs). If names differ, adjust accordingly.

### 6.2. Adding to the remediation pipeline

Locate the code where remediation services are registered / composed (likely under `Services/Remediation` or in DI setup). Insert `StructureRebuildService` in the **structure fix** phase, **before** smaller fixers like table scope, form role fixes, etc.

Conceptually, the order should look like:

```text
Metadata Fixes:
  - PdfUaMetadataService
  - Language/Title/DocumentInfo services

Structure Fixes:
  - StructureRebuildService          // NEW
  - TableStructureValidationService
  - TableScopeAttributeFixService
  - FormRoleAttributeFixService
  - FormWidgetNestingFixService

Content Fixes:
  - FigureAltTextService
  - ArtifactTaggedContentFixService
  - WhitespaceServiceAdapter
  - etc.

AI Fallback:
  - GptRemediationService / ClaudeRemediationService
```

Adjust the actual code to match your existing service registration pattern.

---

## 7. Dependency Injection Registration

> **Task:** Wire the new services (`ILogicalLayoutAnalysisService`, `IPdfStructureWriter`, `StructureRebuildService`) into the DI container.

Locate the DI configuration code (often `Program.cs`, `Startup.cs`, or a dedicated `ServiceCollectionExtensions` file). Add registrations similar to:

```csharp
using AccessForm.Services.Analysis;
using AccessForm.Services.Pdf;
using AccessForm.Services.Remediation;

// inside method where services are configured:
services.AddSingleton<ILogicalLayoutAnalysisService, StubLogicalLayoutAnalysisService>();
services.AddSingleton<IPdfStructureWriter, StubPdfStructureWriter>();

// StructureRebuildService should be registered as an IRemediationService
services.AddSingleton<IRemediationService, StructureRebuildService>();
```

If the remediation pipeline uses a specific pattern (e.g., an ordered list injected as `IEnumerable<IRemediationService>`), ensure that `StructureRebuildService` appears in the correct relative order.

---

## 8. Test Artifacts and Grounding Example

> **Task:** Provide a sample logical structure file and basic tests to validate the new abstractions.

### 8.1. Sample logical model: Route 12 brochure

Create `tests/logical/route12.json` (or similar test folder). This file won’t be loaded by the app in production; it’s for tests and as a reference for AI layout outputs.

```json
{
  "pages": [
    {
      "pageNumber": 1,
      "blocks": [
        {
          "type": "heading",
          "level": 1,
          "text": "ROUTE 12 – ALBION",
          "bounds": { "x": 50, "y": 50, "width": 400, "height": 40 }
        },
        {
          "type": "heading",
          "level": 2,
          "text": "WEEKDAY SERVICE",
          "bounds": { "x": 50, "y": 100, "width": 300, "height": 30 }
        },
        {
          "type": "heading",
          "level": 2,
          "text": "FARE INFORMATION",
          "bounds": { "x": 50, "y": 150, "width": 300, "height": 30 }
        },
        {
          "type": "table",
          "bounds": { "x": 50, "y": 190, "width": 300, "height": 200 },
          "rows": [
            {
              "cells": [
                { "rowIndex": 0, "columnIndex": 0, "isHeader": true,  "text": "Fare Type" },
                { "rowIndex": 0, "columnIndex": 1, "isHeader": true,  "text": "Price" }
              ]
            },
            {
              "cells": [
                { "rowIndex": 1, "columnIndex": 0, "isHeader": false, "text": "Full Fare" },
                { "rowIndex": 1, "columnIndex": 1, "isHeader": false, "text": "$1.65" }
              ]
            },
            {
              "cells": [
                { "rowIndex": 2, "columnIndex": 0, "isHeader": false, "text": "Transfer" },
                { "rowIndex": 2, "columnIndex": 1, "isHeader": false, "text": "$0.45" }
              ]
            }
          ]
        },
        {
          "type": "heading",
          "level": 2,
          "text": "UNIVERSITY STUDENTS",
          "bounds": { "x": 50, "y": 410, "width": 350, "height": 30 }
        },
        {
          "type": "paragraph",
          "text": "All university students of PennWest Edinboro, Gannon, Mercyhurst, and Penn State Behrend ride FREE with your university-issued ID card and current eSticker.",
          "bounds": { "x": 50, "y": 450, "width": 500, "height": 60 }
        },
        {
          "type": "figure",
          "bounds": { "x": 350, "y": 200, "width": 300, "height": 300 },
          "altTextSuggestion": "Map of Route 12 bus route between Downtown Erie and Albion, with stops at Millcreek Mall, Walmart, Lake City, Route 18, and Albion SCI.",
          "isLikelyDecorative": false
        }
      ]
    }
  ]
}
```

**Note:** This structure is illustrative. AI layout analysis should produce something roughly similar, but it does not need to match coordinates exactly.

### 8.2. Simple unit tests

If you have a test project (e.g., using xUnit), add something like `Tests/StructureRebuildTests.cs`:

```csharp
using AccessForm.Logical;
using AccessForm.Services.Pdf;
using AccessForm.Services.Remediation.Structure;
using Xunit;

public class StructureRebuildTests
{
    [Fact]
    public void StructureTreeBuilder_Builds_Heading_And_Table_Nodes()
    {
        // Arrange
        var logical = new LogicalDocument(new[]
        {
            new LogicalPage(1, new List<LogicalBlock>
            {
                new HeadingBlock(new Rect(0,0,100,20), 1, "Test H1"),
                new ParagraphBlock(new Rect(0,20,100,20), "Hello world"),
                new TableBlock(new Rect(0,40,100,60), new []
                {
                    new TableRow(new []
                    {
                        new TableCell(0,0,true,"Header 1"),
                        new TableCell(0,1,true,"Header 2")
                    }),
                    new TableRow(new []
                    {
                        new TableCell(1,0,false,"Cell 1"),
                        new TableCell(1,1,false,"Cell 2")
                    })
                })
            })
        });

        // Act
        var tree = StructureTreeBuilder.Build(logical);

        // Assert
        Assert.NotNull(tree);
        var root = Assert.Single(tree.Nodes);
        Assert.Equal("Document", root.Role);
        Assert.NotNull(root.Children);

        Assert.Contains(root.Children!, n => n.Role == "H1" && n.TextContent == "Test H1");
        Assert.Contains(root.Children!, n => n.Role == "P" && n.TextContent == "Hello world");
        Assert.Contains(root.Children!, n => n.Role == "Table");
    }

    [Fact]
    public void StubPdfStructureWriter_Returns_Original_Data()
    {
        // Arrange
        var writer = new StubPdfStructureWriter();
        var tree = new StructureTree(new[]
        {
            new StructureNode("Document", null, null, new []
            {
                new StructureNode("P", "Example", null, null)
            })
        });

        var original = new byte[] { 1, 2, 3, 4 };

        // Act
        var result = writer.Rewrite(original, tree);

        // Assert
        Assert.Equal(original, result);
    }
}
```

Adjust namespaces and test project structure to match your existing layout.

---

## 9. AI Integration Notes (for Claude / AI Agents)

This section is guidance for how to implement the actual AI pieces after the plumbing is in place.

### 9.1. Implementing real layout analysis

Replace `StubLogicalLayoutAnalysisService` with a class that:

1. Accepts `pdfBytes`.
2. Renders each page to an image (PNG or JPEG) at a reasonable DPI (e.g., 150–300 DPI).
3. Extracts raw text runs + bounding boxes using an existing PDF library.
4. Sends the image and text geometry to a vision-capable LLM.
5. Parses the LLM’s response into `LogicalBlock` instances.

The core idea: let AI tell you where headings/paragraphs/tables/figures are and how they relate, then reify that into `LogicalDocument`.

### 9.2. Implementing a real PDF structure writer

Replace `StubPdfStructureWriter` with an implementation that:

- Creates or replaces `/StructTreeRoot` in the PDF.
- Adds `/RoleMap` entries mapping:
  - `"H1"` → standard heading role
  - `"P"` → paragraph
  - `"Table"`, `"TR"`, `"TH"`, `"TD"` → table roles
  - `"Figure"` → figure role
- Associates StructureNodes with content via MCIDs and marked-content sequences.
- Sets Alt text (`/Alt`) for figures when `Attributes["alt"]` is present.
- Marks decorative nodes as `/Artifact` where appropriate.

You can implement this directly in C# with a PDF library, or by calling out to Python (e.g., FastAPI + pikepdf) that accepts:

- The original PDF as bytes or a temp file path.
- The `StructureTree` serialized to JSON.
- Returns new PDF bytes.

---

## 10. “Do This” Summary for Claude Code

This is the short version you can literally say to an AI coding agent:

> 1. Add the LogicalDocument model and related blocks under `Models/Logical/` (Rect, LogicalBlock, LogicalPage, LogicalDocument, HeadingBlock, ParagraphBlock, TableBlock, TableRow, TableCell, FigureBlock).
> 2. Add `LogicalLayoutOptions` and `ILogicalLayoutAnalysisService` under `Services/Analysis/`, and create `StubLogicalLayoutAnalysisService` that returns a dummy LogicalDocument.
> 3. Add `StructureNode`, `StructureTree`, and `StructureTreeBuilder` under `Services/Remediation/Structure/`. Implement `StructureTreeBuilder` to convert LogicalDocument into a simple Document → Hn/P/Table/Figure tree as specified above.
> 4. Add `IPdfStructureWriter` and `StubPdfStructureWriter` under `Services/Pdf/`. The stub simply returns the original PDF bytes unchanged.
> 5. Add `StructureRebuildService` under `Services/Remediation/`. It should:
>    - Call `ILogicalLayoutAnalysisService.AnalyzeAsync`,
>    - Build a StructureTree with `StructureTreeBuilder.Build`,
>    - Call `IPdfStructureWriter.Rewrite` and return updated bytes.
> 6. Wire everything into DI in the main service registration (Program.cs or equivalent):
>    - Register StubLogicalLayoutAnalysisService as ILogicalLayoutAnalysisService.
>    - Register StubPdfStructureWriter as IPdfStructureWriter.
>    - Register StructureRebuildService as an IRemediationService and ensure it runs in the “structure fixes” phase of the remediation pipeline.
> 7. Optionally, add the sample `tests/logical/route12.json` and `StructureRebuildTests.cs` to validate the new abstractions compile and behave as expected.
> 8. Once plumbing is working, replace the stub implementations with real AI and PDF-writing logic.

If an AI follows this spec exactly, AccessForm will have a fully wired structural remediation pipeline ready for AI layout analysis and PDF tag rebuilding.

---

## 11. Notes for Human Seth :-)

- You can drop this file into your repo (root or `Markdown Specs/ access-form/`) and point Claude Code at it with a prompt like:
  - “Follow the instructions in `AI-STRUCTURE-REBUILD-SPEC.md` and implement everything it describes.”
- Start with the **stub implementations** to make sure the pipeline compiles and runs end-to-end.
- Then iterate:
  - Replace `StubLogicalLayoutAnalysisService` with a real AI-based layout analyzer.
  - Replace `StubPdfStructureWriter` with a real tag-writing backend.

Once both stubs are replaced, you’ll have a system that can genuinely repair structural accessibility issues at scale, instead of band-aiding PDFs that happen to squeak past checkers.
