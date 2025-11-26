#!/usr/bin/env python3
"""
End-to-end remediation test with pikepdf.
Picks 10 random PDFs from Alexandria directory and processes them through
the full remediation pipeline using the orchestrator.
"""

import sys
import json
import random
from pathlib import Path

# Add orchestrator directory to path
orchestrator_dir = Path(__file__).parent / "Services" / "Pdf" / "Pikepdf"
sys.path.insert(0, str(orchestrator_dir))

from orchestrator import PikepdfOrchestrator

# Configuration
SOURCE_DIR = Path("/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria")
OUTPUT_DIR = Path(__file__).parent / "EndToEndPikepdfTest"
NUM_PDFS = 10

def create_test_structure_tree(pdf_name):
    """
    Create a simple test structure tree.
    In production, this would come from AI layout analysis.
    """
    return {
        "nodes": [
            {
                "id": "/0",
                "role": "Document",
                "lang": "en-US",
                "children": [
                    {
                        "id": "/0/0",
                        "role": "H1",
                        "children": []
                    },
                    {
                        "id": "/0/1",
                        "role": "P",
                        "children": []
                    },
                    {
                        "id": "/0/2",
                        "role": "P",
                        "children": []
                    }
                ]
            }
        ]
    }

def process_pdf(input_path, output_path):
    """Process a single PDF through pikepdf orchestrator."""
    print(f"\n📄 Processing: {input_path.name}")

    results = {
        'success': False,
        'elements_created': 0,
        'mcr_kids_created': 0,
        'bdc_emc_pairs': 0,
        'error': None,
        'input_size': 0,
        'output_size': 0
    }

    try:
        # Get input size
        results['input_size'] = input_path.stat().st_size

        # Create structure tree
        structure_tree = create_test_structure_tree(input_path.name)
        structure_json = json.dumps(structure_tree)

        # Process with orchestrator
        orchestrator = PikepdfOrchestrator(enable_mcid=True)
        result = orchestrator.rebuild_structure(
            str(input_path),
            str(output_path),
            structure_json
        )

        # Capture results
        results['success'] = result['success']
        results['elements_created'] = result.get('elements_created', 0)
        results['mcr_kids_created'] = result.get('mcr_kids_created', 0)
        results['bdc_emc_pairs'] = result.get('bdc_emc_pairs', 0)

        # Get output size
        if output_path.exists():
            results['output_size'] = output_path.stat().st_size

        print(f"  ✅ Success!")
        print(f"     Elements: {results['elements_created']}, MCR kids: {results['mcr_kids_created']}, Markers: {results['bdc_emc_pairs']}")
        print(f"     Size: {results['input_size']:,} → {results['output_size']:,} bytes")

    except Exception as e:
        results['error'] = str(e)
        print(f"  ❌ Failed: {e}")

    return results

def main():
    print("=" * 70)
    print("END-TO-END PIKEPDF REMEDIATION TEST")
    print("=" * 70)

    # Create output directory
    OUTPUT_DIR.mkdir(exist_ok=True)
    print(f"\n📁 Output directory: {OUTPUT_DIR}")

    # Find all PDFs
    all_pdfs = list(SOURCE_DIR.glob("*.pdf"))
    print(f"📚 Found {len(all_pdfs)} PDFs in source directory")

    # Select random PDFs
    if len(all_pdfs) < NUM_PDFS:
        selected = all_pdfs
        print(f"⚠️  Only {len(all_pdfs)} PDFs available, processing all")
    else:
        selected = random.sample(all_pdfs, NUM_PDFS)
        print(f"🎲 Randomly selected {NUM_PDFS} PDFs")

    # Process each PDF
    results = []
    for i, pdf_path in enumerate(selected, 1):
        print(f"\n[{i}/{len(selected)}]", end=" ")
        output_path = OUTPUT_DIR / f"remediated_{i:02d}_{pdf_path.name}"
        result = process_pdf(pdf_path, output_path)
        results.append({
            'input': pdf_path.name,
            'output': output_path.name,
            **result
        })

    # Summary
    print("\n" + "=" * 70)
    print("SUMMARY")
    print("=" * 70)

    successful = sum(1 for r in results if r['success'])
    total_elements = sum(r['elements_created'] for r in results)
    total_mcr_kids = sum(r['mcr_kids_created'] for r in results)
    total_markers = sum(r['bdc_emc_pairs'] for r in results)

    print(f"\n✅ Successfully processed: {successful}/{len(results)}")
    print(f"📊 Total elements created: {total_elements}")
    print(f"🔗 Total MCR kids created: {total_mcr_kids}")
    print(f"🏷️  Total BDC/EMC markers: {total_markers}")

    if successful < len(results):
        print(f"\n❌ Failures: {len(results) - successful}")
        for r in results:
            if not r['success']:
                print(f"  - {r['input']}: {r['error']}")

    print(f"\n📦 Output directory: {OUTPUT_DIR}")

    # Save detailed results
    results_file = OUTPUT_DIR / "results.json"
    with open(results_file, 'w') as f:
        json.dump(results, f, indent=2)
    print(f"📝 Detailed results: {results_file}")

    return successful == len(results)

if __name__ == '__main__':
    success = main()

    print("\n" + "=" * 70)
    if success:
        print("✅ ALL TESTS PASSED")
    else:
        print("❌ SOME TESTS FAILED")
    print("=" * 70)

    sys.exit(0 if success else 1)
