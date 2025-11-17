#!/usr/bin/env python3
import os
import glob

# Find all ERA PDFs in source directory
source_dir = "StateAssets_archived_20251112_063442/Pennsylvania/Erie"
all_pdfs = glob.glob(f"{source_dir}/ERA_*.pdf")

# Find completed documents in remediation-best
remediation_dir = "remediation-best"
completed_violations = glob.glob(f"{remediation_dir}/ERA_*_violations.txt")

# Extract document names from completed violations
completed_docs = set()
for vfile in completed_violations:
    basename = os.path.basename(vfile)
    # Extract document name (remove _best_iterN_timestamp_violations.txt)
    doc_name = basename.split('_best_')[0] + '.pdf'
    completed_docs.add(doc_name)

# Find incomplete documents
incomplete = []
for pdf_path in all_pdfs:
    pdf_name = os.path.basename(pdf_path)
    if pdf_name not in completed_docs:
        incomplete.append(pdf_name)

incomplete.sort()

print(f"Total PDFs: {len(all_pdfs)}")
print(f"Completed: {len(completed_docs)}")
print(f"Incomplete: {len(incomplete)}")
print()
print("Incomplete documents:")
for doc in incomplete:
    print(f"  {doc}")
