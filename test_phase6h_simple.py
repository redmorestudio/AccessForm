#!/usr/bin/env python3
"""Simple Phase 6H Integration Test"""

import base64
import requests
import pikepdf

# Test configuration
MCID_REWRITER_URL = "http://localhost:8000/api/mcid-rewrite"
INPUT_PDF = "./alexandria_FINAL_WORKING.pdf"
OUTPUT_PDF = "./phase6h_test_output.pdf"

print("=" * 80)
print("PHASE 6H INTEGRATION TEST")
print("=" * 80)

# Read input PDF
print(f"\n1. Reading input PDF: {INPUT_PDF}")
with open(INPUT_PDF, "rb") as f:
    pdf_bytes = f.read()
print(f"   Input PDF size: {len(pdf_bytes)} bytes")

# Encode to Base64
pdf_base64 = base64.b64encode(pdf_bytes).decode('utf-8')
print(f"   Base64 encoded: {len(pdf_base64)} characters")

# Build test plan with 2 MCID segments
plan = {
    "documentId": "alexandria-test",
    "version": "6H-1",
    "segments": [
        {
            "pageIndex": 1,  # 1-based
            "mcid": 0,
            "role": "P",
            "x": 72.0,
            "y": 720.0,
            "width": 468.0,
            "height": 12.0,
            "sequenceIndex": 0
        },
        {
            "pageIndex": 1,  # 1-based
            "mcid": 1,
            "role": "P",
            "x": 72.0,
            "y": 700.0,
            "width": 468.0,
            "height": 12.0,
            "sequenceIndex": 1
        }
    ],
    "debug": False
}

print(f"\n2. Building MCID rewrite plan with {len(plan['segments'])} segments")
for i, seg in enumerate(plan['segments']):
    print(f"   Segment {i}: MCID={seg['mcid']}, Page={seg['pageIndex']}, Role={seg['role']}")

# Call microservice
print(f"\n3. Calling MCID rewriter microservice at {MCID_REWRITER_URL}")
request_payload = {
    "pdfBase64": pdf_base64,
    "plan": plan
}

try:
    response = requests.post(MCID_REWRITER_URL, json=request_payload, timeout=30)
    response.raise_for_status()
    result = response.json()

    print(f"   ✅ Microservice call successful")
    print(f"   Response: {result['message']}")

    if result.get('stats'):
        stats = result['stats']
        print(f"   Stats:")
        print(f"     - Pages processed: {stats.get('pagesProcessed', 0)}")
        print(f"     - Segments processed: {stats.get('segmentsProcessed', 0)}")
        print(f"     - BDC count: {stats.get('bdcCount', 0)}")
        print(f"     - EMC count: {stats.get('emcCount', 0)}")

    # Decode output PDF
    output_pdf_base64 = result.get('pdfBase64')
    if not output_pdf_base64:
        print("   ❌ No PDF in response")
        exit(1)

    output_bytes = base64.b64decode(output_pdf_base64)
    print(f"   Output PDF size: {len(output_bytes)} bytes")

    # Save output PDF
    print(f"\n4. Saving output PDF to: {OUTPUT_PDF}")
    with open(OUTPUT_PDF, "wb") as f:
        f.write(output_bytes)
    print(f"   ✅ Saved successfully")

    # Verify BDC/EMC markers in output
    print(f"\n5. Verifying BDC/EMC markers in output PDF")
    pdf = pikepdf.open(OUTPUT_PDF)
    page = pdf.pages[0]
    content_stream = bytes(page.Contents.read_bytes())

    bdc_count = content_stream.count(b'BDC')
    emc_count = content_stream.count(b'EMC')

    print(f"   Content stream size: {len(content_stream)} bytes")
    print(f"   BDC markers found: {bdc_count}")
    print(f"   EMC markers found: {emc_count}")

    pdf.close()

    # Check results
    print(f"\n6. Test Results:")
    if bdc_count > 0 and emc_count > 0:
        print(f"   ✅ SUCCESS: BDC/EMC markers are present in the output PDF!")
        print(f"   ✅ Phase 6H is working correctly!")
        exit(0)
    else:
        print(f"   ❌ FAILURE: BDC/EMC markers NOT found in output PDF")
        print(f"   ❌ Phase 6H implementation has issues")
        exit(1)

except requests.exceptions.RequestException as e:
    print(f"   ❌ HTTP request failed: {e}")
    exit(1)
except Exception as e:
    print(f"   ❌ Error: {e}")
    import traceback
    traceback.print_exc()
    exit(1)
