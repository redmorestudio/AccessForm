#!/usr/bin/env python3
"""
Fix for text operations that appear immediately after EMC (End Marked Content).
These are the problematic patterns causing PAC violations:
- Text after EMC but before the next BMC/BDC
- Text inside Artifact blocks

Usage: python3 fix_text_after_emc.py <input_pdf> [output_pdf]
"""

import sys
import json
import tempfile
import os
import re

try:
    import fitz  # PyMuPDF
except ImportError:
    print(json.dumps({
        "success": False,
        "error": "PyMuPDF not installed. Run: pip install PyMuPDF"
    }))
    sys.exit(1)


def fix_text_after_emc(input_pdf_path, output_pdf_path=None):
    """
    Fix text operations that appear after EMC markers (untagged).
    """
    if output_pdf_path is None:
        output_pdf_path = tempfile.mktemp(suffix='.pdf')

    try:
        doc = fitz.open(input_pdf_path)

        total_fixed = 0
        after_emc_fixed = 0
        artifact_fixed = 0

        for page_num in range(len(doc)):
            page = doc[page_num]

            try:
                xrefs = page.get_contents()
                if not isinstance(xrefs, list):
                    xrefs = [xrefs] if xrefs else []

                for xref in xrefs:
                    stream = doc.xref_stream(xref)
                    if not stream:
                        continue

                    content = stream.decode('latin-1', errors='ignore')
                    original_content = content

                    # Pattern 0: Fix inline untagged text with transparency mode (3 Tr)
                    # This appears in the middle of tagged content but is itself untagged
                    transparency_pattern = r'(3\s+Tr\s+[\d.]+\s+[\d.]+\s+Td\s*)(\(\s*\)\s*Tj)'
                    content = re.sub(transparency_pattern, r'\1', content)

                    # Pattern 0.5: Fix text that appears right before EMC without proper tagging
                    # Look for: ... 0 Tc 0 Tw/TT0 1 Tf (: )Tj EMC
                    before_emc_pattern = r'(0\s+Tc\s+0\s+Tw[^(]*)\(([:\s\)]+)\)\s*Tj\s*(EMC)'
                    def fix_before_emc(match):
                        nonlocal after_emc_fixed
                        prefix = match.group(1)
                        text = match.group(2)
                        emc = match.group(3)
                        # Remove problematic text before EMC
                        if text.strip() in [':', ')', ': ', ' ']:
                            after_emc_fixed += 1
                            return prefix + emc
                        return match.group(0)
                    content = re.sub(before_emc_pattern, fix_before_emc, content)

                    # Pattern 1: Fix text that appears right after EMC
                    # Look for: EMC ... (text) Tj
                    # This text is untagged because it's outside the marked content
                    pattern_after_emc = r'(EMC\s*(?:/[^\s]+\s*)*)((?:\([^)]*\)\s*Tj\s*)+)'

                    matches = list(re.finditer(pattern_after_emc, content))
                    for match in reversed(matches):
                        emc_part = match.group(1)
                        text_part = match.group(2)

                        # Check if the text is just whitespace or problematic characters
                        text_content = re.findall(r'\(([^)]*)\)', text_part)
                        is_problematic = False

                        for txt in text_content:
                            # Check for single spaces, colons with spaces, empty strings
                            if txt in [' ', ': ', ':', '', '\\40', ') ']:
                                is_problematic = True
                                break
                            # Check if it's all whitespace
                            decoded = txt.replace('\\40', ' ').strip()
                            if not decoded or decoded == ':':
                                is_problematic = True
                                break

                        if is_problematic:
                            # Remove the text operation that's after EMC
                            new_content = emc_part
                            content = content[:match.start()] + new_content + content[match.end():]
                            after_emc_fixed += 1

                    # Pattern 2: Fix text inside Artifact blocks
                    # /Artifact BMC ... (text) Tj ... EMC
                    artifact_pattern = r'(/Artifact\s+BMC)(.*?)(EMC)'

                    def process_artifact(match):
                        nonlocal artifact_fixed
                        start = match.group(1)
                        middle = match.group(2)
                        end = match.group(3)

                        # Remove text operations from artifact blocks
                        # These often contain spaces or formatting characters
                        modified = False

                        # Remove simple text operations
                        patterns_to_remove = [
                            r'\(\s*\)\s*Tj',  # Empty or whitespace
                            r'\( \)\s*Tj',    # Single space
                            r'\(\\40\)\s*Tj', # Octal space
                            r'\([:\)]\)\s*Tj', # Single punctuation
                        ]

                        for pattern in patterns_to_remove:
                            if re.search(pattern, middle):
                                middle = re.sub(pattern, '', middle)
                                modified = True
                                artifact_fixed += 1

                        if modified:
                            return start + middle + end
                        return match.group(0)

                    content = re.sub(artifact_pattern, process_artifact, content, flags=re.DOTALL)

                    # Pattern 3: Clean up orphaned text between marked content blocks
                    # Look for text operations between EMC and the next BDC/BMC
                    emc_to_next_pattern = r'(EMC\s*)([^BE]*?)(?=(?:BDC|BMC|BT|ET|$))'

                    def clean_between_marks(match):
                        nonlocal after_emc_fixed
                        emc = match.group(1)
                        between = match.group(2)

                        # Check if there are text operations in this space
                        if 'Tj' in between or 'TJ' in between:
                            # Remove problematic text operations
                            cleaned = between

                            # Remove specific problematic patterns
                            patterns = [
                                r'/TT\d+\s+\d+\s+Tf\s*\([^)]*\)\s*Tj',  # Font changes with text
                                r'\([:\s\)]*\)\s*Tj',  # Punctuation and spaces
                                r'<0003>\s*Tj',  # ETX character
                                r'\(\s*\)\s*Tj',  # Empty strings
                            ]

                            for pattern in patterns:
                                if re.search(pattern, cleaned):
                                    cleaned = re.sub(pattern, '', cleaned)
                                    after_emc_fixed += 1

                            # Preserve non-text operations (like font settings without text)
                            cleaned = re.sub(r'\s+', ' ', cleaned).strip()
                            if cleaned and not re.search(r'Tj|TJ', cleaned):
                                return emc + ' ' + cleaned + ' '
                            else:
                                return emc + ' '

                        return match.group(0)

                    content = re.sub(emc_to_next_pattern, clean_between_marks, content)

                    # Update stream if modified
                    if content != original_content:
                        new_stream = content.encode('latin-1', errors='ignore')
                        doc.update_stream(xref, new_stream)
                        total_fixed += 1

            except Exception as page_error:
                print(f"Warning: Error processing page {page_num + 1}: {page_error}", file=sys.stderr)

        # Save the fixed PDF
        doc.save(output_pdf_path, garbage=4, deflate=True, clean=True)
        doc.close()

        return {
            "success": True,
            "output_path": output_pdf_path,
            "after_emc_fixed": after_emc_fixed,
            "artifact_fixed": artifact_fixed,
            "pages_modified": total_fixed,
            "message": f"Fixed {after_emc_fixed} after-EMC and {artifact_fixed} artifact violations"
        }

    except Exception as e:
        import traceback
        traceback.print_exc(file=sys.stderr)
        return {
            "success": False,
            "error": str(e)
        }


def main():
    if len(sys.argv) < 2:
        print(json.dumps({
            "success": False,
            "error": "Usage: fix_text_after_emc.py <input_pdf> [output_pdf]"
        }))
        sys.exit(1)

    input_pdf = sys.argv[1]
    output_pdf = sys.argv[2] if len(sys.argv) > 2 else None

    if not os.path.exists(input_pdf):
        print(json.dumps({
            "success": False,
            "error": f"Input PDF not found: {input_pdf}"
        }))
        sys.exit(1)

    result = fix_text_after_emc(input_pdf, output_pdf)
    print(json.dumps(result, indent=2))
    sys.exit(0 if result["success"] else 1)


if __name__ == "__main__":
    main()