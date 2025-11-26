#!/usr/bin/env python3
"""Test pikepdf with proper stdout/stderr separation."""

import sys
import json
import random
from pathlib import Path
import subprocess

SOURCE_DIR = Path("/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria")
OUTPUT_DIR = Path("Seth10_RealTest")
ORCHESTRATOR = Path("Services/Pdf/Pikepdf/orchestrator.py")

STRUCTURE_TREE = {
    "nodes": [{
        "id": "/0", "role": "Document", "lang": "en-US",
        "alt": None, "actual_text": None,
        "children": [
            {"id": "/0/0", "role": "H1", "alt": None, "actual_text": None, "lang": None, "children": []},
            {"id": "/0/1", "role": "P", "alt": None, "actual_text": None, "lang": None, "children": []}
        ]
    }]
}

def run_orchestrator(input_pdf, output_pdf, structure_json_path):
    """Call pikepdf orchestrator with proper output handling."""
    try:
        # Run with stderr redirected separately
        result = subprocess.run(
            ["python3", str(ORCHESTRATOR), str(input_pdf), str(output_pdf), str(structure_json_path)],
            capture_output=True,
            text=True,
            timeout=60
        )
        
        if result.returncode == 0:
            # Parse JSON from stdout (ignore stderr logs)
            return json.loads(result.stdout.strip())
        else:
            return {'success': False, 'error': f"Exit {result.returncode}"}
    except Exception as e:
        return {'success': False, 'error': str(e)}

OUTPUT_DIR.mkdir(exist_ok=True)
structure_json_path = OUTPUT_DIR / "structure.json"
with open(structure_json_path, 'w') as f:
    json.dump(STRUCTURE_TREE, f)

all_pdfs = list(SOURCE_DIR.glob("*.pdf"))
selected = random.sample(all_pdfs, min(10, len(all_pdfs)))

print(f"Testing {len(selected)} PDFs...\n")

results = []
for i, pdf_path in enumerate(selected, 1):
    print(f"[{i}/10] {pdf_path.name[:50]}...", end=" ")
    output_path = OUTPUT_DIR / f"out_{i:02d}_{pdf_path.name}"
    
    result = run_orchestrator(pdf_path, output_path, structure_json_path)
    
    if result.get('success'):
        print(f"✅ {result['elements_created']}e {result['mcr_kids_created']}m {result['bdc_emc_pairs']}b")
        results.append({'file': pdf_path.name, **result})
    else:
        print(f"❌ {result.get('error', 'Unknown')[:30]}")
        results.append({'file': pdf_path.name, **result})

successful = sum(1 for r in results if r['success'])
print(f"\n{'='*60}")
print(f"SUCCESS: {successful}/10 ({successful*10}%)")
print(f"{'='*60}")

if successful >= 8:
    print("✅ PIKEPDF READY FOR PRODUCTION TESTING")
else:
    print("⚠️  NEEDS MORE WORK")
