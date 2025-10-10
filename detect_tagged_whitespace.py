#!/usr/bin/env python3
"""
Detect ALL tagged whitespace in a PDF - find every single tagged element that contains only whitespace.
"""

import sys
import json
import re

try:
    import fitz  # PyMuPDF
except ImportError:
    print(json.dumps({
        "success": False,
        "error": "PyMuPDF not installed. Run: pip install PyMuPDF"
    }))
    sys.exit(1)


def detect_tagged_whitespace(pdf_path):
    """
    Find ALL tagged content that contains only whitespace.
    """
    try:
        doc = fitz.open(pdf_path)

        violations = []
        total_pages = len(doc)

        for page_num in range(total_pages):
            page = doc[page_num]

            try:
                # Get content stream(s)
                xrefs = page.get_contents()
                if isinstance(xrefs, list):
                    xref_list = xrefs
                else:
                    xref_list = [xrefs]

                # Combine all content streams
                content_str = ""
                for xref in xref_list:
                    stream = doc.xref_stream(xref)
                    if stream:
                        content_str += stream.decode('latin-1', errors='ignore')

                if not content_str:
                    continue

                # Find ALL tagged content blocks (BDC...EMC)
                # Pattern: /TagName <</MCID X>> BDC ... EMC
                pattern = r'/(\w+)\s*<<[^>]*>>\s*BDC\s*(.*?)\s*EMC'
                matches = re.finditer(pattern, content_str, re.DOTALL)

                for match in matches:
                    tag_type = match.group(1)
                    content = match.group(2)

                    # Check if content contains ONLY whitespace and text operators
                    # Remove all text operators to see what's left
                    text_only = content

                    # Remove text operators
                    text_only = re.sub(r'\s*Tj\s*', '', text_only)
                    text_only = re.sub(r'\s*TJ\s*', '', text_only)
                    text_only = re.sub(r'\s*Td\s*', '', text_only)
                    text_only = re.sub(r'\s*TD\s*', '', text_only)
                    text_only = re.sub(r'\s*Tm\s*', '', text_only)
                    text_only = re.sub(r'\s*T\*\s*', '', text_only)
                    text_only = re.sub(r"'", '', text_only)
                    text_only = re.sub(r'"', '', text_only)

                    # Extract text strings from parentheses
                    text_strings = re.findall(r'\(([^)]*)\)', text_only)

                    # Check if ALL text strings are whitespace-only
                    if text_strings:
                        all_whitespace = True
                        for text in text_strings:
                            # Unescape common PDF escapes
                            unescaped = text.replace('\\n', '\n').replace('\\r', '\r').replace('\\t', '\t')
                            if unescaped.strip():  # If there's any non-whitespace
                                all_whitespace = False
                                break

                        if all_whitespace:
                            # This is a violation!
                            sample = text_strings[0][:50] if text_strings else ""
                            violations.append({
                                'page': page_num + 1,
                                'tag_type': tag_type,
                                'text': sample,
                                'full_block': content[:200]
                            })

            except Exception as page_error:
                print(f"Warning: Error processing page {page_num + 1}: {page_error}", file=sys.stderr)

        doc.close()

        return {
            "success": True,
            "total_violations": len(violations),
            "total_pages": total_pages,
            "violations": violations
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
            "error": "Usage: detect_tagged_whitespace.py <pdf_path>"
        }))
        sys.exit(1)

    pdf_path = sys.argv[1]
    result = detect_tagged_whitespace(pdf_path)

    print(json.dumps(result, indent=2))

    sys.exit(0 if result["success"] and result.get("total_violations", 0) == 0 else 1)


if __name__ == "__main__":
    main()
