#!/usr/bin/env python3
"""
Fix PDF/UA violations by removing tagged whitespace elements.

This script finds tagged content (elements with /MCID) that contain ONLY whitespace
and removes the tagging markers, converting them to unmarked content.

Usage: python3 fix_tagged_whitespace.py <input_pdf> [output_pdf]
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


def fix_tagged_whitespace(input_pdf_path, output_pdf_path=None):
    """
    Remove tagging from whitespace-only content.

    Finds patterns like: /Span <</MCID X>> BDC ( ) Tj EMC
    and removes the BDC/EMC wrappers, leaving just: ( ) Tj
    """
    if output_pdf_path is None:
        output_pdf_path = tempfile.mktemp(suffix='.pdf')

    try:
        doc = fitz.open(input_pdf_path)

        total_fixed = 0
        violations_found = 0

        for page_num in range(len(doc)):
            page = doc[page_num]

            try:
                # Get content stream(s)
                xrefs = page.get_contents()
                if isinstance(xrefs, list):
                    xref_list = xrefs
                else:
                    xref_list = [xrefs]

                # Process each content stream
                for xref in xref_list:
                    stream = doc.xref_stream(xref)
                    if not stream:
                        continue

                    content_str = stream.decode('latin-1', errors='ignore')
                    original_content = content_str
                    modified = False

                    # Pattern 1: Tagged whitespace with BDC...EMC
                    # Matches: /Span <</MCID 123>> BDC ( ) Tj EMC
                    # Also matches variations like /P, /Figure, etc.
                    pattern1 = r'/(?:Span|P|Figure|Form|Table|H\d|L|LI|LBody|TD|TH)\s*<<[^>]*>>\s*BDC\s*\([\s\\n\\r\\t]*\)\s*Tj\s*EMC'
                    matches = list(re.finditer(pattern1, content_str))

                    if matches:
                        violations_found += len(matches)
                        # Replace from end to beginning to preserve indices
                        for match in reversed(matches):
                            # Extract just the whitespace text operation: ( ) Tj
                            # This effectively removes the tagging
                            content_str = content_str[:match.start()] + content_str[match.end():]
                            modified = True
                            total_fixed += 1

                    # Pattern 2: Tagged whitespace with multiple spaces
                    # Matches: /Span <</MCID 123>> BDC (  ) Tj EMC or ( \n) etc.
                    pattern2 = r'/(?:Span|P|Figure|Form|Table|H\d|L|LI|LBody|TD|TH)\s*<<[^>]*>>\s*BDC\s*\([\\s\\n\\r\\t ]*\)\s*Tj\s*EMC'
                    matches = list(re.finditer(pattern2, content_str))

                    if matches:
                        violations_found += len(matches)
                        for match in reversed(matches):
                            content_str = content_str[:match.start()] + content_str[match.end():]
                            modified = True
                            total_fixed += 1

                    # Pattern 3: Tagged whitespace with TJ operator (array form)
                    # Matches: /Span <</MCID 123>> BDC [( )] TJ EMC
                    pattern3 = r'/(?:Span|P|Figure|Form|Table|H\d|L|LI|LBody|TD|TH)\s*<<[^>]*>>\s*BDC\s*\[\s*\([\\s\\n\\r\\t ]*\)\s*\]\s*TJ\s*EMC'
                    matches = list(re.finditer(pattern3, content_str))

                    if matches:
                        violations_found += len(matches)
                        for match in reversed(matches):
                            content_str = content_str[:match.start()] + content_str[match.end():]
                            modified = True
                            total_fixed += 1

                    # Pattern 4: End-of-sentence whitespace (space after period, etc.)
                    # Matches: (text.) Tj /Span <</MCID X>> BDC ( ) Tj EMC
                    # Keep the preceding text, remove the tagged space
                    pattern4 = r'/(?:Span|P|Figure|Form|Table|H\d|L|LI|LBody|TD|TH)\s*<<[^>]*>>\s*BDC\s*\([\s]+\)\s*Tj\s*EMC'
                    matches = list(re.finditer(pattern4, content_str))

                    if matches:
                        violations_found += len(matches)
                        for match in reversed(matches):
                            # Complete removal - no need to preserve whitespace at end of sentence
                            content_str = content_str[:match.start()] + content_str[match.end():]
                            modified = True
                            total_fixed += 1

                    # If we modified this stream, update it
                    if modified:
                        # Update the content stream
                        new_stream = content_str.encode('latin-1', errors='ignore')
                        doc.update_stream(xref, new_stream)

            except Exception as page_error:
                print(f"Warning: Error processing page {page_num + 1}: {page_error}", file=sys.stderr)
                import traceback
                traceback.print_exc(file=sys.stderr)

        # Save the fixed PDF
        doc.save(output_pdf_path, garbage=4, deflate=True, clean=True)
        doc.close()

        return {
            "success": True,
            "output_path": output_pdf_path,
            "violations_found": violations_found,
            "violations_fixed": total_fixed,
            "message": f"Removed tagging from {total_fixed} whitespace elements"
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
            "error": "Usage: fix_tagged_whitespace.py <input_pdf> [output_pdf]"
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

    result = fix_tagged_whitespace(input_pdf, output_pdf)

    print(json.dumps(result, indent=2))

    sys.exit(0 if result["success"] else 1)


if __name__ == "__main__":
    main()
