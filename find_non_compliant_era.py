#!/usr/bin/env python3
import re

# Read the report to find non-100% compliant documents
report_path = "StateAssets_archived_20251112_063442/Pennsylvania/Erie/erie_remediation_report_20251113_171421.txt"

with open(report_path, 'r') as f:
    content = f.read()

# Find the TABLE 1 section for rules-based compliance
table1_match = re.search(r'TIER: 99 PERCENT.*?(?=TIER:|TABLE 2:)', content, re.DOTALL)
table2_match = re.search(r'TIER: 97-98 PERCENT.*?(?=TIER: BELOW 95|TABLE 2:)', content, re.DOTALL)
table3_match = re.search(r'TIER: BELOW 95 PERCENT.*?(?=TIER:|========)', content, re.DOTALL)

non_compliant_docs = []

# Parse each section
for section in [table1_match, table2_match, table3_match]:
    if section:
        lines = section.group(0).split('\n')
        for line in lines:
            # Match document names (start of line, not "TIER TOTAL")
            if line.strip() and not line.startswith('TIER') and not line.startswith('Document') and not line.startswith('---'):
                # Extract document name (first column)
                parts = line.split()
                if parts and not parts[0] in ['TIER', 'Document']:
                    doc_name = parts[0]
                    if doc_name and not doc_name.startswith('-'):
                        # Add .pdf if not present
                        if not doc_name.endswith('.pdf'):
                            # Reconstruct the document name from all parts until we hit a percentage
                            full_name = []
                            for part in parts:
                                if '%' in part or part.replace('.', '').replace('-', '').isdigit():
                                    break
                                full_name.append(part)
                            doc_name = ' '.join(full_name)

                        # Convert to ERA_ filename format
                        if doc_name and not doc_name.startswith('TIER'):
                            pdf_name = f"ERA_{doc_name}.pdf"
                            if pdf_name not in non_compliant_docs:
                                non_compliant_docs.append(pdf_name)

# Clean up the list
cleaned_docs = []
for doc in non_compliant_docs:
    # Remove any trailing junk
    doc = doc.split('%')[0].strip()
    if doc.endswith('.pdf'):
        cleaned_docs.append(doc)

print(f"Found {len(cleaned_docs)} non-compliant documents to reprocess:\n")
for doc in sorted(set(cleaned_docs)):
    print(f"  {doc}")
