#!/usr/bin/env python3
"""
Analyze untagged text in context to understand exactly where they appear.
Shows the text before and after untagged content to identify patterns.

Usage: python3 analyze_untagged_context.py <input_pdf>
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


def analyze_untagged_context(input_pdf_path):
    """
    Analyze untagged text operations showing their context.
    """
    try:
        doc = fitz.open(input_pdf_path)

        findings = []

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

                    # Find ALL BT...ET blocks
                    bt_pattern = r'BT(.*?)ET'
                    bt_matches = list(re.finditer(bt_pattern, content, re.DOTALL))

                    for bt_match in bt_matches:
                        block_content = bt_match.group(1)

                        # Skip blocks with no tagging at all
                        if 'BDC' not in block_content and 'BMC' not in block_content:
                            continue

                        # This block has some tagging - analyze line by line
                        lines = block_content.split('\n')
                        depth = 0

                        for i, line in enumerate(lines):
                            # Calculate depth before this line
                            temp_depth = 0
                            for j in range(i):
                                prev_line = lines[j]
                                temp_depth += len(re.findall(r'\bBDC\b', prev_line))
                                temp_depth += len(re.findall(r'\bBMC\b', prev_line))
                                temp_depth -= len(re.findall(r'\bEMC\b', prev_line))
                            temp_depth = max(0, temp_depth)

                            # Check for text operations when depth is 0
                            if temp_depth == 0:
                                # Look for any text operations
                                tj_match = re.search(r'\(([^)]*)\)\s*Tj', line)
                                if tj_match:
                                    text_content = tj_match.group(1)

                                    # Get context - previous and next tagged text
                                    prev_text = ""
                                    next_text = ""

                                    # Look back for previous tagged text
                                    for j in range(i-1, -1, -1):
                                        if 'Tj' in lines[j] or 'TJ' in lines[j]:
                                            m = re.search(r'\(([^)]*)\)', lines[j])
                                            if m:
                                                prev_text = m.group(1)
                                                break

                                    # Look forward for next tagged text
                                    for j in range(i+1, len(lines)):
                                        if 'Tj' in lines[j] or 'TJ' in lines[j]:
                                            m = re.search(r'\(([^)]*)\)', lines[j])
                                            if m:
                                                next_text = m.group(1)
                                                break

                                    # Decode text
                                    decoded = text_content
                                    decoded = decoded.replace('\\40', ' ')
                                    decoded = decoded.replace('\\(', '(')
                                    decoded = decoded.replace('\\)', ')')

                                    # Check if it's whitespace
                                    is_whitespace = all(c <= ' ' for c in decoded)

                                    findings.append({
                                        "page": page_num + 1,
                                        "text": decoded if decoded else "[EMPTY]",
                                        "raw": text_content,
                                        "is_whitespace": is_whitespace,
                                        "before": prev_text[-20:] if prev_text else "[START]",
                                        "after": next_text[:20] if next_text else "[END]",
                                        "line": line.strip()
                                    })

                                # Check for hex strings
                                hex_match = re.search(r'<([0-9A-Fa-f]+)>\s*Tj', line)
                                if hex_match:
                                    hex_content = hex_match.group(1)

                                    # Get context
                                    prev_text = ""
                                    next_text = ""

                                    # Look back for previous text
                                    for j in range(i-1, -1, -1):
                                        if 'Tj' in lines[j] or 'TJ' in lines[j]:
                                            m = re.search(r'\(([^)]*)\)', lines[j])
                                            if m:
                                                prev_text = m.group(1)
                                                break

                                    # Look forward for next text
                                    for j in range(i+1, len(lines)):
                                        if 'Tj' in lines[j] or 'TJ' in lines[j]:
                                            m = re.search(r'\(([^)]*)\)', lines[j])
                                            if m:
                                                next_text = m.group(1)
                                                break

                                    findings.append({
                                        "page": page_num + 1,
                                        "text": f"<HEX:{hex_content}>",
                                        "raw": hex_content,
                                        "is_whitespace": hex_content in ['0020', '0003', '0009', '000A', '000D', '00A0'],
                                        "before": prev_text[-20:] if prev_text else "[START]",
                                        "after": next_text[:20] if next_text else "[END]",
                                        "line": line.strip()
                                    })

            except Exception as page_error:
                print(f"Warning: Error processing page {page_num + 1}: {page_error}", file=sys.stderr)

        # Analyze patterns
        print("\n=== UNTAGGED TEXT IN CONTEXT ===\n")

        # Group by pattern
        after_colon = []
        after_paren = []
        random_spaces = []
        random_chars = []

        for f in findings:
            if f['before'].endswith(':'):
                after_colon.append(f)
            elif f['before'].endswith(')'):
                after_paren.append(f)
            elif f['is_whitespace']:
                random_spaces.append(f)
            else:
                random_chars.append(f)

        if after_colon:
            print(f"\n** {len(after_colon)} instances after colons:")
            for f in after_colon[:5]:  # Show first 5
                print(f"  Page {f['page']}: '{f['before']}' -> [{f['text']}] -> '{f['after']}'")

        if after_paren:
            print(f"\n** {len(after_paren)} instances after parentheses:")
            for f in after_paren[:5]:
                print(f"  Page {f['page']}: '{f['before']}' -> [{f['text']}] -> '{f['after']}'")

        if random_spaces:
            print(f"\n** {len(random_spaces)} random spaces:")
            for f in random_spaces[:5]:
                print(f"  Page {f['page']}: '{f['before']}' -> [{f['text']}] -> '{f['after']}'")

        if random_chars:
            print(f"\n** {len(random_chars)} random characters:")
            for f in random_chars[:5]:
                print(f"  Page {f['page']}: '{f['before']}' -> [{f['text']}] -> '{f['after']}'")
                print(f"    Raw: {f['line']}")

        return {
            "total_untagged": len(findings),
            "after_colon": len(after_colon),
            "after_paren": len(after_paren),
            "random_spaces": len(random_spaces),
            "random_chars": len(random_chars),
            "findings": findings[:20]  # First 20 for JSON output
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
            "error": "Usage: analyze_untagged_context.py <input_pdf>"
        }))
        sys.exit(1)

    input_pdf = sys.argv[1]

    if not os.path.exists(input_pdf):
        print(json.dumps({
            "success": False,
            "error": f"Input PDF not found: {input_pdf}"
        }))
        sys.exit(1)

    result = analyze_untagged_context(input_pdf)
    print("\n" + "="*50)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    import os
    main()