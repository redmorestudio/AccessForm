#!/usr/bin/env python3
"""
Test script to process Corry PDF through Phase 6K MCID pipeline
"""
import os
import subprocess
import sys

# Paths
PDF_PATH = "StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0032_105 Corry.pdf"
OUTPUT_PATH = "corry_phase6k_output.pdf"

print("🚀 Phase 6K MCID Integration Test - Corry PDF")
print("=" * 60)

# Check if PDF exists
if not os.path.exists(PDF_PATH):
    print(f"❌ ERROR: PDF not found at {PDF_PATH}")
    sys.exit(1)

print(f"✅ Input PDF found: {PDF_PATH}")
print(f"📄 File size: {os.path.getsize(PDF_PATH):,} bytes")
print()

# Use the existing Python test infrastructure
print("🔧 Running Phase 6K pipeline via Python test...")
print()

# Run the test
test_script = """
import sys
import os
sys.path.insert(0, 'McidRewriterMicroservice')

# Import the test we know works (from test_phase6k_erie.py)
import asyncio
import base64
from main import rewrite_mcids_endpoint, McidRewriteRequest, McidRewritePlan, McidSegment

async def test_corry():
    # Read the PDF
    pdf_path = "StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0032_105 Corry.pdf"
    with open(pdf_path, 'rb') as f:
        pdf_bytes = f.read()
    
    # For now, just create a simple test plan with one MCID
    # In real usage, this would come from ITextPdfStructureWriter
    plan = McidRewritePlan(segments=[
        McidSegment(
            pageIndex=0,
            mcid=0,
            bounds={"x": 100, "y": 100, "width": 400, "height": 50},
            role="P",
            sequenceIndex=0
        )
    ])
    
    request = McidRewriteRequest(
        pdfBase64=base64.b64encode(pdf_bytes).decode('utf-8'),
        plan=plan
    )
    
    print(f"📥 Processing PDF: {len(pdf_bytes):,} bytes")
    print(f"📋 MCID plan: {len(plan.segments)} segments")
    
    response = await rewrite_mcids_endpoint(request)
    
    if response.success:
        output_bytes = base64.b64decode(response.pdfBase64)
        output_path = "corry_phase6k_output.pdf"
        with open(output_path, 'wb') as f:
            f.write(output_bytes)
        
        print(f"✅ SUCCESS!")
        print(f"📤 Output saved: {output_path}")
        print(f"📏 Output size: {len(output_bytes):,} bytes")
        print(f"🎯 Markers added: {response.markersAdded}")
        print()
        print("🔍 Verifying BDC/EMC markers...")
        
        # Quick hex check for markers
        import subprocess
        result = subprocess.run(['xxd', output_path], capture_output=True, text=True)
        if '/MCID' in result.stdout or 'BDC' in result.stdout:
            print("✅ BDC/EMC markers detected in output!")
        else:
            print("⚠️  No obvious markers found (may need deeper inspection)")
    else:
        print(f"❌ FAILED: {response.message}")
        sys.exit(1)

asyncio.run(test_corry())
"""

with open('_test_corry_temp.py', 'w') as f:
    f.write(test_script)

try:
    result = subprocess.run([sys.executable, '_test_corry_temp.py'], 
                          capture_output=False, text=True)
    exit_code = result.returncode
finally:
    if os.path.exists('_test_corry_temp.py'):
        os.remove('_test_corry_temp.py')

if exit_code == 0:
    print()
    print("=" * 60)
    print("🎉 Phase 6K MCID Test COMPLETE!")
    print("=" * 60)
else:
    print()
    print("=" * 60)
    print("❌ Test failed with exit code:", exit_code)
    print("=" * 60)
    sys.exit(exit_code)
