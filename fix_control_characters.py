#!/usr/bin/env python3
"""
Specialized script to fix control character and whitespace violations in PDFs.
Specifically targets ETX (0x0003) characters and untagged spaces that are causing accessibility violations.

Usage: python3 fix_control_characters.py <input_pdf> [output_pdf]
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


def fix_control_characters(input_pdf_path, output_pdf_path=None):
    """
    Fix control character violations, specifically:
    1. ETX (0x0003) characters appearing as <0003> in the PDF
    2. Untagged spaces
    3. Untagged BT...ET blocks containing these problematic characters
    """
    if output_pdf_path is None:
        output_pdf_path = tempfile.mktemp(suffix='.pdf')

    try:
        doc = fitz.open(input_pdf_path)

        etx_removed = 0
        spaces_fixed = 0
        bt_blocks_fixed = 0
        total_fixed = 0

        for page_num in range(len(doc)):
            page = doc[page_num]

            try:
                # Get content streams
                xrefs = page.get_contents()
                if not isinstance(xrefs, list):
                    xrefs = [xrefs] if xrefs else []

                for xref in xrefs:
                    stream = doc.xref_stream(xref)
                    if not stream:
                        continue

                    content = stream.decode('latin-1', errors='ignore')
                    original = content
                    modified = False

                    # PASS 1: Remove ETX control characters (0x0003)
                    # These appear as <0003> in hex strings
                    etx_patterns = [
                        r'<0003>\s*Tj',  # Hex ETX with Tj
                        r'<0003>\s*TJ',  # Hex ETX with TJ
                        r'\[\s*<0003>\s*\]\s*TJ',  # ETX in TJ array
                        r'\\003',  # Octal ETX
                        r'\x03',  # Direct ETX character
                    ]

                    for pattern in etx_patterns:
                        matches = list(re.finditer(pattern, content))

                        for match in reversed(matches):
                            pos = match.start()
                            depth = calculate_depth_at_position(content, pos)

                            if depth == 0:  # Untagged
                                # Simply remove ETX characters - they serve no purpose
                                content = content[:match.start()] + content[match.end():]
                                modified = True
                                etx_removed += 1

                    # PASS 2: Handle untagged spaces more aggressively
                    space_patterns = [
                        r'\(\s\)\s*Tj',  # Single space with Tj
                        r'\(\s+\)\s*Tj',  # Multiple spaces with Tj
                        r'\[[\s\(\)]*\]\s*TJ',  # Empty or whitespace-only TJ arrays
                        r'\(\)\s*Tj',  # Empty strings with Tj
                    ]

                    for pattern in space_patterns:
                        matches = list(re.finditer(pattern, content))

                        for match in reversed(matches):
                            pos = match.start()
                            depth = calculate_depth_at_position(content, pos)

                            if depth == 0:  # Untagged
                                # Remove untagged spaces completely
                                content = content[:match.start()] + content[match.end():]
                                modified = True
                                spaces_fixed += 1

                    # PASS 3: Handle untagged BT...ET blocks containing only problematic content
                    bt_pattern = r'BT(.*?)ET'
                    bt_matches = list(re.finditer(bt_pattern, content, re.DOTALL))

                    for bt_match in reversed(bt_matches):
                        bt_start = bt_match.start()

                        # Check if BT is inside marked content
                        bt_depth = calculate_depth_at_position(content, bt_start)
                        if bt_depth > 0:
                            continue  # Already tagged

                        block_content = bt_match.group(1)

                        # Check if block has any marked content
                        if 'BDC' in block_content or 'BMC' in block_content:
                            continue  # Has some tagging

                        # Check if block contains only problematic content
                        # (ETX characters, spaces, or empty)
                        has_etx = '<0003>' in block_content or '\\003' in block_content
                        text_ops = re.findall(r'\([^)]*\)\s*(?:Tj|TJ)', block_content)

                        is_problematic = False
                        if has_etx:
                            is_problematic = True
                        elif text_ops:
                            # Check if all text operations are whitespace-only
                            all_whitespace = True
                            for op in text_ops:
                                text_match = re.search(r'\(([^)]*)\)', op)
                                if text_match:
                                    text = text_match.group(1)
                                    if text.strip() and text != '\\003':
                                        all_whitespace = False
                                        break
                            is_problematic = all_whitespace

                        if is_problematic:
                            # Remove the entire problematic block
                            content = content[:bt_match.start()] + content[bt_match.end():]
                            modified = True
                            bt_blocks_fixed += 1

                    # PASS 4: Clean up any remaining untagged control characters
                    # Target all control characters from 0x00 to 0x1F except common ones
                    control_char_pattern = r'<00[0-8A-CF][0-9A-F]>\s*Tj'
                    matches = list(re.finditer(control_char_pattern, content))

                    for match in reversed(matches):
                        pos = match.start()
                        depth = calculate_depth_at_position(content, pos)

                        if depth == 0:  # Untagged
                            # Extract the hex value to check which character
                            hex_match = re.search(r'<(00[0-8A-CF][0-9A-F])>', match.group(0))
                            if hex_match:
                                hex_val = hex_match.group(1)
                                # Skip common formatting characters (tab, newline, etc.)
                                if hex_val not in ['0009', '000A', '000D']:
                                    content = content[:match.start()] + content[match.end():]
                                    modified = True
                                    etx_removed += 1

                    # Update stream if modified
                    if modified:
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
            "etx_removed": etx_removed,
            "spaces_fixed": spaces_fixed,
            "bt_blocks_fixed": bt_blocks_fixed,
            "total_operations": etx_removed + spaces_fixed + bt_blocks_fixed,
            "message": f"Removed {etx_removed} ETX chars, {spaces_fixed} spaces, {bt_blocks_fixed} BT blocks"
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
            "error": "Usage: fix_control_characters.py <input_pdf> [output_pdf]"
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

    result = fix_control_characters(input_pdf, output_pdf)
    print(json.dumps(result, indent=2))
    sys.exit(0 if result["success"] else 1)


if __name__ == "__main__":
    main()