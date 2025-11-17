#!/usr/bin/env python3
import os
import shutil
import re

base_dir = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
remediation_dir = os.path.join(base_dir, "remediation-best")
alexandria_base = os.path.join(base_dir, "StateAssets/Virginia/Alexandria")

folders = [
    "100 percent and modern",
    "99 percent (nearly complete)",
    "97-98 percent (minor work)",
    "95-96 percent (moderate work)"
]

copied_count = 0

for folder in folders:
    folder_path = os.path.join(alexandria_base, folder)

    # Get all PDFs in this folder
    pdf_files = [f for f in os.listdir(folder_path) if f.endswith('.pdf')]

    print(f"\nProcessing: {folder}")
    print("-" * 60)

    for pdf_file in pdf_files:
        # Extract base document name
        base_name = pdf_file.replace('_best_iter3_', '_SPLIT_').replace('_best_iter0_', '_SPLIT_')
        base_name = base_name.split('_SPLIT_')[0]

        # Find the final violations file (iter3 or latest)
        violations_files = []
        for f in os.listdir(remediation_dir):
            if f.startswith(base_name) and f.endswith('_violations.txt'):
                violations_files.append(f)

        if violations_files:
            # Sort to get the latest one (iter3 preferred)
            violations_files.sort(reverse=True)

            # Prefer iter3 files
            iter3_files = [f for f in violations_files if '_iter3_' in f]
            if iter3_files:
                vio_file = iter3_files[0]
            else:
                vio_file = violations_files[0]

            # Check if already exists
            target_path = os.path.join(folder_path, vio_file)
            if not os.path.exists(target_path):
                source_path = os.path.join(remediation_dir, vio_file)
                if os.path.exists(source_path):
                    shutil.copy2(source_path, target_path)
                    print(f"  ✓ {vio_file}")
                    copied_count += 1
            else:
                print(f"  - {vio_file} (already exists)")

print(f"\n{'='*60}")
print(f"Total violation reports copied: {copied_count}")
print(f"{'='*60}")
