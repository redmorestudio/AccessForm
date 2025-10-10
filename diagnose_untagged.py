#!/usr/bin/env python3
"""
Diagnose untagged text objects in a PDF to find what PAC is flagging.
"""

import sys
import fitz
import re

def diagnose_untagged_text(pdf_path):
    """Find all text-showing operators and check if they're tagged."""

    doc = fitz.open(pdf_path)
    untagged_count = 0

    for page_num in range(len(doc)):
        page = doc[page_num]

        try:
            # Get content stream
            xrefs = page.get_contents()
            if isinstance(xrefs, list):
                xref_list = xrefs
            else:
                xref_list = [xrefs]

            for xref in xref_list:
                stream = doc.xref_stream(xref)
                if not stream:
                    continue

                content = stream.decode('latin-1', errors='ignore')
                lines = content.split('\n')

                # Track marked content depth
                marked_depth = 0

                for i, line in enumerate(lines):
                    # Find text operators FIRST: Tj, TJ, ', "
                    if re.search(r'\bTj\b|\bTJ\b|[\'"]', line):
                        # Check if untagged by counting markers BEFORE this line
                        before_content = '\n'.join(lines[:i]) + '\n' + line.split('Tj')[0].split('TJ')[0].split("'")[0].split('"')[0]
                        bmc_count = before_content.count('BMC')
                        bdc_count = before_content.count('BDC')
                        emc_count = before_content.count('EMC')
                        depth = (bmc_count + bdc_count) - emc_count

                        # Check if untagged (outside all marked content)
                        if depth == 0:
                            # Extract the text content
                            text_match = re.search(r'\(([^)]*)\)', line)
                            text_content = text_match.group(1) if text_match else "???"

                            print(f"Page {page_num + 1}, Line {i}: UNTAGGED TEXT: '{text_content}' (depth={depth})")
                            print(f"  Context: {line.strip()[:150]}")
                            # Show previous line for context
                            if i > 0:
                                print(f"  Previous: {lines[i-1].strip()[:100]}")
                            untagged_count += 1

        except Exception as e:
            print(f"Error on page {page_num + 1}: {e}")

    doc.close()
    print(f"\nTotal untagged text objects found: {untagged_count}")
    return untagged_count

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python diagnose_untagged.py <pdf_file>")
        sys.exit(1)

    count = diagnose_untagged_text(sys.argv[1])
    sys.exit(0)
