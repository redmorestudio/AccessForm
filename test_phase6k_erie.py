#!/usr/bin/env python3
"""Test Phase 6K with Erie Route 5 PDF - Direct microservice call"""

import base64
import requests
import pikepdf

# Configuration
MCID_REWRITER_URL = "http://localhost:8000/api/mcid-rewrite"
INPUT_PDF = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0051_Route 5 August 2025.pdf"
OUTPUT_PDF = "./erie_phase6k_output.pdf"

print("=" * 80)
print("PHASE 6K TEST - Erie Route 5 PDF")
print("=" * 80)

# Read input PDF
print(f"\n1. Reading input PDF: {INPUT_PDF}")
with open(INPUT_PDF, "rb") as f:
    pdf_bytes = f.read()
print(f"   Input PDF size: {len(pdf_bytes)} bytes ({len(pdf_bytes)/1024:.1f} KB)")

# Analyze PDF structure
print("\n2. Analyzing PDF structure...")
pdf = pikepdf.open(INPUT_PDF)
print(f"   Pages: {len(pdf.pages)}")

for page_idx in range(min(len(pdf.pages), 3)):
    page = pdf.pages[page_idx]
    mediabox = page.MediaBox
    page_width = float(mediabox[2])
    page_height = float(mediabox[3])
    print(f"   Page {page_idx + 1}: {page_width} x {page_height} points")

pdf.close()

# Build test plan - simple grid of segments across first 2 pages
print("\n3. Building Phase 6K test plan (grid-based segments)")

# Create a grid of test segments to cover typical form layout areas
segments = []
mcid = 0

# Page 1 - header, body, footer areas
for page_num in [1, 2]:
    # Top area (header/title)
    segments.append({
        "pageIndex": page_num, "mcid": mcid, "role": "H1",
        "x": 72.0, "y": 72.0, "width": 468.0, "height": 30.0,
        "sequenceIndex": mcid
    })
    mcid += 1

    # Main content area (divide into 3 sections vertically)
    for i in range(3):
        y_pos = 120.0 + (i * 180.0)
        segments.append({
            "pageIndex": page_num, "mcid": mcid, "role": "P",
            "x": 72.0, "y": y_pos, "width": 468.0, "height": 160.0,
            "sequenceIndex": mcid
        })
        mcid += 1

    # Bottom area (footer)
    segments.append({
        "pageIndex": page_num, "mcid": mcid, "role": "P",
        "x": 72.0, "y": 680.0, "width": 468.0, "height": 50.0,
        "sequenceIndex": mcid
    })
    mcid += 1

plan = {
    "documentId": "erie-route5-phase6k-test",
    "version": "6K-1",
    "segments": segments,
    "debug": False
}

print(f"   Built plan with {len(plan['segments'])} segments across {len(set(s['pageIndex'] for s in plan['segments']))} pages")

# Encode PDF to Base64
pdf_base64 = base64.b64encode(pdf_bytes).decode('utf-8')

# Call microservice
print(f"\n4. Calling MCID rewriter microservice at {MCID_REWRITER_URL}")
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
    print(f"\n5. Saving output PDF to: {OUTPUT_PDF}")
    with open(OUTPUT_PDF, "wb") as f:
        f.write(output_bytes)
    print(f"   ✅ Saved successfully")

    # Verify markers
    print(f"\n6. Verifying BDC/EMC marker counts")

    # Count in input
    input_text = pdf_bytes.decode('latin-1', errors='ignore')
    input_bdc = input_text.count('BDC')
    input_emc = input_text.count('EMC')

    # Count in output
    output_text = output_bytes.decode('latin-1', errors='ignore')
    output_bdc = output_text.count('BDC')
    output_emc = output_text.count('EMC')

    print(f"   INPUT:  {input_bdc} BDC, {input_emc} EMC")
    print(f"   OUTPUT: {output_bdc} BDC, {output_emc} EMC")
    print(f"   ADDED:  +{output_bdc - input_bdc} BDC, +{output_emc - input_emc} EMC")

    # Final result
    print(f"\n7. Test Results:")
    if output_bdc > input_bdc and output_emc > input_emc:
        print(f"   ✅✅✅ SUCCESS! Added {output_bdc - input_bdc} new MCID marker pairs!")
        print(f"   ✅ Phase 6K microservice integration is working!")
        print(f"   ✅ Output saved to: {OUTPUT_PDF}")
    else:
        print(f"   ⚠️  No new markers added - segments may not overlap with content")
        print(f"   Output still saved to: {OUTPUT_PDF}")

except requests.exceptions.RequestException as e:
    print(f"   ❌ HTTP request failed: {e}")
    exit(1)
except Exception as e:
    print(f"   ❌ Error: {e}")
    import traceback
    traceback.print_exc()
    exit(1)
