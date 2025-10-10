#!/usr/bin/env python3
"""
Wrap orphaned whitespace text objects in Artifact tags.
This fixes the "Text object not tagged" PDF/UA error where whitespace
exists outside of any marked content (BMC/EMC pairs).

SAFE APPROACH: Uses PyMuPDF which properly handles content stream manipulation.
"""

import sys
import fitz  # PyMuPDF
import re

def is_only_whitespace(text):
    """Check if text contains only whitespace characters."""
    if not text:
        return True

    # Decode octal sequences
    text = decode_octal(text)

    # Check if only whitespace
    return text.strip() == ''

def decode_octal(text):
    """Decode octal sequences like \\040 (space) in PDF strings."""
    def replace_octal(match):
        octal_value = match.group(1)
        try:
            char_code = int(octal_value, 8)
            return chr(char_code)
        except:
            return match.group(0)

    return re.sub(r'\\(\d{3})', replace_octal, text)

def wrap_orphaned_whitespace(input_pdf_path, output_pdf_path=None):
    """
    Wrap orphaned whitespace text objects in /Artifact BMC...EMC markers.
    """
    if output_pdf_path is None:
        output_pdf_path = input_pdf_path

    doc = fitz.open(input_pdf_path)
    orphans_wrapped = 0

    for page_num, page in enumerate(doc, start=1):
        # Get the page's content stream
        content = page.read_contents().decode('latin-1', errors='ignore')

        # Track if we modified anything
        modified = False
        new_content = []
        lines = content.split('\n')

        # Track whether we're inside marked content
        marked_content_depth = 0

        i = 0
        while i < len(lines):
            line = lines[i]

            # Track marked content depth
            if ' BMC' in line or ' BDC' in line:
                marked_content_depth += 1
            if 'EMC' in line:
                marked_content_depth -= 1

            # Look for text-showing operators: (text) Tj, [(text)] TJ
            text_match = re.search(r'\(([^)]*)\)\s*(?:Tj|TJ|\'|")', line)

            # Only process if OUTSIDE marked content
            if text_match and marked_content_depth == 0:
                text_content = text_match.group(1)

                # Check if this is only whitespace
                if is_only_whitespace(text_content):
                    # Wrap this line in artifact markers
                    new_content.append('/Artifact BMC')
                    new_content.append(line)
                    new_content.append('EMC')

                    orphans_wrapped += 1
                    modified = True
                    print(f"Page {page_num}: Wrapped orphaned whitespace: '{text_content}'")
                else:
                    # Has real content, keep as-is
                    new_content.append(line)
            else:
                # Regular content or already tagged, keep it
                new_content.append(line)

            i += 1

        # If we modified the content, update the page
        if modified:
            new_content_str = '\n'.join(new_content)

            # Update the page content stream using PyMuPDF's _setContents
            new_content_bytes = new_content_str.encode('latin-1', errors='ignore')
            page._setContents(new_content_bytes)

            print(f"Page {page_num}: Updated content stream")

    # Save the modified PDF
    doc.save(output_pdf_path, garbage=4, deflate=True, clean=True)
    doc.close()

    print(f"\\nTotal orphaned whitespace elements wrapped: {orphans_wrapped}")
    return orphans_wrapped

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python wrap_orphaned_whitespace.py <input_pdf> [output_pdf]")
        sys.exit(1)

    input_pdf = sys.argv[1]
    output_pdf = sys.argv[2] if len(sys.argv) > 2 else input_pdf

    try:
        count = wrap_orphaned_whitespace(input_pdf, output_pdf)
        print(f"Successfully processed {input_pdf}")
        print(f"Wrapped {count} orphaned whitespace elements in artifact tags")
        sys.exit(0)
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        sys.exit(1)
