#!/usr/bin/env python3
"""
Analyze what went wrong with Centennial remediation
"""

import os
import re
from collections import defaultdict

remediation_dir = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/remediation-best"

# Documents that didn't improve or got worse
problem_docs = [
    "CEN_cpag_2019_web-gallery",           # 21 → 198 WORSE
    "CEN_hoa_list",                        # 252 → 252 NO CHANGE
    "CEN_coc-benefits-guide",              # 118 → 118 NO CHANGE
    "CEN_dr_8404_retail_application_9",    # 482 → 302 (some improvement but stuck)
    "CEN_dr_8409_e_wo",                    # 313 → 302 (some improvement but stuck)
    "CEN_tco-co-commercial-checklist-ada", # 18 → 16 (minimal improvement)
]

def parse_violations(filepath):
    """Parse violation report"""
    with open(filepath, 'r') as f:
        content = f.read()

    categories = {}
    for match in re.finditer(r'Category: ([\d.]+) \((\d+) violations?\)', content):
        cat = match.group(1)
        count = int(match.group(2))
        categories[cat] = count

    total = re.search(r'Total Violations: (\d+)', content)
    return int(total.group(1)) if total else 0, categories

print("="*100)
print("CENTENNIAL FAILURE ANALYSIS")
print("="*100)
print()

for doc_base in problem_docs:
    print(f"\n{'='*100}")
    print(f"DOCUMENT: {doc_base.replace('CEN_', '')}")
    print(f"{'='*100}")

    # Find iter0 and iter3 reports
    iter0_file = None
    iter3_file = None

    for f in os.listdir(remediation_dir):
        if f.startswith(doc_base) and f.endswith('_violations.txt'):
            if '_iter0_' in f:
                iter0_file = f
            elif '_iter3_' in f:
                iter3_file = f

    if not iter0_file:
        print("  ⚠️  No iter0 report found")
        continue

    iter0_path = os.path.join(remediation_dir, iter0_file)
    iter0_total, iter0_cats = parse_violations(iter0_path)

    print(f"\nITERATION 0 (Initial): {iter0_total} violations")
    print("-" * 100)
    for cat in sorted(iter0_cats.keys(), key=lambda x: iter0_cats[x], reverse=True):
        print(f"  Category {cat:>10}: {iter0_cats[cat]:>4} violations")

    if iter3_file:
        iter3_path = os.path.join(remediation_dir, iter3_file)
        iter3_total, iter3_cats = parse_violations(iter3_path)

        print(f"\nITERATION 3 (Final): {iter3_total} violations")
        print("-" * 100)
        for cat in sorted(iter3_cats.keys(), key=lambda x: iter3_cats[x], reverse=True):
            change = iter3_cats[cat] - iter0_cats.get(cat, 0)
            change_str = f"({change:+d})" if change != 0 else ""
            print(f"  Category {cat:>10}: {iter3_cats[cat]:>4} violations {change_str:>8}")

        # Show what changed
        print(f"\nCHANGES:")
        print("-" * 100)

        # New violations
        new_cats = set(iter3_cats.keys()) - set(iter0_cats.keys())
        if new_cats:
            print("  ⚠️  NEW violation categories introduced:")
            for cat in sorted(new_cats):
                print(f"     Category {cat}: {iter3_cats[cat]} violations")

        # Fixed violations
        fixed_cats = set(iter0_cats.keys()) - set(iter3_cats.keys())
        if fixed_cats:
            print("  ✓ FIXED violation categories:")
            for cat in sorted(fixed_cats):
                print(f"     Category {cat}: {iter0_cats[cat]} violations fixed")

        # Changed violations
        changed = []
        for cat in set(iter0_cats.keys()) & set(iter3_cats.keys()):
            change = iter3_cats[cat] - iter0_cats[cat]
            if change != 0:
                changed.append((cat, iter0_cats[cat], iter3_cats[cat], change))

        if changed:
            changed.sort(key=lambda x: abs(x[3]), reverse=True)
            print("  📊 CHANGED violations:")
            for cat, before, after, change in changed:
                status = "⚠️ WORSE" if change > 0 else "✓ Better"
                print(f"     Category {cat}: {before} → {after} ({change:+d}) {status}")

        # Overall
        total_change = iter3_total - iter0_total
        if total_change > 0:
            print(f"\n  ❌ OVERALL: Got WORSE by {total_change} violations ({iter0_total} → {iter3_total})")
        elif total_change < 0:
            print(f"\n  ✓ OVERALL: Improved by {abs(total_change)} violations ({iter0_total} → {iter3_total})")
        else:
            print(f"\n  ⚠️  OVERALL: NO CHANGE ({iter0_total} violations remain)")
    else:
        print("\n  ⚠️  No iter3 report found (may not have run iterations)")

print(f"\n{'='*100}")
print("SUMMARY")
print(f"{'='*100}")
print()
print("The remediation system is failing on these documents.")
print("Most likely causes:")
print("  1. Document structure issues our fixes can't handle")
print("  2. Fixes in one category creating violations in another")
print("  3. Complex PDFs that need AI-guided remediation")
print()
print("Recommendation: Re-run these with AI agent assistance")
print()
