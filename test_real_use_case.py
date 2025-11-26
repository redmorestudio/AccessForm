#!/usr/bin/env python3
"""
Test pikepdf orchestrator with real StateAssets PDFs.
ACTUAL use case: Structure tree building + MCID marking.
"""

import sys
import json
import random
from pathlib import Path
import subprocess
import shutil

SOURCE_DIR = Path("/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria")
OUTPUT_DIR = Path("/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/Seth10_RealTest")
ORCHESTRATOR = Path("/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/Services/Pdf/Pikepdf/orchestrator.py")

# Simple structure tree for testing
STRUCTURE_TREE = {
    "nodes": [
        {
            "id": "/0",
            "role": "Document",
            "lang": "en-US",
            "alt": None,
            "actual_text": None,
            "children": [
                {
                    "id": "/0/0",
                    "role": "H1",
                    "alt": None,
                    "actual_text": None,
                    "lang": None,
                    "children": []
                },
                {
                    "id": "/0/1",
                    "role": "P",
                    "alt": None,
                    "actual_text": None,
                    "lang": None,
                    "children": []
                }
            ]
        }
    ]
}

def run_orchestrator(input_pdf, output_pdf, structure_json_path):
    """Call pikepdf orchestrator."""
    try:
        result = subprocess.run(
            ["python3", str(ORCHESTRATOR), str(input_pdf), str(output_pdf), str(structure_json_path)],
            capture_output=True,
            text=True,
            timeout=60
        )
        
        if result.returncode == 0:
            # Parse JSON output (last line)
            output_lines = result.stdout.strip().split('\n')
            if output_lines:
                return json.loads(output_lines[-1])
        else:
            return {
                'success': False,
                'error': f"Exit code {result.returncode}: {result.stderr}"
            }
    except subprocess.TimeoutExpired:
        return {'success': False, 'error': 'Timeout (60s)'}
    except Exception as e:
        return {'success': False, 'error': str(e)}

def check_pdf_valid(pdf_path):
    """Quick check if PDF is valid."""
    try:
        import pikepdf
        pdf = pikepdf.open(pdf_path)
        page_count = len(pdf.pages)
        has_structure = '/StructTreeRoot' in pdf.Root
        pdf.close()
        return True, page_count, has_structure
    except Exception as e:
        return False, 0, False

def main():
    print("=" * 70)
    print("REAL USE CASE TEST: Structure Tree Building + MCID Marking")
    print("=" * 70)
    
    # Create output directory
    OUTPUT_DIR.mkdir(exist_ok=True)
    
    # Create structure JSON file
    structure_json_path = OUTPUT_DIR / "structure.json"
    with open(structure_json_path, 'w') as f:
        json.dump(STRUCTURE_TREE, f, indent=2)
    
    # Find all PDFs and select 10 random
    all_pdfs = list(SOURCE_DIR.glob("*.pdf"))
    print(f"\n📚 Found {len(all_pdfs)} PDFs")
    
    selected = random.sample(all_pdfs, min(10, len(all_pdfs)))
    print(f"🎲 Testing with {len(selected)} random PDFs\n")
    
    results = []
    
    for i, pdf_path in enumerate(selected, 1):
        print(f"[{i}/{len(selected)}] {pdf_path.name}")
        
        output_path = OUTPUT_DIR / f"output_{i:02d}_{pdf_path.name}"
        
        # Run orchestrator
        result = run_orchestrator(pdf_path, output_path, structure_json_path)
        
        if result.get('success'):
            # Check output PDF
            valid, pages, has_structure = check_pdf_valid(output_path)
            
            print(f"  ✅ Success: {result['elements_created']} elements, "
                  f"{result['mcr_kids_created']} MCR kids, "
                  f"{result['bdc_emc_pairs']} markers")
            print(f"     Pages: {pages}, Structure: {'✅' if has_structure else '❌'}")
            
            results.append({
                'input': pdf_path.name,
                'success': True,
                'valid_output': valid,
                'pages': pages,
                'has_structure': has_structure,
                **result
            })
        else:
            print(f"  ❌ Failed: {result.get('error', 'Unknown error')}")
            results.append({
                'input': pdf_path.name,
                'success': False,
                'error': result.get('error')
            })
    
    # Summary
    print("\n" + "=" * 70)
    print("SUMMARY")
    print("=" * 70)
    
    successful = sum(1 for r in results if r['success'])
    valid_outputs = sum(1 for r in results if r.get('valid_output', False))
    with_structure = sum(1 for r in results if r.get('has_structure', False))
    
    print(f"\n✅ Successfully processed: {successful}/{len(results)}")
    print(f"✅ Valid output PDFs: {valid_outputs}/{len(results)}")
    print(f"✅ PDFs with structure: {with_structure}/{len(results)}")
    
    if successful > 0:
        total_elements = sum(r.get('elements_created', 0) for r in results if r['success'])
        total_mcr = sum(r.get('mcr_kids_created', 0) for r in results if r['success'])
        total_markers = sum(r.get('bdc_emc_pairs', 0) for r in results if r['success'])
        
        print(f"\n📊 Totals:")
        print(f"   Elements created: {total_elements}")
        print(f"   MCR kids created: {total_mcr}")
        print(f"   BDC/EMC pairs: {total_markers}")
    
    if successful < len(results):
        print(f"\n❌ Failures: {len(results) - successful}")
        for r in results:
            if not r['success']:
                print(f"   - {r['input']}: {r.get('error', 'Unknown')}")
    
    # Save detailed results
    results_path = OUTPUT_DIR / "results.json"
    with open(results_path, 'w') as f:
        json.dump(results, f, indent=2)
    
    print(f"\n📄 Detailed results: {results_path}")
    print(f"📦 Output directory: {OUTPUT_DIR}")
    
    # Create zip
    try:
        shutil.make_archive(
            str(OUTPUT_DIR.parent / "Seth10_RealTest"),
            'zip',
            OUTPUT_DIR
        )
        print(f"📦 Zip created: Seth10_RealTest.zip")
    except Exception as e:
        print(f"⚠️  Zip creation failed: {e}")
    
    # Overall assessment
    success_rate = (successful / len(results)) * 100
    print(f"\n{'='*70}")
    print(f"SUCCESS RATE: {success_rate:.1f}%")
    
    if success_rate >= 90:
        print("✅ PIKEPDF IS READY FOR PRODUCTION (with more testing)")
    elif success_rate >= 70:
        print("⚠️  PIKEPDF WORKS BUT NEEDS MORE HARDENING")
    else:
        print("❌ PIKEPDF NOT READY - TOO MANY FAILURES")
    print(f"{'='*70}")

if __name__ == '__main__':
    main()
