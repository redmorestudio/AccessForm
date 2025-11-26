#!/usr/bin/env python3
"""
Quick integration test for pikepdf C# wrapper.
Tests that the orchestrator can be called and produces valid output.
"""

import sys
import json
from pathlib import Path

# Add orchestrator directory to path
orchestrator_dir = Path(__file__).parent / "Services" / "Pdf" / "Pikepdf"
sys.path.insert(0, str(orchestrator_dir))

from orchestrator import PikepdfOrchestrator

def test_orchestrator():
    print("=" * 70)
    print("PIKEPDF INTEGRATION TEST")
    print("=" * 70)

    # Find test PDF
    test_pdf = Path("test_orthodontia.pdf")
    if not test_pdf.exists():
        print(f"❌ Test PDF not found: {test_pdf}")
        return False

    print(f"\n✅ Found test PDF: {test_pdf}")
    print(f"   Size: {test_pdf.stat().st_size:,} bytes")

    # Create simple structure tree
    structure_tree = {
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
                    }
                ]
            }
        ]
    }

    structure_json = json.dumps(structure_tree)
    output_pdf = Path("test_integration_output.pdf")

    # Remove old output if exists
    if output_pdf.exists():
        output_pdf.unlink()

    # Run orchestrator
    print("\n🚀 Running orchestrator...")
    orchestrator = PikepdfOrchestrator(enable_mcid=True)

    try:
        result = orchestrator.rebuild_structure(
            str(test_pdf),
            str(output_pdf),
            structure_json
        )

        print(f"\n✅ Orchestrator completed successfully!")
        print(f"   Elements created: {result['elements_created']}")
        print(f"   MCR kids created: {result['mcr_kids_created']}")
        print(f"   BDC/EMC pairs: {result['bdc_emc_pairs']}")

        # Check output file
        if output_pdf.exists():
            output_size = output_pdf.stat().st_size
            print(f"\n✅ Output PDF created: {output_pdf}")
            print(f"   Size: {output_size:,} bytes")

            if output_size < 1000:
                print("   ⚠️  Warning: Output seems too small!")
                return False

            return True
        else:
            print(f"\n❌ Output PDF not created: {output_pdf}")
            return False

    except Exception as e:
        print(f"\n❌ Orchestrator failed: {e}")
        import traceback
        traceback.print_exc()
        return False

if __name__ == '__main__':
    success = test_orchestrator()

    print("\n" + "=" * 70)
    if success:
        print("✅ INTEGRATION TEST PASSED")
    else:
        print("❌ INTEGRATION TEST FAILED")
    print("=" * 70)

    sys.exit(0 if success else 1)
