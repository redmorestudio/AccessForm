# Phase 6H: MCID Rewriter Microservice

Python FastAPI microservice that rewrites PDF content streams with BDC/EMC markers using pikepdf.

## Why This Exists

iText7's internal tagging model discards content stream modifications made via `SetData()` during `pdfDoc.Close()`. This external microservice bypasses that limitation by using pikepdf for low-level PDF manipulation outside of iText7's control.

## Installation

```bash
cd McidRewriterMicroservice
pip3 install -r requirements.txt
```

## Running the Service

```bash
python3 main.py
```

The service will start on `http://localhost:8000`.

## API Endpoints

### Health Check
```
GET /health
```

Returns `{"status": "healthy", "service": "mcid-rewriter", "version": "6H-1"}`

### MCID Rewrite
```
POST /api/mcid-rewrite
```

**Request Body**:
```json
{
  "pdfBase64": "base64-encoded PDF bytes",
  "plan": {
    "documentId": "optional-doc-id",
    "version": "6H-1",
    "segments": [
      {
        "pageIndex": 1,
        "mcid": 0,
        "role": "P",
        "x": 72.0,
        "y": 720.0,
        "width": 468.0,
        "height": 12.0,
        "sequenceIndex": 0
      }
    ],
    "debug": false
  }
}
```

**Response**:
```json
{
  "success": true,
  "message": "Successfully rewrote PDF with N MCID markers",
  "pdfBase64": "base64-encoded rewritten PDF",
  "stats": {
    "pagesProcessed": 5,
    "segmentsProcessed": 120,
    "bdcCount": 120,
    "emcCount": 120
  }
}
```

## Architecture

1. .NET application builds structure tree and assigns MCIDs
2. .NET calls this microservice with PDF bytes + McidRewritePlan
3. Microservice uses pikepdf to parse and rewrite content streams
4. Returns rewritten PDF with BDC/EMC markers that will persist

## Configuration

The .NET application expects the microservice to run at `http://localhost:8000` by default. This can be configured in `appsettings.json`:

```json
{
  "Phase6H": {
    "MicroserviceUrl": "http://localhost:8000"
  }
}
```
