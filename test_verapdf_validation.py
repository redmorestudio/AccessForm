#!/usr/bin/env python3
"""
Run VeraPDF validation on pikepdf outputs to check PDF/UA compliance.
"""

import subprocess
import json
from pathlib import Path
import xml.etree.ElementTree as ET

VERAPDF_PATH = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/verapdf/verapdf"
JAVA_HOME = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/jdk-21.0.8.jdk/Contents/Home"
OUTPUT_DIR = Path("EndToEndPikepdfTest")

def run_verapdf(pdf_path):
    """Run VeraPDF validation and return violation count."""
    print(f"\n📋 Validating: {pdf_path.name}")

    try:
        env = {"JAVA_HOME": JAVA_HOME}
        result = subprocess.run(
            [VERAPDF_PATH, "--format", "text", str(pdf_path)],
            capture_output=True,
            text=True,
            env=env,
            timeout=30
        )

        output = result.stdout

        # Count violations in text output
        violations = 0
        for line in output.split('\n'):
            if 'validationReports' in line or 'failedChecks' in line:
                # Try to extract number
                if 'failedChecks=' in line:
                    try:
                        violations = int(line.split('failedChecks=')[1].split(',')[0])
                    except:
                        pass

        # Also look for PASS/FAIL
        compliant = 'PASS' in output or 'compliant="true"' in output

        if compliant:
            print(f"  ✅ COMPLIANT (0 violations)")
            return 0
        else:
            # Count "Failed checks" lines
            failed_lines = [line for line in output.split('\n') if 'Failed check' in line or 'failed' in line.lower()]
            violation_count = len(failed_lines) if failed_lines else violations

            print(f"  ⚠️  {violation_count} violations")

            # Show first few violations
            for line in failed_lines[:5]:
                print(f"     - {line.strip()}")
            if len(failed_lines) > 5:
                print(f"     ... and {len(failed_lines) - 5} more")

            return violation_count

    except subprocess.TimeoutExpired:
        print(f"  ❌ Validation timed out")
        return -1
    except Exception as e:
        print(f"  ❌ Validation failed: {e}")
        return -1

def main():
    print("=" * 70)
    print("VERAPDF VALIDATION OF PIKEPDF OUTPUTS")
    print("=" * 70)

    # Find all output PDFs
    output_pdfs = list(OUTPUT_DIR.glob("remediated_*.pdf"))
    print(f"\n📁 Found {len(output_pdfs)} PDFs to validate")

    results = []
    for pdf_path in output_pdfs:
        violation_count = run_verapdf(pdf_path)
        results.append({
            'pdf': pdf_path.name,
            'violations': violation_count,
            'compliant': violation_count == 0
        })

    # Summary
    print("\n" + "=" * 70)
    print("SUMMARY")
    print("=" * 70)

    compliant = sum(1 for r in results if r['compliant'])
    total_violations = sum(r['violations'] for r in results if r['violations'] > 0)

    print(f"\n✅ Compliant PDFs: {compliant}/{len(results)}")
    print(f"⚠️  Total violations: {total_violations}")

    if compliant < len(results):
        print(f"\n📊 Violation breakdown:")
        for r in sorted(results, key=lambda x: x['violations'], reverse=True):
            if r['violations'] > 0:
                print(f"  {r['violations']:4d} - {r['pdf']}")

    # Save results
    results_file = OUTPUT_DIR / "verapdf_results.json"
    with open(results_file, 'w') as f:
        json.dump(results, f, indent=2)
    print(f"\n📝 Detailed results: {results_file}")

    print("\n" + "=" * 70)

if __name__ == '__main__':
    main()
