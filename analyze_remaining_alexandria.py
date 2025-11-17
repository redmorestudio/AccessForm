#!/usr/bin/env python3
import os
import re
import shutil
from collections import defaultdict

remediation_dir = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/remediation-best"
source_dir = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/Virginia/Alexandria"
base_target_dir = source_dir

# Compliance categories
CATEGORIES = {
    '99_percent': {'folder': '99 percent (nearly complete)', 'range': (99.0, 99.5), 'docs': []},
    '97_98_percent': {'folder': '97-98 percent (minor work)', 'range': (97.0, 99.0), 'docs': []},
    '95_96_percent': {'folder': '95-96 percent (moderate work)', 'range': (95.0, 97.0), 'docs': []}
}

# Category descriptions
CATEGORY_NAMES = {
    '5': 'PDF/UA-ID',
    '5-1': 'Metadata',
    '7.1': 'Structure',
    '7.2': 'Headings',
    '7.3': 'Fig-Alt',
    '7.18.1': 'Link-Ann',
    '7.18.3': 'Link-Ctx',
    '7.18.4': 'Link-Alt',
    '7.18.5': 'Link-Str',
    '7.21.3.1': 'F-CIDSet',
    '7.21.4.1': 'F-Embed',
    '7.21.4.2': 'F-CIDEnt',
    '28-005': 'Language',
    '713': 'TaggedCnt',
}

# Read summary file to get compliance scores
summary_file = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/alexandria_summary_20251102_235532.txt"
docs_data = {}

with open(summary_file, 'r') as f:
    for line in f:
        match = re.match(r'\s+(.+?)\.pdf: ([\d.]+)% compliant \((\d+) violations?\)', line)
        if match:
            doc_name = match.group(1)
            compliance = float(match.group(2))
            violations = int(match.group(3))

            # Skip 100% compliant
            if compliance >= 100.0:
                continue

            docs_data[doc_name] = {
                'compliance': compliance,
                'total_violations': violations,
                'categories': defaultdict(int)
            }

# Find and analyze violation files
for filename in os.listdir(remediation_dir):
    if not filename.endswith('_violations.txt'):
        continue

    # Extract doc name from filename
    base_name = filename.replace('_best_iter3_', '_SPLIT_').replace('_best_iter0_', '_SPLIT_')
    base_name = base_name.split('_SPLIT_')[0]

    if base_name not in docs_data:
        continue

    filepath = os.path.join(remediation_dir, filename)

    # Only read iter3 violations (final state before 100%)
    if '_iter3_' not in filename:
        continue

    try:
        with open(filepath, 'r') as f:
            content = f.read()

        # Extract categories
        for cat_match in re.finditer(r'Category: ([\d.]+) \((\d+) violations?\)', content):
            category = cat_match.group(1)
            count = int(cat_match.group(2))
            docs_data[base_name]['categories'][category] = count
    except:
        pass

# Categorize documents
for doc_name, data in docs_data.items():
    compliance = data['compliance']
    for cat_key, cat_info in CATEGORIES.items():
        min_c, max_c = cat_info['range']
        if min_c <= compliance < max_c:
            cat_info['docs'].append((doc_name, data))
            break

# Create folders and move files
print("Creating folders and moving files...\n")
for cat_key, cat_info in CATEGORIES.items():
    folder_path = os.path.join(base_target_dir, cat_info['folder'])
    os.makedirs(folder_path, exist_ok=True)
    print(f"Created: {cat_info['folder']}")

print("\nMoving files...")
moved_count = 0
for cat_key, cat_info in CATEGORIES.items():
    for doc_name, data in cat_info['docs']:
        # Find the final PDF in remediation-best
        pdf_pattern = f"{doc_name}_best_iter3_*.pdf"
        matches = []
        for f in os.listdir(remediation_dir):
            if f.startswith(doc_name + "_best_iter3_") and f.endswith('.pdf') and '_violations' not in f:
                matches.append(f)

        if matches:
            # Get the latest one
            matches.sort()
            source_pdf = os.path.join(remediation_dir, matches[-1])
            target_pdf = os.path.join(base_target_dir, cat_info['folder'], matches[-1])

            if os.path.exists(source_pdf):
                shutil.copy2(source_pdf, target_pdf)
                moved_count += 1

print(f"Moved {moved_count} PDFs\n")

# Generate detailed report
print("\n" + "="*120)
print("ALEXANDRIA REMAINING DOCUMENTS - VIOLATION ANALYSIS BY CATEGORY")
print("="*120)

# Get all unique categories across all docs
all_cats = set()
for doc_name, data in docs_data.items():
    all_cats.update(data['categories'].keys())
def sort_key(cat):
    try:
        # Handle categories like "7.21.4.2" or "5-1" or "713"
        if '.' in cat and '-' in cat:
            return float(cat.split('-')[0].replace('.', ''))
        elif '.' in cat:
            return float(cat.replace('.', ''))
        elif '-' in cat:
            parts = cat.split('-')
            return float(parts[0]) + float(parts[1])/100
        else:
            return float(cat)
    except:
        return 999.0

sorted_cats = sorted(all_cats, key=sort_key)

# Print report for each compliance tier
for cat_key in ['99_percent', '97_98_percent', '95_96_percent']:
    cat_info = CATEGORIES[cat_key]
    if not cat_info['docs']:
        continue

    print(f"\n{'='*120}")
    print(f"{cat_info['folder'].upper()}")
    print(f"{'='*120}")

    # Sort docs by total violations
    sorted_docs = sorted(cat_info['docs'], key=lambda x: x[1]['total_violations'])

    # Print header
    print(f"{'Document':<50} {'Cmpl%':>6} {'Total':>5}", end='')
    for cat in sorted_cats:
        cat_name = CATEGORY_NAMES.get(cat, cat)
        print(f" {cat_name:>9}", end='')
    print()
    print("-"*120)

    # Print each document
    for doc_name, data in sorted_docs:
        print(f"{doc_name[:48]:<50} {data['compliance']:>6.1f} {data['total_violations']:>5}", end='')
        for cat in sorted_cats:
            count = data['categories'].get(cat, 0)
            if count > 0:
                print(f" {count:>9}", end='')
            else:
                print(f" {'-':>9}", end='')
        print()

    # Print subtotal
    print("-"*120)
    total_docs = len(sorted_docs)
    total_violations = sum(d[1]['total_violations'] for d in sorted_docs)
    print(f"{'SUBTOTAL: ' + str(total_docs) + ' documents':<50} {'':>6} {total_violations:>5}", end='')

    # Category totals
    for cat in sorted_cats:
        cat_total = sum(d[1]['categories'].get(cat, 0) for d in sorted_docs)
        if cat_total > 0:
            print(f" {cat_total:>9}", end='')
        else:
            print(f" {'-':>9}", end='')
    print()

# Grand total
print(f"\n{'='*120}")
total_docs = sum(len(cat_info['docs']) for cat_info in CATEGORIES.values())
total_violations = sum(data['total_violations'] for doc_name, data in docs_data.items())
print(f"{'GRAND TOTAL: ' + str(total_docs) + ' documents':<50} {'':>6} {total_violations:>5}", end='')

for cat in sorted_cats:
    cat_total = sum(data['categories'].get(cat, 0) for doc_name, data in docs_data.items())
    if cat_total > 0:
        print(f" {cat_total:>9}", end='')
    else:
        print(f" {'-':>9}", end='')
print()
print("="*120)

# Legend
print("\nCATEGORY LEGEND:")
print("-" * 60)
for cat in sorted_cats:
    full_name = {
        '5': 'PDF/UA Identification',
        '5-1': 'Metadata',
        '7.1': 'Document Structure',
        '7.2': 'Headings',
        '7.3': 'Figure Alt Text',
        '7.18.1': 'Link Annotations',
        '7.18.3': 'Link Context',
        '7.18.4': 'Link Alternative Text',
        '7.18.5': 'Link Structure',
        '7.21.3.1': 'Font CIDSet',
        '7.21.4.1': 'Font Embedding',
        '7.21.4.2': 'Font CIDSet Entry',
        '28-005': 'Language Specification',
        '713': 'Tagged Content',
    }.get(cat, f'Category {cat}')
    print(f"  {CATEGORY_NAMES.get(cat, cat):>10} = {full_name}")
