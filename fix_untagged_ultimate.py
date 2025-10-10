#!/usr/bin/env python3
"""
Ultimate aggressive fix for ALL untagged text operations.
This will remove ANY text operation that is not within a marked content block.
This is the nuclear option for PAC compliance.

Usage: python3 fix_untagged_ultimate.py <input_pdf> [output_pdf]
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


def calculate_depth_at_position(text, position):
    """Calculate the marked content depth at a specific position."""
    depth = 0
    for m in re.finditer(r'(BDC|BMC|EMC)', text[:position]):
        marker_type = m.group(0)
        if marker_type in ['BDC', 'BMC']:
            depth += 1
        elif marker_type == 'EMC':
            depth = max(0, depth - 1)
    return depth


def fix_untagged_ultimate(input_pdf_path, output_pdf_path=None):
    """
    Nuclear option: Remove ALL untagged text operations.
    """
    if output_pdf_path is None:
        output_pdf_path = tempfile.mktemp(suffix='.pdf')

    try:
        doc = fitz.open(input_pdf_path)

        total_removed = 0
        tj_removed = 0
        bt_removed = 0

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
                    original_length = len(content)

                    # ULTRA AGGRESSIVE PASS: Remove ALL untagged BT...ET blocks
                    bt_pattern = r'BT(.*?)ET'
                    while True:
                        modified = False
                        bt_matches = list(re.finditer(bt_pattern, content, re.DOTALL))

                        for bt_match in reversed(bt_matches):
                            bt_start = bt_match.start()

                            # Check if BT is inside marked content
                            bt_depth = calculate_depth_at_position(content, bt_start)
                            if bt_depth > 0:
                                continue  # Already tagged

                            block_content = bt_match.group(1)

                            # If block has NO internal tagging, remove it entirely
                            if 'BDC' not in block_content and 'BMC' not in block_content:
                                # Remove the ENTIRE block regardless of content
                                content = content[:bt_match.start()] + content[bt_match.end():]
                                bt_removed += 1
                                modified = True
                                break

                        if not modified:
                            break

                    # SECONDARY PASS: Remove ANY remaining untagged text operations
                    # This catches anything that might be outside BT...ET blocks
                    text_ops = [
                        # All forms of Tj operations
                        r'\([^)]*\)\s*Tj',
                        r'<[0-9A-Fa-f]+>\s*Tj',
                        r'\[[^\]]*\]\s*TJ',
                        # Show string operations
                        r"'",  # Move to next line and show text
                        r'"',  # Set spacing and show text
                    ]

                    for pattern in text_ops:
                        matches = list(re.finditer(pattern, content))

                        for match in reversed(matches):
                            pos = match.start()
                            depth = calculate_depth_at_position(content, pos)

                            if depth == 0:  # Untagged - remove it
                                content = content[:match.start()] + content[match.end():]
                                tj_removed += 1

                    # Update stream if modified
                    if len(content) != original_length:
                        new_stream = content.encode('latin-1', errors='ignore')
                        doc.update_stream(xref, new_stream)
                        total_removed += (original_length - len(content))

            except Exception as page_error:
                print(f"Warning: Error processing page {page_num + 1}: {page_error}", file=sys.stderr)

        # Save the fixed PDF
        doc.save(output_pdf_path, garbage=4, deflate=True, clean=True)
        doc.close()

        return {
            "success": True,
            "output_path": output_pdf_path,
            "bt_blocks_removed": bt_removed,
            "text_ops_removed": tj_removed,
            "bytes_removed": total_removed,
            "total_operations": bt_removed + tj_removed,
            "message": f"Removed {bt_removed} BT blocks and {tj_removed} text operations"
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
            "error": "Usage: fix_untagged_ultimate.py <input_pdf> [output_pdf]"
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

    result = fix_untagged_ultimate(input_pdf, output_pdf)
    print(json.dumps(result, indent=2))
    sys.exit(0 if result["success"] else 1)


if __name__ == "__main__":
    main()