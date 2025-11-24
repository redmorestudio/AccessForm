#!/usr/bin/env python3
"""Test Phase 6J with BAR Admin Approval PDF"""

import base64
import requests
import pikepdf
from pikepdf import parse_content_stream

# Configuration
MCID_REWRITER_URL = "http://localhost:8000/api/mcid-rewrite"
INPUT_PDF = "/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria/BAR-Admin-Approval.pdf"
OUTPUT_PDF = "./bar_admin_phase6j_output.pdf"

print("=" * 80)
print("PHASE 6J TEST - BAR Admin Approval PDF")
print("=" * 80)

# Read input PDF
print(f"\n1. Reading input PDF: {INPUT_PDF}")
with open(INPUT_PDF, "rb") as f:
    pdf_bytes = f.read()
print(f"   Input PDF size: {len(pdf_bytes)} bytes ({len(pdf_bytes)/1024:.1f} KB)")

# Analyze PDF to get page dimensions and existing fields
print("\n2. Analyzing PDF structure...")
pdf = pikepdf.open(INPUT_PDF)
print(f"   Pages: {len(pdf.pages)}")

for page_idx, page in enumerate(pdf.pages):
    mediabox = page.MediaBox
    page_width = float(mediabox[2])
    page_height = float(mediabox[3])
    print(f"   Page {page_idx + 1}: {page_width} x {page_height} points")

    # Check for existing form fields
    if '/Annots' in page:
        annots = page.Annots
        print(f"   Page {page_idx + 1} has {len(annots)} annotations/fields")

pdf.close()

# Encode to Base64
pdf_base64 = base64.b64encode(pdf_bytes).decode('utf-8')
print(f"   Base64 encoded: {len(pdf_base64)} characters")

# Build realistic field coordinates based on the form layout
# Page 1 has many form fields, Page 2 has signature fields
print("\n3. Building MCID rewrite plan with realistic field coordinates")

plan = {
    "documentId": "bar-admin-approval-test",
    "version": "6J-1",
    "segments": [
        # Page 1 - Top section
        {"pageIndex": 1, "mcid": 0, "role": "Form", "x": 400.0, "y": 55.0, "width": 150.0, "height": 15.0, "sequenceIndex": 0},  # BAR CASE# field

        # Page 1 - ADDRESS OF PROJECT
        {"pageIndex": 1, "mcid": 1, "role": "Form", "x": 220.0, "y": 230.0, "width": 330.0, "height": 15.0, "sequenceIndex": 1},

        # Page 1 - Applicant section
        {"pageIndex": 1, "mcid": 2, "role": "Form", "x": 130.0, "y": 280.0, "width": 30.0, "height": 12.0, "sequenceIndex": 2},  # Property Owner checkbox
        {"pageIndex": 1, "mcid": 3, "role": "Form", "x": 250.0, "y": 280.0, "width": 30.0, "height": 12.0, "sequenceIndex": 3},  # Business checkbox

        # Page 1 - Name, Address, City, State, Zip fields
        {"pageIndex": 1, "mcid": 4, "role": "Form", "x": 85.0, "y": 310.0, "width": 465.0, "height": 15.0, "sequenceIndex": 4},  # Name
        {"pageIndex": 1, "mcid": 5, "role": "Form", "x": 105.0, "y": 335.0, "width": 445.0, "height": 15.0, "sequenceIndex": 5},  # Address
        {"pageIndex": 1, "mcid": 6, "role": "Form", "x": 85.0, "y": 360.0, "width": 150.0, "height": 15.0, "sequenceIndex": 6},  # City
        {"pageIndex": 1, "mcid": 7, "role": "Form", "x": 295.0, "y": 360.0, "width": 60.0, "height": 15.0, "sequenceIndex": 7},  # State
        {"pageIndex": 1, "mcid": 8, "role": "Form", "x": 385.0, "y": 360.0, "width": 90.0, "height": 15.0, "sequenceIndex": 8},  # Zip

        # Page 1 - Phone and Email
        {"pageIndex": 1, "mcid": 9, "role": "Form", "x": 90.0, "y": 385.0, "width": 150.0, "height": 15.0, "sequenceIndex": 9},  # Phone
        {"pageIndex": 1, "mcid": 10, "role": "Form", "x": 300.0, "y": 385.0, "width": 250.0, "height": 15.0, "sequenceIndex": 10},  # Email

        # Page 1 - Authorized Agent checkboxes
        {"pageIndex": 1, "mcid": 11, "role": "Form", "x": 280.0, "y": 415.0, "width": 30.0, "height": 12.0, "sequenceIndex": 11},  # Attorney
        {"pageIndex": 1, "mcid": 12, "role": "Form", "x": 400.0, "y": 415.0, "width": 30.0, "height": 12.0, "sequenceIndex": 12},  # Architect

        # Page 1 - Agent Name, Phone, Email
        {"pageIndex": 1, "mcid": 13, "role": "Form", "x": 85.0, "y": 445.0, "width": 350.0, "height": 15.0, "sequenceIndex": 13},  # Agent Name
        {"pageIndex": 1, "mcid": 14, "role": "Form", "x": 450.0, "y": 445.0, "width": 100.0, "height": 15.0, "sequenceIndex": 14},  # Agent Phone
        {"pageIndex": 1, "mcid": 15, "role": "Form", "x": 85.0, "y": 470.0, "width": 200.0, "height": 15.0, "sequenceIndex": 15},  # Agent Email

        # Page 1 - Legal Property Owner section
        {"pageIndex": 1, "mcid": 16, "role": "Form", "x": 85.0, "y": 520.0, "width": 465.0, "height": 15.0, "sequenceIndex": 16},  # Owner Name
        {"pageIndex": 1, "mcid": 17, "role": "Form", "x": 105.0, "y": 545.0, "width": 445.0, "height": 15.0, "sequenceIndex": 17},  # Owner Address
        {"pageIndex": 1, "mcid": 18, "role": "Form", "x": 85.0, "y": 570.0, "width": 150.0, "height": 15.0, "sequenceIndex": 18},  # Owner City
        {"pageIndex": 1, "mcid": 19, "role": "Form", "x": 295.0, "y": 570.0, "width": 60.0, "height": 15.0, "sequenceIndex": 19},  # Owner State
        {"pageIndex": 1, "mcid": 20, "role": "Form", "x": 385.0, "y": 570.0, "width": 90.0, "height": 15.0, "sequenceIndex": 20},  # Owner Zip
        {"pageIndex": 1, "mcid": 21, "role": "Form", "x": 90.0, "y": 595.0, "width": 150.0, "height": 15.0, "sequenceIndex": 21},  # Owner Phone
        {"pageIndex": 1, "mcid": 22, "role": "Form", "x": 300.0, "y": 595.0, "width": 250.0, "height": 15.0, "sequenceIndex": 22},  # Owner Email

        # Page 1 - Description of proposed work (large text area)
        {"pageIndex": 1, "mcid": 23, "role": "Form", "x": 72.0, "y": 650.0, "width": 478.0, "height": 100.0, "sequenceIndex": 23},

        # Page 2 - BAR CASE# field
        {"pageIndex": 2, "mcid": 24, "role": "Form", "x": 400.0, "y": 55.0, "width": 150.0, "height": 15.0, "sequenceIndex": 24},

        # Page 2 - Signature fields
        {"pageIndex": 2, "mcid": 25, "role": "Form", "x": 120.0, "y": 645.0, "width": 300.0, "height": 15.0, "sequenceIndex": 25},  # Signature
        {"pageIndex": 2, "mcid": 26, "role": "Form", "x": 140.0, "y": 670.0, "width": 280.0, "height": 15.0, "sequenceIndex": 26},  # Printed Name
        {"pageIndex": 2, "mcid": 27, "role": "Form", "x": 85.0, "y": 695.0, "width": 100.0, "height": 15.0, "sequenceIndex": 27},  # Date
    ],
    "debug": False
}

print(f"   Built plan with {len(plan['segments'])} segments across {len(set(s['pageIndex'] for s in plan['segments']))} pages")
for i, seg in enumerate(plan['segments'][:5]):
    print(f"   Segment {i}: Page={seg['pageIndex']}, MCID={seg['mcid']}, Bbox=({seg['x']:.1f}, {seg['y']:.1f}, {seg['width']:.1f}, {seg['height']:.1f})")
print(f"   ... and {len(plan['segments']) - 5} more segments")

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

    # Compare marker counts
    print(f"\n6. Comparing BDC/EMC marker counts")

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
        print(f"   ✅ Phase 6J is working with real-world form PDFs!")
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
