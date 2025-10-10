#!/usr/bin/env python3
"""
Aggressive fix for ALL untagged whitespace that PAC identifies as accessibility violations.
This specifically targets the 20 individual spaces that appear in places like:
- End of sentences
- Around page numbers
- Near logos
- Between form fields

Usage: python3 fix_all_untagged_whitespace.py <input_pdf> [output_pdf]
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


def is_whitespace_only(text):
    """Check if text contains only whitespace or control characters."""
    # Decode escape sequences
    decoded = text
    decoded = decoded.replace('\\n', '\n')
    decoded = decoded.replace('\\r', '\r')
    decoded = decoded.replace('\\t', '\t')
    decoded = decoded.replace('\\40', ' ')
    decoded = decoded.replace('\\(', '(')
    decoded = decoded.replace('\\)', ')')

    # Check for octal sequences (like \003 for ETX)
    decoded = re.sub(r'\\(\d{1,3})', lambda m: chr(int(m.group(1), 8)) if int(m.group(1), 8) < 128 else m.group(0), decoded)

    # Check if it's only whitespace or control characters
    for char in decoded:
        if char > ' ' and ord(char) < 127:  # Printable ASCII
            return False
    return True


def fix_all_untagged_whitespace(input_pdf_path, output_pdf_path=None):
    """
    Aggressively remove ALL untagged whitespace that causes PAC violations.
    """
    if output_pdf_path is None:
        output_pdf_path = tempfile.mktemp(suffix='.pdf')

    try:
        doc = fitz.open(input_pdf_path)

        total_removed = 0
        spaces_removed = 0
        control_removed = 0
        blocks_removed = 0

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

                    # PASS 1: Find and remove ALL untagged BT...ET blocks that contain only whitespace
                    bt_pattern = r'BT(.*?)ET'
                    while True:
                        modified_this_pass = False
                        bt_matches = list(re.finditer(bt_pattern, content, re.DOTALL))

                        for bt_match in reversed(bt_matches):
                            bt_start = bt_match.start()

                            # Check if BT is inside marked content
                            bt_depth = calculate_depth_at_position(content, bt_start)
                            if bt_depth > 0:
                                continue  # Already tagged

                            block_content = bt_match.group(1)

                            # Skip if has any tagging
                            if 'BDC' in block_content or 'BMC' in block_content:
                                continue

                            # Extract all text operations
                            text_ops = []

                            # Pattern for text operations
                            ops_patterns = [
                                r'\(([^)]*)\)\s*Tj',  # Simple text
                                r'<([0-9A-Fa-f]+)>\s*Tj',  # Hex string
                                r'\[(.*?)\]\s*TJ',  # TJ array
                            ]

                            has_only_whitespace = True
                            for pattern in ops_patterns:
                                for match in re.finditer(pattern, block_content):
                                    if pattern == r'<([0-9A-Fa-f]+)>\s*Tj':
                                        # Hex string - check if it's whitespace or control
                                        hex_str = match.group(1)
                                        # Common whitespace/control in hex
                                        if hex_str not in ['0020', '0009', '000A', '000D', '0003', '00A0']:
                                            # Try to decode and check
                                            try:
                                                if len(hex_str) % 2 == 0:
                                                    chars = [hex_str[i:i+2] for i in range(0, len(hex_str), 2)]
                                                    for hex_char in chars:
                                                        char_code = int(hex_char, 16)
                                                        if char_code > 32 and char_code < 127:
                                                            has_only_whitespace = False
                                                            break
                                            except:
                                                has_only_whitespace = False
                                    elif pattern == r'\(([^)]*)\)\s*Tj':
                                        # Regular text string
                                        text = match.group(1)
                                        if not is_whitespace_only(text):
                                            has_only_whitespace = False
                                            break

                            if has_only_whitespace:
                                # Remove the entire BT...ET block
                                content = content[:bt_match.start()] + content[bt_match.end():]
                                blocks_removed += 1
                                modified_this_pass = True
                                break

                        if not modified_this_pass:
                            break

                    # PASS 2: Remove individual untagged whitespace operations
                    # Be very aggressive here - these are the specific spaces PAC is complaining about
                    patterns = [
                        # Spaces in various forms
                        (r'\(\s+\)\s*Tj', 'space'),
                        (r'\(\\40\)\s*Tj', 'space'),
                        (r'\( \)\s*Tj', 'space'),

                        # Control characters (especially ETX)
                        (r'<0003>\s*Tj', 'control'),
                        (r'<00[0-1][0-9A-F]>\s*Tj', 'control'),  # Other control chars
                        (r'\(\\003\)\s*Tj', 'control'),

                        # Empty strings
                        (r'\(\)\s*Tj', 'empty'),

                        # Whitespace in arrays
                        (r'\[\s*\(\s*\)\s*\]\s*TJ', 'space'),
                        (r'\[\s*\(\\40\)\s*\]\s*TJ', 'space'),

                        # Non-breaking spaces
                        (r'<00A0>\s*Tj', 'space'),
                        (r'<A0>\s*Tj', 'space'),
                    ]

                    for pattern, ptype in patterns:
                        matches = list(re.finditer(pattern, content))

                        for match in reversed(matches):
                            pos = match.start()
                            depth = calculate_depth_at_position(content, pos)

                            if depth == 0:  # Untagged
                                content = content[:match.start()] + content[match.end():]
                                if ptype == 'space' or ptype == 'empty':
                                    spaces_removed += 1
                                elif ptype == 'control':
                                    control_removed += 1

                    # PASS 3: Look for specific problem areas near common locations
                    # Remove standalone space operations between text blocks
                    standalone_space = r'(?<=ET\s{0,10})\s*BT[^E]*?\(\s*\)[^E]*?Tj[^E]*?ET(?=\s{0,10}BT)'
                    matches = list(re.finditer(standalone_space, content))
                    for match in reversed(matches):
                        pos = match.start()
                        depth = calculate_depth_at_position(content, pos)
                        if depth == 0:
                            content = content[:match.start()] + content[match.end():]
                            spaces_removed += 1

                    # Update stream if content was modified
                    if len(content) != original_length:
                        new_stream = content.encode('latin-1', errors='ignore')
                        doc.update_stream(xref, new_stream)
                        total_removed += (original_length - len(content))

            except Exception as page_error:
                print(f"Warning: Error processing page {page_num + 1}: {page_error}", file=sys.stderr)

        # Save the fixed PDF
        doc.save(output_pdf_path, garbage=4, deflate=True, clean=True)
        doc.close()

        total_operations = spaces_removed + control_removed + blocks_removed

        return {
            "success": True,
            "output_path": output_pdf_path,
            "spaces_removed": spaces_removed,
            "control_removed": control_removed,
            "blocks_removed": blocks_removed,
            "bytes_removed": total_removed,
            "total_fixes": total_operations,
            "message": f"Removed {spaces_removed} spaces, {control_removed} control chars, {blocks_removed} blocks"
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
            "error": "Usage: fix_all_untagged_whitespace.py <input_pdf> [output_pdf]"
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

    result = fix_all_untagged_whitespace(input_pdf, output_pdf)
    print(json.dumps(result, indent=2))
    sys.exit(0 if result["success"] else 1)


if __name__ == "__main__":
    main()