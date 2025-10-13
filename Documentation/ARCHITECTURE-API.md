# AccessForm API Architecture - Endpoint Documentation

**Last Updated:** 2025-10-12

## Overview

This document provides detailed documentation for all API endpoints in AccessForm. The application exposes ~30 REST endpoints for document processing, field management, debugging, and utilities.

**Quick Navigation:**
- [Main Processing APIs](#main-processing-apis)
- [Field Management APIs](#field-management-apis)
- [Cascade Correction APIs](#cascade-correction-apis)
- [Accessibility & Compliance APIs](#accessibility--compliance-apis)
- [Debugging & Utility APIs](#debugging--utility-apis)

---

## Main Processing APIs

These are the primary endpoints for converting documents to accessible PDFs.

### POST `/api/process-with-passportpdf-auto`

**Purpose**: Main processing endpoint supporting both Word→PDF and PDF→PDF paths
**Location**: Program.cs:4143-4348
**Used By**: Primary GUI upload flow

**Request**:
```http
POST /api/process-with-passportpdf-auto
Content-Type: multipart/form-data

file: <.docx or .pdf file>
useSyncfusion: true|false
useClaudeVision: true|false
useGoogle: true|false
useClaudeValidation: true|false
useGroqValidation: true|false
useMultiStageValidation: true|false
useSignatureDetection: true|false
useAsposeFontEmbed: true|false
mode: Sequential|Simultaneous|SyncfusionWithValidation
```

**Response**:
```json
{
  "success": true,
  "accessiblePdf": "<base64 PDF bytes>",
  "fieldsDetected": 25,
  "processing": {
    "isWordSource": true,
    "pathUsed": "Word→PDF",
    "servicesUsed": ["Syncfusion", "ClaudeVision", "PassportPDF"],
    "artifactViolationsFixed": 3,
    "whitespaceViolationsFixed": 7
  }
}
```

**Processing Logic**:

**For .docx files (Path 1)**:
```csharp
1. Validate file extension
2. Convert to PDF with AI field detection:
   → ConfigurableFieldDetectionService.ConvertWithConfig()
   → Uses config from form parameters
   → Returns (pdfBytes, fields)
3. Optional: Apply Aspose font embedding
   → If useAsposeFontEmbed=true
   → PdfCompleteRebuildService with FontEmbed option
4. Convert to PDF/A-2u:
   → PassportPdfService.ConvertToPdfAPreservingFieldsAsync()
   → Embeds fonts, replaces ZapfDingbats
   → Removes JavaScript
5. Return accessible PDF
```

**For .pdf files (Path 2)**:
```csharp
1. Validate file extension
2. Extract existing fields:
   → PdfPreservationService.GetExistingFields()
3. Process with accessibility enhancements:
   → PdfPreservationService.ProcessExistingPdfAsync()
   → Fixes artifact violations
   → Fixes whitespace violations
   → Adds metadata and tooltips
   → PRESERVES JavaScript
4. Return accessible PDF
```

**Status Codes**:
- `200 OK` - Success, returns JSON with PDF
- `400 Bad Request` - Invalid file or missing parameters
- `500 Internal Server Error` - Processing failed

**Example Usage**:
```javascript
const formData = new FormData();
formData.append('file', fileBlob);
formData.append('useSyncfusion', 'true');
formData.append('useClaudeVision', 'true');
formData.append('useMultiStageValidation', 'true');
formData.append('mode', 'Sequential');

const response = await fetch('/api/process-with-passportpdf-auto', {
    method: 'POST',
    body: formData
});

const result = await response.json();
```

---

### POST `/api/convert`

**Purpose**: Legacy Word→PDF conversion endpoint (pre-PassportPDF)
**Location**: Program.cs:281-775
**Status**: ⚠️ Legacy - Use `/api/process-with-passportpdf-auto` instead

**Request**:
```http
POST /api/convert
Content-Type: multipart/form-data

file: <.docx file>
```

**Processing**:
1. Font substitution pipeline
2. Character normalization (smart quotes, etc.)
3. Word → PDF conversion (Syncfusion DocIORenderer)
4. Basic accessibility enhancements
5. Returns normal + accessible PDF

**Note**: Does NOT use AI field detection or PassportPDF conversion.

---

### POST `/api/convert-with-ai`

**Purpose**: AI-powered Word→PDF conversion with field detection
**Location**: Program.cs:997-1541
**Status**: ⚠️ Legacy - Use `/api/process-with-passportpdf-auto` instead

**Request**:
```http
POST /api/convert-with-ai
Content-Type: multipart/form-data

file: <.docx file>
aiProvider: anthropic|openai
detectFields: true|false
```

**Processing**:
1. Convert Word → PDF
2. Use AI to detect form fields
3. Apply accessibility enhancements
4. Return PDF with AI-detected fields

---

### POST `/api/convert-with-config`

**Purpose**: Configurable field detection (before PassportPDF integration)
**Location**: Program.cs:3951-4141
**Status**: ⚠️ Legacy - Use `/api/process-with-passportpdf-auto` instead

---

## Field Management APIs

APIs for managing PDF form fields (extraction, editing, creation).

### POST `/api/extract-pdf-fields`

**Purpose**: Extract existing fields from a PDF
**Location**: Program.cs:2527-2779
**Used By**: Field editor UI

**Request**:
```http
POST /api/extract-pdf-fields
Content-Type: multipart/form-data

file: <.pdf file>
```

**Response**:
```json
{
  "fields": [
    {
      "name": "EmployeeName",
      "type": "text",
      "x": 120.5,
      "y": 450.2,
      "width": 200,
      "height": 20,
      "pageNumber": 1,
      "tooltip": "[PAGE:1] Employee full name",
      "value": "",
      "isReadOnly": false
    },
    {
      "name": "AgreementCheckbox",
      "type": "checkbox",
      "x": 50,
      "y": 100,
      "width": 13.8,
      "height": 13.8,
      "pageNumber": 2,
      "tooltip": "[PAGE:2] I agree to terms",
      "checked": false
    }
  ],
  "totalFields": 25,
  "pageCount": 3
}
```

**Field Types Extracted**:
- `text` - PdfLoadedTextBoxField
- `checkbox` - PdfLoadedCheckBoxField
- `radiobutton` - PdfLoadedRadioButtonListField
- `combobox` - PdfLoadedComboBoxField
- `listbox` - PdfLoadedListBoxField
- `signature` - PdfLoadedSignatureField

**Page Detection**:
```csharp
// Priority 1: Extract from tooltip [PAGE:X] pattern
var tooltipPage = FieldTooltipGenerator.ExtractPageFromTooltip(tooltip);

// Priority 2: Use widget detection
foreach (var page in pdfDoc.Pages)
{
    foreach (var annotation in page.Annotations)
    {
        if (annotation.Name == field.Name)
        {
            fieldPage = page;
            break;
        }
    }
}

// Priority 3: Smart Y-coordinate heuristics
// Y < 150: likely page 2
// Y 150-600: page 2 (for multi-page forms)
// Y > 600: page 1
```

---

### POST `/api/update-pdf-fields-v4`

**Purpose**: Update PDF fields using complete rebuild service
**Location**: Program.cs:1937-2100
**Status**: ✅ Current recommended method

**Request**:
```http
POST /api/update-pdf-fields-v4
Content-Type: multipart/form-data

file: <.pdf file>
fieldUpdates: <JSON array of field updates>
```

**Field Update Structure**:
```json
{
  "fieldUpdates": [
    {
      "oldName": "Text1",
      "newName": "EmployeeName",
      "newTooltip": "Enter employee full name",
      "newType": "textbox",
      "bounds": {
        "x": 120,
        "y": 450,
        "width": 200,
        "height": 20
      },
      "pageNumber": 1
    }
  ]
}
```

**Processing**:
```csharp
// Uses PdfCompleteRebuildService
var fieldUpdatesList = ParseFieldUpdates(fieldUpdates);
var result = await completeRebuildService.CompletelyRebuildPdfAsync(
    pdfBytes,
    fieldUpdatesList,
    serviceOptions
);
```

**Response**:
```json
{
  "success": true,
  "pdfBytes": "<base64>",
  "fieldsUpdated": 5,
  "errors": []
}
```

---

### POST `/api/update-pdf-fields-v3`

**Purpose**: Update fields using iText rebuild service
**Location**: Program.cs:1803-1936
**Status**: ⚠️ Alternative to v4

**Uses**: ITextFieldRebuildService for field renaming with proper structure preservation

---

### POST `/api/update-pdf-fields-v2`

**Purpose**: Update fields using Syncfusion editor service
**Location**: Program.cs:1720-1802
**Status**: ⚠️ Legacy method

---

### POST `/api/update-pdf-fields`

**Purpose**: Original field update endpoint
**Location**: Program.cs:2101-2526
**Status**: ⚠️ Deprecated - use v4

---

### POST `/api/convert-with-updated-fields`

**Purpose**: Convert Word document with custom field positions
**Location**: Program.cs:1543-1686
**Used By**: Field editor after manual field adjustment

**Request**:
```http
POST /api/convert-with-updated-fields
Content-Type: multipart/form-data

file: <.docx file>
updatedFields: <JSON array of field definitions>
detectFields: true|false
```

**Updated Fields Structure**:
```json
{
  "updatedFields": [
    {
      "fieldName": "EmployeeName",
      "fieldType": "textbox",
      "x": 120,
      "y": 450,
      "width": 200,
      "height": 20,
      "pageNumber": 1,
      "tooltip": "Employee full name",
      "isValid": true,
      "source": "ClaudeVision"
    }
  ]
}
```

**Processing**:
1. Convert Word → clean PDF (no fields)
2. Add updated fields at specified positions
3. Return PDF with custom field layout

---

### POST `/api/fields/load`

**Purpose**: Load saved field configuration from JSON
**Location**: Program.cs:4349-4556

**Request**:
```http
POST /api/fields/load
Content-Type: multipart/form-data

file: <.pdf or .docx file>
```

**Response**: Returns saved field configuration JSON if exists, otherwise detects fields

---

### POST `/api/fields/save`

**Purpose**: Save field configuration to JSON
**Location**: Program.cs:4557-4627

**Request**:
```http
POST /api/fields/save
Content-Type: application/json

{
  "fileName": "form.docx",
  "fields": [/* field array */]
}
```

**Saves to**: `/FieldConfigs/{sanitizedFileName}.json`

---

## Cascade Correction APIs

Interactive field correction system for multi-round AI editing.

### POST `/api/cascade-correction/session`

**Purpose**: Start a new cascade correction session
**Location**: Program.cs:4685-4718
**Used By**: Interactive field editor UI

**Request**:
```http
POST /api/cascade-correction/session
Content-Type: multipart/form-data

file: <.pdf file>
```

**Response**:
```json
{
  "sessionId": "abc123-def456",
  "pageCount": 3,
  "fieldsDetected": 25,
  "previewImage": "<base64 PNG of page 1>"
}
```

**Processing**:
```csharp
// Creates InteractiveCascadeCorrector session
var sessionId = cascadeCorrector.CreateSession(pdfBytes);

// Generates initial preview
var previewImage = cascadeCorrector.RenderPage(sessionId, pageNumber: 1);
```

---

### GET `/api/cascade-correction/{sessionId}`

**Purpose**: Get page preview for active session
**Location**: Program.cs:4719-4741

**Request**:
```http
GET /api/cascade-correction/{sessionId}?pageNumber=1
```

**Response**:
```json
{
  "pageImage": "<base64 PNG>",
  "width": 612,
  "height": 792,
  "fields": [/* fields on this page */]
}
```

---

### POST `/api/cascade-correction/{sessionId}/command`

**Purpose**: Send AI command to modify fields
**Location**: Program.cs:4742-4798

**Request**:
```http
POST /api/cascade-correction/{sessionId}/command
Content-Type: application/json

{
  "command": "Move the 'Employee Name' field down 20 pixels",
  "pageNumber": 1
}
```

**Response**:
```json
{
  "success": true,
  "updatedFields": [/* modified fields */],
  "previewImage": "<base64 PNG>",
  "aiResponse": "Moved EmployeeName field from Y=450 to Y=470"
}
```

**AI Processing**:
```csharp
// Sends command to Claude/GPT
var response = await cascadeCorrector.ProcessCommand(sessionId, command, pageNumber);

// Updates field positions
// Regenerates preview
// Returns updated state
```

---

### POST `/api/cascade-correction/{sessionId}/apply`

**Purpose**: Apply cascade corrections and finalize PDF
**Location**: Program.cs:4799-4846

**Request**:
```http
POST /api/cascade-correction/{sessionId}/apply
```

**Response**:
```json
{
  "success": true,
  "pdfBytes": "<base64>",
  "fieldsModified": 7
}
```

---

## Accessibility & Compliance APIs

### POST `/api/pdf-ua-compliance`

**Purpose**: Validate PDF/UA compliance
**Location**: Program.cs:2780-2846
**Used By**: Compliance checking

**Request**:
```http
POST /api/pdf-ua-compliance
Content-Type: multipart/form-data

file: <.pdf file>
```

**Response**:
```json
{
  "isCompliant": false,
  "violations": [
    {
      "type": "MissingTooltip",
      "field": "EmployeeName",
      "severity": "Error",
      "message": "Form field missing tooltip (required for PDF/UA)"
    },
    {
      "type": "FontNotEmbedded",
      "page": 1,
      "font": "Times-Roman",
      "severity": "Error",
      "message": "Font 'Times-Roman' not embedded"
    }
  ],
  "warnings": [
    {
      "type": "LowContrast",
      "page": 2,
      "message": "Text may have insufficient contrast"
    }
  ],
  "summary": {
    "totalViolations": 2,
    "totalWarnings": 1,
    "complianceLevel": "Partial"
  }
}
```

**Validation Checks**:
- Document metadata (Title, Language)
- Structure tree presence
- Form field tooltips
- Font embedding
- Artifact violations
- Tagged whitespace
- Alternative text for images

---

### GET `/api/accessibility-report/latest`

**Purpose**: Get latest HTML accessibility report
**Location**: Program.cs:237-256

**Request**:
```http
GET /api/accessibility-report/latest
```

**Response**: Returns HTML report content

**Report Location**: `/AccessibilityReports/{filename}_accessibility_{timestamp}.html`

---

### GET `/api/accessibility-reports`

**Purpose**: List all accessibility reports
**Location**: Program.cs:259-278

**Response**:
```json
{
  "reports": [
    {
      "fileName": "form_accessibility_20250110_153045.html",
      "created": "2025-01-10T15:30:45Z",
      "size": 45678
    }
  ]
}
```

---

### POST `/api/remediate-pdf`

**Purpose**: Apply accessibility remediation to existing PDF
**Location**: Program.cs:776-968

**Request**:
```http
POST /api/remediate-pdf
Content-Type: multipart/form-data

file: <.pdf file>
```

**Processing**:
1. Load PDF
2. Apply AccessibilityRetrofitService
3. Apply PdfAccessibilityEnhancer
4. Set metadata with AccessibilityService
5. Return remediated PDF

---

## Preview & Rendering APIs

### POST `/api/pdf-page-with-field-boxes`

**Purpose**: Render PDF page with field bounding boxes overlaid
**Location**: Program.cs:2847-3094
**Used By**: Field editor for visual debugging

**Request**:
```http
POST /api/pdf-page-with-field-boxes
Content-Type: application/json

{
  "pdfBytes": "<base64>",
  "pageNumber": 1,
  "fields": [/* field array */],
  "highlightFieldName": "EmployeeName"
}
```

**Response**:
```json
{
  "imageBase64": "<base64 PNG>",
  "width": 612,
  "height": 792
}
```

**Rendering**:
```csharp
// Renders PDF page to PNG
var pngImage = RenderPdfPage(pdfBytes, pageNumber, dpi: 150);

// Overlays field boxes
foreach (var field in fields)
{
    DrawFieldBox(canvas, field.Bounds,
        color: field.FieldName == highlightFieldName ? Red : Blue,
        label: field.FieldName
    );
}

return pngImageBytes;
```

---

### POST `/api/pdf-page-preview`

**Purpose**: Simple PDF page preview without overlays
**Location**: Program.cs:3095-3302

**Request**:
```http
POST /api/pdf-page-preview
Content-Type: application/json

{
  "pdfBytes": "<base64>",
  "pageNumber": 1,
  "dpi": 150
}
```

**Response**:
```json
{
  "imageBase64": "<base64 PNG>"
}
```

---

### POST `/api/update-field-preview`

**Purpose**: Preview field changes before applying
**Location**: Program.cs:5979-6130

**Request**:
```http
POST /api/update-field-preview
Content-Type: multipart/form-data

pdfFile: <.pdf file>
fieldUpdates: <JSON>
pageNumber: 1
```

**Response**: PNG image with field updates visualized

---

## Structure & Tag APIs

### POST `/api/extract-tag-structure`

**Purpose**: Extract PDF tag structure tree
**Location**: Program.cs:3303-3950
**Used By**: Tag structure viewer modal

**Request**:
```http
POST /api/extract-tag-structure
Content-Type: multipart/form-data

file: <.pdf file>
```

**Response**:
```json
{
  "success": true,
  "tagTree": {
    "type": "Document",
    "children": [
      {
        "type": "Sect",
        "children": [
          {
            "type": "H1",
            "content": "Form Title"
          },
          {
            "type": "P",
            "content": "This is a paragraph."
          },
          {
            "type": "Form",
            "children": [
              {
                "type": "FormField",
                "fieldName": "EmployeeName",
                "fieldType": "textbox"
              }
            ]
          }
        ]
      }
    ]
  },
  "statistics": {
    "totalTags": 127,
    "headings": 5,
    "paragraphs": 45,
    "tables": 3,
    "formFields": 25,
    "images": 2
  },
  "violations": [
    {
      "type": "EmptyElement",
      "path": "/Document/Sect[0]/P[3]",
      "message": "Paragraph contains only whitespace"
    }
  ]
}
```

**Tag Structure Analysis**:
```csharp
// Recursively parse structure tree
void ParseStructureElement(PdfStructureElement element)
{
    var node = new TagNode
    {
        Type = element.TagType.ToString(),
        Title = element.Title,
        ActualText = element.ActualText
    };

    // Get text content
    if (element.TextContent != null)
        node.Content = element.TextContent;

    // Parse children
    foreach (var child in element.Children)
        node.Children.Add(ParseStructureElement(child));

    return node;
}
```

---

## Debugging & Utility APIs

### GET `/api/debug/{debugId}`

**Purpose**: Retrieve cached AI response for debugging
**Location**: Program.cs:5492-5509
**Used By**: AI debugging panel

**Request**:
```http
GET /api/debug/{debugId}
```

**Response**:
```json
{
  "requestId": "abc123",
  "timestamp": "2025-01-10T15:30:45Z",
  "service": "ClaudeVision",
  "prompt": "Analyze this form...",
  "response": "I found 25 form fields...",
  "images": ["<base64>", "<base64>"],
  "tokensUsed": 3450,
  "cost": 0.0104,
  "duration": "PT34.5S"
}
```

---

### GET `/api/debug-text/{debugId}`

**Purpose**: Get AI response as plain text
**Location**: Program.cs:5510-5544

**Response**: Plain text AI response

---

### GET `/api/health`

**Purpose**: Health check and system status
**Location**: Program.cs:969-996

**Response**:
```json
{
  "status": "healthy",
  "timestamp": "2025-01-10T15:30:45Z",
  "version": "1.0.0",
  "aiServices": {
    "anthropic": {
      "configured": true,
      "keyPresent": true
    },
    "openai": {
      "configured": true,
      "keyPresent": true
    },
    "google": {
      "configured": false,
      "keyPresent": false
    }
  },
  "costTracking": {
    "totalCostToday": 5.47,
    "totalCostThisMonth": 142.33,
    "requestsToday": 45,
    "requestsThisMonth": 892
  },
  "passportPdf": {
    "configured": true,
    "pagesProcessedToday": 127
  }
}
```

---

### GET `/api/logs`

**Purpose**: Retrieve application logs
**Location**: Program.cs:6131-6165

**Request**:
```http
GET /api/logs?lines=100&level=Information
```

**Query Parameters**:
- `lines` - Number of recent log lines (default: 100)
- `level` - Filter by log level (All, Information, Warning, Error)

**Response**:
```json
{
  "logs": [
    "[2025-01-10 15:30:45] [Information] Processing form.docx",
    "[2025-01-10 15:30:46] [Information] Field detection started",
    "[2025-01-10 15:31:20] [Information] Claude Vision detected 25 fields"
  ],
  "totalLines": 1543,
  "returnedLines": 100
}
```

---

### GET `/api/test-page-detection`

**Purpose**: Test page number detection algorithm
**Location**: Program.cs:4628-4668
**Used By**: Development/debugging

**Request**:
```http
GET /api/test-page-detection?yCoordinate=120.5&totalPages=3
```

**Response**:
```json
{
  "yCoordinate": 120.5,
  "totalPages": 3,
  "detectedPage": 2,
  "reasoning": "Y=120.5 is in range 0-150, assigned to page 2"
}
```

---

### GET `/api/cascade-correction/test`

**Purpose**: Test cascade correction service health
**Location**: Program.cs:4669-4684

**Response**:
```json
{
  "status": "operational",
  "activeSessions": 3
}
```

---

### POST `/api/process-pdf`

**Purpose**: General-purpose PDF processing endpoint
**Location**: Program.cs:5667-5846
**Status**: ⚠️ Experimental/testing

---

### POST `/api/test-json`

**Purpose**: Test JSON parsing
**Location**: Program.cs:5847-5858
**Status**: Development only

---

## API Response Formats

### Standard Success Response
```json
{
  "success": true,
  "data": { /* endpoint-specific data */ },
  "message": "Operation completed successfully"
}
```

### Standard Error Response
```json
{
  "success": false,
  "error": "Error message",
  "details": "Detailed error information",
  "stackTrace": "..." // Only in development mode
}
```

### PDF Response (Base64)
```json
{
  "pdfBytes": "<base64-encoded PDF>",
  "fileName": "accessible_form.pdf",
  "fileSize": 245678
}
```

### Field Array Response
```json
{
  "fields": [
    {
      "fieldName": "string",
      "fieldType": "textbox|checkbox|radiobutton|combobox|signature",
      "x": 0.0,
      "y": 0.0,
      "width": 0.0,
      "height": 0.0,
      "pageNumber": 1,
      "tooltip": "string",
      "isValid": true,
      "source": "ClaudeVision|Syncfusion|Google",
      "confidence": 0.95
    }
  ]
}
```

---

## HTTP Headers

### Required Request Headers
```http
Content-Type: multipart/form-data  // For file uploads
Content-Type: application/json     // For JSON payloads
```

### Response Headers
```http
Content-Type: application/json     // For JSON responses
Content-Type: application/pdf      // For PDF downloads
Content-Type: image/png           // For image previews
Access-Control-Allow-Origin: *    // CORS enabled
```

---

## Error Codes

| Status Code | Meaning | Common Causes |
|-------------|---------|---------------|
| 200 | Success | Request completed successfully |
| 400 | Bad Request | Missing file, invalid parameters, malformed JSON |
| 404 | Not Found | Debug ID not found, report not found |
| 500 | Internal Server Error | Processing failed, AI API error, PDF corruption |
| 503 | Service Unavailable | AI service timeout, PassportPDF unavailable |

---

## Rate Limiting

**Current Status**: No rate limiting implemented

**Recommendations**:
- Implement rate limiting for AI-powered endpoints (high cost)
- Suggested limits:
  - `/api/process-with-passportpdf-auto`: 10 requests/minute per IP
  - `/api/cascade-correction/command`: 20 requests/minute per session
  - Other endpoints: 60 requests/minute per IP

---

## Authentication

**Current Status**: No authentication required

**Security Notes**:
- All endpoints are publicly accessible
- Consider implementing API keys for production
- Recommended for multi-tenant deployments

---

## API Usage Examples

### JavaScript/Fetch
```javascript
// Upload and process document
const formData = new FormData();
formData.append('file', fileInput.files[0]);
formData.append('useSyncfusion', 'true');
formData.append('useClaudeVision', 'true');

const response = await fetch('/api/process-with-passportpdf-auto', {
    method: 'POST',
    body: formData
});

const result = await response.json();
console.log(`Processed ${result.fieldsDetected} fields`);
```

### C# HttpClient
```csharp
using var client = new HttpClient();
using var content = new MultipartFormDataContent();

var fileContent = new ByteArrayContent(pdfBytes);
fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
content.Add(fileContent, "file", "form.pdf");

var response = await client.PostAsync("/api/extract-pdf-fields", content);
var result = await response.Content.ReadAsStringAsync();
var fields = JsonSerializer.Deserialize<FieldExtractionResult>(result);
```

### cURL
```bash
# Process document with AI
curl -X POST http://localhost:5001/api/process-with-passportpdf-auto \
  -F "file=@form.docx" \
  -F "useSyncfusion=true" \
  -F "useClaudeVision=true" \
  -F "mode=Sequential"

# Extract fields from PDF
curl -X POST http://localhost:5001/api/extract-pdf-fields \
  -F "file=@form.pdf"

# Check health
curl http://localhost:5001/api/health
```

---

**End of ARCHITECTURE-API.md**

**See also:**
- **ARCHITECTURE.md** - Main processing paths
- **ARCHITECTURE-SERVICES.md** - Service class details
- **ARCHITECTURE-GUI.md** - GUI components
