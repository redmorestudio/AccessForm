#!/usr/bin/env python3
"""
Fix PDF/UA violations by unwrapping tagged content from artifacts.

This script finds /Artifact BMC...EMC blocks that contain tagged content (/MCID)
and removes ONLY the artifact markers, keeping the tagged content intact.

Usage: python3 fix_artifact_violations.py <input_pdf> [output_pdf]
"""

import sys
import json
import tempfile
import os

try:
    import fitz  # PyMuPDF
except ImportError:
    print(json.dumps({
        "success": False,
        "error": "PyMuPDF not installed. Run: pip install PyMuPDF"
    }))
    sys.exit(1)


def fix_artifact_violations(input_pdf_path, output_pdf_path=None):
    """
    Remove artifact markers from tagged content.

    This removes the /Artifact BMC...EMC wrapper from blocks that contain
    tagged content (/MCID), while preserving the tagged content itself.
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

                    # Find and fix all /Artifact BMC blocks with tagged content
                    while True:
                        artifact_pos = content_str.find('/Artifact BMC')
                        if artifact_pos == -1:
                            break

                        # Find BMC end
                        bmc_end = artifact_pos + len('/Artifact BMC')

                        # Find MATCHING EMC by counting nesting levels
                        # We need to count BMC/BDC and EMC to find the right one
                        nesting_level = 1
                        search_pos = bmc_end
                        matching_emc = -1

                        while search_pos < len(content_str):
                            # Look for next BMC, BDC, or EMC
                            next_bmc = content_str.find('BMC', search_pos)
                            next_bdc = content_str.find('BDC', search_pos)
                            next_emc = content_str.find('EMC', search_pos)

                            # Find the earliest one
                            earliest = None
                            earliest_pos = len(content_str)

                            if next_bmc != -1 and next_bmc < earliest_pos:
                                earliest = 'BMC'
                                earliest_pos = next_bmc
                            if next_bdc != -1 and next_bdc < earliest_pos:
                                earliest = 'BDC'
                                earliest_pos = next_bdc
                            if next_emc != -1 and next_emc < earliest_pos:
                                earliest = 'EMC'
                                earliest_pos = next_emc

                            if earliest is None:
                                break  # No more markers found

                            if earliest == 'BMC' or earliest == 'BDC':
                                nesting_level += 1
                                search_pos = earliest_pos + 3
                            elif earliest == 'EMC':
                                nesting_level -= 1
                                if nesting_level == 0:
                                    matching_emc = earliest_pos
                                    break
                                search_pos = earliest_pos + 3

                        if matching_emc == -1:
                            # No matching EMC, skip this artifact
                            content_str = content_str[:artifact_pos] + '###PROCESSED###' + content_str[artifact_pos + 13:]
                            continue

                        # Get content between BMC and EMC
                        between = content_str[bmc_end:matching_emc]

                        # Check if this artifact contains tagged content (/MCID)
                        if '/MCID' in between:
                            violations_found += 1

                            # This is a violation - unwrap the tagged content from artifact
                            # BUT: if the content is ONLY whitespace, delete it entirely

                            # Check what type of content this is
                            import re

                            # Check if it's whitespace-only
                            is_whitespace_only = False
                            text_strings = re.findall(r'\(([^)]*)\)', between)
                            if text_strings:
                                is_whitespace_only = True
                                for text in text_strings:
                                    unescaped = text.replace('\\n', '\n').replace('\\r', '\r').replace('\\t', '\t').replace('\\(', '(').replace('\\)', ')')
                                    if unescaped.strip():
                                        is_whitespace_only = False
                                        break

                            # Check if it's ONLY path/graphics commands (no text, no images)
                            # Path operators: m, l, c, v, y, h, re, S, s, f, F, f*, B, B*, b, b*, n, W, W*
                            # If there's no text operators (Tj, TJ, ') and no image operators (Do), it's just paths
                            has_text = bool(re.search(r'\bTj\b|\bTJ\b|\'|"', between))
                            has_image = bool(re.search(r'\bDo\b', between))
                            is_path_only = not has_text and not has_image

                            # Always preserve the tagged content, just remove the artifact wrapper
                            # Even if it's whitespace or paths - if Word tagged it, we keep it tagged
                            # The only exception is if it's UNTAGGED whitespace/paths
                            has_mcid = '/MCID' in between

                            if (is_whitespace_only or is_path_only) and not has_mcid:
                                # Untagged whitespace or paths - delete entirely
                                fixed_content = content_str[:artifact_pos] + content_str[matching_emc + 3:]
                            else:
                                # Either real content OR tagged whitespace/paths - keep with tags
                                fixed_content = content_str[:artifact_pos] + between + content_str[matching_emc + 3:]

                            content_str = fixed_content
                            modified = True
                            total_fixed += 1
                        else:
                            # This artifact is OK (no tagged content inside)
                            # Mark it as processed so we skip it
                            content_str = content_str[:artifact_pos] + '###PROCESSED###' + content_str[artifact_pos + 13:]

                    # Restore the markers we used for tracking
                    content_str = content_str.replace('###PROCESSED###', '/Artifact BMC')

                    # CLEANUP PASS: Remove all untagged whitespace-only text operations
                    # Pattern: ( ) Tj or (  ) Tj or (\40) Tj etc. that are NOT inside BDC...EMC
                    # This handles end-of-sentence spaces and other stray whitespace
                    import re

                    # Find all text operations that are OUTSIDE of any BDC...EMC blocks

                    # Pattern: whitespace-only text followed by Tj, TJ, ', or "
                    # This includes: ( ) Tj, (  ) Tj, (\n) Tj, (\40) Tj (octal space), etc.
                    # Also handles: [( )] TJ (array form)
                    # Match parentheses containing only:
                    #  - literal spaces and whitespace
                    #  - backslash followed by octal codes (40, 11, 12, 15 - octal)
                    #  - backslash followed by n, r, t
                    # Note: In the PDF content stream, \40 appears as literal backslash-40
                    # In raw string: \\ matches one backslash
                    whitespace_pattern = r'(\[)?\((\\(40|11|12|15|n|r|t)|\s)*\)(\])?\s*T[Jj]|(\[)?\((\\(40|11|12|15|n|r|t)|\s)*\)(\])?\s*[\'"]'

                    # Find all matches
                    matches = list(re.finditer(whitespace_pattern, content_str))

                    # Check each match to see if it's inside a BDC...EMC block
                    for match in reversed(matches):  # Reverse to preserve indices
                        match_pos = match.start()

                        # Check if this is inside a tagged block (BDC/BMC...EMC)
                        # Count BDC, BMC, and EMC before this position
                        before = content_str[:match_pos]
                        bdc_count = before.count('BDC')
                        bmc_count = before.count('BMC')
                        emc_count = before.count('EMC')

                        # If total opens equals closes, we're NOT inside any marked content block
                        if (bdc_count + bmc_count) == emc_count:
                            # This is untagged whitespace - delete it
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
            "message": f"Fixed {total_fixed} artifact violations"
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
            "error": "Usage: fix_artifact_violations.py <input_pdf> [output_pdf]"
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

    result = fix_artifact_violations(input_pdf, output_pdf)

    print(json.dumps(result, indent=2))

    sys.exit(0 if result["success"] else 1)


if __name__ == "__main__":
    main()
