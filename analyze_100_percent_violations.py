#!/usr/bin/env python3
import os
import re
from collections import defaultdict

violations_dir = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/Virginia/Alexandria/100 percent and modern"

# Store data for each document
docs = []
all_violations = defaultdict(int)

# Find all violation files
for filename in os.listdir(violations_dir):
    if not filename.endswith('_violations.txt'):
        continue

    filepath = os.path.join(violations_dir, filename)

    with open(filepath, 'r') as f:
        content = f.read()

    # Extract document name
    doc_match = re.search(r'Document: (.+?)\.pdf', content)
    if not doc_match:
        continue
    doc_name = doc_match.group(1)

    # Extract total violations
    total_match = re.search(r'Total Violations: (\d+)', content)
    total_violations = int(total_match.group(1)) if total_match else 0

    # Extract categories and counts
    category_violations = {}
    for cat_match in re.finditer(r'Category: ([\d.]+) \((\d+) violations?\)', content):
        category = cat_match.group(1)
        count = int(cat_match.group(2))
        category_violations[category] = count
        all_violations[category] += count

    docs.append({
        'name': doc_name,
        'total': total_violations,
        'categories': category_violations
    })

# Sort documents by total violations (descending)
docs.sort(key=lambda x: x['total'], reverse=True)

# Print main table
print("Alexandria 100% Compliant PDFs - Initial Violation Analysis")
print("=" * 80)
print(f"{'Document Name':<60} {'Initial Violations':>18}")
print("-" * 80)

for doc in docs:
    print(f"{doc['name']:<60} {doc['total']:>18}")

print("-" * 80)
print(f"{'TOTAL':<60} {sum(d['total'] for d in docs):>18}")
print()
print()

# Print top 10 violation types
print("Top 10 Violation Types Across All Documents")
print("=" * 60)
print(f"{'Category':<20} {'Total Count':>15} {'Description':<25}")
print("-" * 60)

# Category descriptions
descriptions = {
    '5': 'PDF/UA Identification',
    '5-1': 'PDF/UA Metadata',
    '7.1': 'Document Structure',
    '7.3': 'Figure Alt Text',
    '7.18.1': 'Link Annotations',
    '7.21.3.1': 'Font CIDSet',
    '7.21.4.1': 'Font Embedding',
    '7.21.4.2': 'CIDSet Entry',
    '28-005': 'Language Specification',
    '713': 'Tagged Content',
}

sorted_violations = sorted(all_violations.items(), key=lambda x: x[1], reverse=True)[:10]

for i, (category, count) in enumerate(sorted_violations, 1):
    desc = descriptions.get(category, 'Other')
    print(f"{category:<20} {count:>15} {desc:<25}")

print("-" * 60)
print(f"{'TOTAL':<20} {sum(all_violations.values()):>15}")
