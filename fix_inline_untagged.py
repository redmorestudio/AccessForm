#!/usr/bin/env python3
"""
Surgical fix for untagged text operations that appear inline within partially-tagged blocks.
This specifically targets individual spaces and characters that appear after punctuation
like colons (:) and parentheses ()), which are causing PAC violations.

Usage: python3 fix_inline_untagged.py <input_pdf> [output_pdf]
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


def fix_inline_untagged(input_pdf_path, output_pdf_path=None):
    """
    Surgical removal of untagged text operations within partially-tagged BT...ET blocks.
    Specifically targets spaces and characters that appear after punctuation.
    """
    if output_pdf_path is None:
        output_pdf_path = tempfile.mktemp(suffix='.pdf')

    try:
        doc = fitz.open(input_pdf_path)

        total_fixed = 0
        inline_spaces_fixed = 0
        inline_chars_fixed = 0

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

                    # Find ALL BT...ET blocks (including those with partial tagging)
                    bt_pattern = r'BT(.*?)ET'
                    bt_matches = list(re.finditer(bt_pattern, content, re.DOTALL))

                    # Process each block from end to beginning to maintain positions
                    for bt_match in reversed(bt_matches):
                        block_content = bt_match.group(1)

                        # Skip if block has NO tagging at all (handled by other scripts)
                        if 'BDC' not in block_content and 'BMC' not in block_content:
                            continue

                        # This block has SOME tagging - need to surgically remove untagged parts
                        modified_block = block_content

                        # Track depth changes through the block
                        lines = modified_block.split('\n')
                        new_lines = []
                        depth = 0

                        for line in lines:
                            # Update depth based on markers in this line
                            bdc_count = len(re.findall(r'\bBDC\b', line))
                            bmc_count = len(re.findall(r'\bBMC\b', line))
                            emc_count = len(re.findall(r'\bEMC\b', line))

                            # Calculate depth BEFORE this line's operations
                            pre_depth = depth

                            # Text operations to check
                            # Look for text operations when depth is 0 (untagged)
                            if pre_depth == 0:
                                # Pattern for single space after colon or paren
                                # These are often untagged and cause issues
                                patterns_to_remove = [
                                    # Space operations
                                    r'\(\s\)\s*Tj',  # Single space
                                    r'\( \)\s*Tj',   # Explicit space
                                    r'\(\\40\)\s*Tj', # Octal space

                                    # ETX control character
                                    r'<0003>\s*Tj',
                                    r'\(\\003\)\s*Tj',

                                    # Empty strings
                                    r'\(\)\s*Tj',

                                    # Single characters that might be orphaned
                                    # Be careful here - only remove if clearly problematic
                                    r'\([)]\)\s*Tj',  # Closing paren by itself
                                    r'\([:]\)\s*Tj',  # Colon by itself

                                    # Hex strings that are just whitespace/control
                                    r'<0020>\s*Tj',  # Space in hex
                                    r'<00A0>\s*Tj',  # Non-breaking space
                                    r'<0009>\s*Tj',  # Tab
                                ]

                                line_modified = False
                                for pattern in patterns_to_remove:
                                    if re.search(pattern, line):
                                        line = re.sub(pattern, '', line)
                                        inline_spaces_fixed += 1
                                        line_modified = True

                                # Also check for TJ arrays with spaces
                                tj_array_pattern = r'\[(.*?)\]\s*TJ'
                                tj_matches = re.finditer(tj_array_pattern, line)
                                for tj_match in tj_matches:
                                    array_content = tj_match.group(1)
                                    # Check if array contains only spaces or empty strings
                                    if re.match(r'^[\s\(\)\\40]*$', array_content):
                                        line = line.replace(tj_match.group(0), '')
                                        inline_spaces_fixed += 1

                            # Update depth after this line's markers
                            depth = depth + bdc_count + bmc_count - emc_count
                            depth = max(0, depth)  # Can't go negative

                            # Only add non-empty lines
                            if line.strip():
                                new_lines.append(line)

                        # Reconstruct the block
                        modified_block = '\n'.join(new_lines)

                        # Replace the block if it was modified
                        if modified_block != block_content:
                            new_block = 'BT' + modified_block + 'ET'
                            content = content[:bt_match.start()] + new_block + content[bt_match.end():]
                            total_fixed += 1

                    # Additional pass: Remove untagged operations BETWEEN BT...ET blocks
                    # These are operations that exist outside any text block
                    # Pattern: Look for Tj/TJ operations outside BT...ET
                    content_outside_blocks = content

                    # Remove all BT...ET blocks temporarily to find orphaned operations
                    temp_content = re.sub(r'BT.*?ET', 'BTBLOCK', content, flags=re.DOTALL)

                    # Find orphaned text operations
                    orphan_patterns = [
                        r'\([^)]*\)\s*Tj',
                        r'<[0-9A-Fa-f]+>\s*Tj',
                        r'\[[^\]]*\]\s*TJ',
                    ]

                    for pattern in orphan_patterns:
                        matches = re.finditer(pattern, temp_content)
                        for match in matches:
                            # This is an orphaned text operation - remove it from original
                            content = content.replace(match.group(0), '')
                            inline_chars_fixed += 1

                    # Update stream if modified
                    if content != original_content:
                        new_stream = content.encode('latin-1', errors='ignore')
                        doc.update_stream(xref, new_stream)

            except Exception as page_error:
                print(f"Warning: Error processing page {page_num + 1}: {page_error}", file=sys.stderr)

        # Save the fixed PDF
        doc.save(output_pdf_path, garbage=4, deflate=True, clean=True)
        doc.close()

        return {
            "success": True,
            "output_path": output_pdf_path,
            "inline_spaces_fixed": inline_spaces_fixed,
            "inline_chars_fixed": inline_chars_fixed,
            "total_blocks_modified": total_fixed,
            "message": f"Fixed {inline_spaces_fixed} inline spaces and {inline_chars_fixed} orphaned chars"
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
            "error": "Usage: fix_inline_untagged.py <input_pdf> [output_pdf]"
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

    result = fix_inline_untagged(input_pdf, output_pdf)
    print(json.dumps(result, indent=2))
    sys.exit(0 if result["success"] else 1)


if __name__ == "__main__":
    main()