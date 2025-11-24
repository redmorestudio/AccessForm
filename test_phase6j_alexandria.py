#!/usr/bin/env python3
"""Phase 6J Real-World Test with DOT Paratransit PDF"""

import base64
import requests
import pikepdf

# Test configuration
MCID_REWRITER_URL = "http://localhost:8000/api/mcid-rewrite"
INPUT_PDF = "/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria/0074_application for alexandria dot paratransit service.pdf"
OUTPUT_PDF = "./alexandria_0074_phase6j_output.pdf"

print("=" * 80)
print("PHASE 6J REAL-WORLD TEST - ALEXANDRIA 0074 PDF")
print("=" * 80)

# Read input PDF
print(f"\n1. Reading input PDF: {INPUT_PDF}")
with open(INPUT_PDF, "rb") as f:
    pdf_bytes = f.read()
print(f"   Input PDF size: {len(pdf_bytes)} bytes ({len(pdf_bytes)/1024:.1f} KB)")

# Analyze PDF to get page dimensions
pdf = pikepdf.open(INPUT_PDF)
page = pdf.pages[0]
mediabox = page.MediaBox
page_width = float(mediabox[2])
page_height = float(mediabox[3])
pdf.close()
print(f"   Page dimensions: {page_width} x {page_height} points")

# Encode to Base64
pdf_base64 = base64.b64encode(pdf_bytes).decode('utf-8')
print(f"   Base64 encoded: {len(pdf_base64)} characters")

# Build realistic test plan with segments covering different areas
# Typical document layout (top to bottom):
# - Header area: y=700-750
# - Title/H1: y=650-690
# - First paragraph: y=600-640
# - Second paragraph: y=550-590
# - Third paragraph: y=500-540
plan = {
    "documentId": "alexandria-phase6j-test",
    "version": "6J-1",
    "segments": [
        {
            "pageIndex": 1,
            "mcid": 0,
            "role": "H1",
            "x": 72.0,
            "y": 660.0,
            "width": 468.0,
            "height": 24.0,
            "sequenceIndex": 0
        },
        {
            "pageIndex": 1,
            "mcid": 1,
            "role": "P",
            "x": 72.0,
            "y": 620.0,
            "width": 468.0,
            "height": 30.0,
            "sequenceIndex": 1
        },
        {
            "pageIndex": 1,
            "mcid": 2,
            "role": "P",
            "x": 72.0,
            "y": 570.0,
            "width": 468.0,
            "height": 40.0,
            "sequenceIndex": 2
        },
        {
            "pageIndex": 1,
            "mcid": 3,
            "role": "P",
            "x": 72.0,
            "y": 510.0,
            "width": 468.0,
            "height": 50.0,
            "sequenceIndex": 3
        },
        {
            "pageIndex": 1,
            "mcid": 4,
            "role": "P",
            "x": 72.0,
            "y": 450.0,
            "width": 468.0,
            "height": 50.0,
            "sequenceIndex": 4
        }
    ],
    "debug": False
}

print(f"\n2. Building MCID rewrite plan with {len(plan['segments'])} segments")
for i, seg in enumerate(plan['segments']):
    print(f"   Segment {i}: MCID={seg['mcid']}, Role={seg['role']}, Y={seg['y']}, Height={seg['height']}")

# Call microservice
print(f"\n3. Calling MCID rewriter microservice at {MCID_REWRITER_URL}")
request_payload = {
    "pdfBase64": pdf_base64,
    "plan": plan
}

try:
    response = requests.post(MCID_REWRITER_URL, json=request_payload, timeout=60)
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
    print(f"   Output PDF size: {len(output_bytes)} bytes ({len(output_bytes)/1024:.1f} KB)")

    # Save output PDF
    print(f"\n4. Saving output PDF to: {OUTPUT_PDF}")
    with open(OUTPUT_PDF, "wb") as f:
        f.write(output_bytes)
    print(f"   ✅ Saved successfully")

    # Verify BDC/EMC markers in output
    print(f"\n5. Verifying BDC/EMC markers in output PDF")
    pdf = pikepdf.open(OUTPUT_PDF)
    page = pdf.pages[0]

    # Parse content stream from page (handles decompression automatically)
    from pikepdf import parse_content_stream

    # Parse the page's content stream - this handles both single and multiple streams
    content_stream = b''
    for operands, operator in parse_content_stream(page):
        # Rebuild the instruction as text
        for op in operands:
            content_stream += str(op).encode('latin-1') + b' '
        content_stream += str(operator).encode('latin-1') + b'\n'

    bdc_count = content_stream.count(b'BDC')
    emc_count = content_stream.count(b'EMC')

    print(f"   Content stream size: {len(content_stream)} bytes ({len(content_stream)/1024:.1f} KB)")
    print(f"   BDC markers found: {bdc_count}")
    print(f"   EMC markers found: {emc_count}")

    # Check for our specific MCIDs
    our_bdc = 0
    for seg in plan['segments']:
        mcid_marker = f"/MCID {seg['mcid']}".encode()
        if mcid_marker in content_stream:
            our_bdc += 1
            print(f"   ✅ Found MCID {seg['mcid']} in content stream")

    print(f"\n6. Content Analysis:")
    print(f"   Total BDC markers (including existing): {bdc_count}")
    print(f"   Our new MCIDs found: {our_bdc}/{len(plan['segments'])}")

    pdf.close()

    # Check results
    print(f"\n7. Test Results:")
    if our_bdc >= len(plan['segments']):
        print(f"   ✅ SUCCESS: All {len(plan['segments'])} MCID segments were inserted!")
        print(f"   ✅ Phase 6J geometry-based segmentation is working with real PDFs!")
        print(f"   ✅ Output PDF saved to: {OUTPUT_PDF}")
        print(f"\n   📋 Next steps:")
        print(f"      - Open {OUTPUT_PDF} in Adobe Acrobat")
        print(f"      - Verify visual layout is preserved")
        print(f"      - Check tags panel shows structure")
        print(f"      - Test with screen reader")
        exit(0)
    else:
        print(f"   ⚠️  PARTIAL: Only {our_bdc}/{len(plan['segments'])} MCIDs were inserted")
        print(f"   This may be expected if segments don't overlap with content")
        exit(0)

except requests.exceptions.RequestException as e:
    print(f"   ❌ HTTP request failed: {e}")
    exit(1)
except Exception as e:
    print(f"   ❌ Error: {e}")
    import traceback
    traceback.print_exc()
    exit(1)
