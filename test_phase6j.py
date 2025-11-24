#!/usr/bin/env python3
"""Phase 6J Integration Test with Synthetic PDF"""

import base64
import requests
import pikepdf
from pikepdf import Pdf, Page, Dictionary, Array, Name, Stream
import io

# Test configuration
MCID_REWRITER_URL = "http://localhost:8000/api/mcid-rewrite"
OUTPUT_PDF = "./phase6j_test_output.pdf"

print("=" * 80)
print("PHASE 6J INTEGRATION TEST")
print("=" * 80)

# Create a simple test PDF with 2 text lines
print("\n1. Creating synthetic test PDF with 2 text lines...")

pdf = Pdf.new()
page = pdf.add_blank_page(page_size=(612, 792))  # Letter size

# Create simple content stream with 2 lines of text
content_stream = b"""
BT
/F1 12 Tf
72 720 Td
(Line 1: This is the first line of text) Tj
0 -20 Td
(Line 2: This is the second line of text) Tj
ET
"""

# Add font resource
page.Resources = Dictionary({
    '/Font': Dictionary({
        '/F1': Dictionary({
            '/Type': Name('/Font'),
            '/Subtype': Name('/Type1'),
            '/BaseFont': Name('/Helvetica')
        })
    })
})

# Set content stream
page.Contents = Stream(pdf, content_stream)

# Save to bytes
pdf_buffer = io.BytesIO()
pdf.save(pdf_buffer)
pdf_bytes = pdf_buffer.getvalue()
pdf.close()

print(f"   Created PDF with size: {len(pdf_bytes)} bytes")

# Encode to Base64
pdf_base64 = base64.b64encode(pdf_bytes).decode('utf-8')
print(f"   Base64 encoded: {len(pdf_base64)} characters")

# Build test plan with 2 MCID segments targeting each line
plan = {
    "documentId": "phase6j-synthetic-test",
    "version": "6J-1",
    "segments": [
        {
            "pageIndex": 1,  # 1-based
            "mcid": 0,
            "role": "P",
            "x": 72.0,
            "y": 720.0,  # Line 1 position
            "width": 400.0,
            "height": 15.0,
            "sequenceIndex": 0
        },
        {
            "pageIndex": 1,  # 1-based
            "mcid": 1,
            "role": "P",
            "x": 72.0,
            "y": 700.0,  # Line 2 position (720 - 20)
            "width": 400.0,
            "height": 15.0,
            "sequenceIndex": 1
        }
    ],
    "debug": False
}

print(f"\n2. Building MCID rewrite plan with {len(plan['segments'])} segments")
for i, seg in enumerate(plan['segments']):
    print(f"   Segment {i}: MCID={seg['mcid']}, Page={seg['pageIndex']}, Role={seg['role']}, Y={seg['y']}")

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

    # Show a snippet of the content stream
    print(f"\n6. Content stream snippet (first 500 chars):")
    print(f"   {content_stream[:500].decode('latin-1', errors='ignore')}")

    pdf.close()

    # Check results
    print(f"\n7. Test Results:")
    if bdc_count >= 2 and emc_count >= 2:
        print(f"   ✅ SUCCESS: BDC/EMC markers are present in the output PDF!")
        print(f"   ✅ Phase 6J geometry-based segmentation is working!")
        print(f"   ✅ Expected 2 BDC and 2 EMC (one pair per line)")
        print(f"   ✅ Found {bdc_count} BDC and {emc_count} EMC")

        # Additional validation: markers should wrap individual lines, not all content
        if bdc_count == 2 and emc_count == 2:
            print(f"   ✅ PERFECT: Exactly 2 segments wrapped (no extra markers)")

        exit(0)
    else:
        print(f"   ❌ FAILURE: Expected at least 2 BDC and 2 EMC markers")
        print(f"   ❌ Found {bdc_count} BDC and {emc_count} EMC")
        exit(1)

except requests.exceptions.RequestException as e:
    print(f"   ❌ HTTP request failed: {e}")
    exit(1)
except Exception as e:
    print(f"   ❌ Error: {e}")
    import traceback
    traceback.print_exc()
    exit(1)
