#!/usr/bin/env python3
"""
Detect PDF/UA violations: Tagged content present inside artifacts.

This script scans a PDF's content streams to find /Artifact BMC...EMC blocks
that incorrectly contain tagged content (elements with /MCID).

Usage: python3 detect_artifact_violations.py <pdf_path>
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


def detect_artifact_violations(pdf_path):
    """
    Detect tagged content inside artifact markers.

    Returns:
        dict with:
            - success: bool
            - total_violations: int
            - violations: list of dicts with page, tag_type, mcid
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

                # Combine all content streams for this page
                content_str = ""
                for xref in xref_list:
                    stream = doc.xref_stream(xref)
                    if stream:
                        content_str += stream.decode('latin-1', errors='ignore')

                if not content_str:
                    continue

                # Find all /Artifact BMC occurrences
                pos = 0
                while True:
                    artifact_pos = content_str.find('/Artifact BMC', pos)
                    if artifact_pos == -1:
                        break

                    # Find BMC end
                    bmc_end = artifact_pos + len('/Artifact BMC')

                    # Find FIRST EMC after this
                    first_emc = content_str.find('EMC', bmc_end)
                    if first_emc == -1:
                        pos = bmc_end
                        continue

                    # Get content between BMC and first EMC
                    between = content_str[bmc_end:first_emc]

                    # Check if there's a /MCID inside (indicates tagged content)
                    if '/MCID' in between:
                        # Determine tag type
                        tag_type = "unknown"
                        if '/Figure' in between:
                            tag_type = "Figure"
                        elif '/P' in between:
                            tag_type = "P"
                        elif '/Span' in between:
                            tag_type = "Span"
                        elif '/Form' in between:
                            tag_type = "Form"
                        elif '/Table' in between:
                            tag_type = "Table"

                        # Extract MCID number
                        mcid_match = re.search(r'/MCID\s+(\d+)', between)
                        mcid = int(mcid_match.group(1)) if mcid_match else -1

                        # Extract any text for debugging
                        text_matches = re.findall(r'\(([^)]{1,100})\)', between)
                        sample_text = text_matches[0][:80] if text_matches else ""

                        violations.append({
                            'page': page_num + 1,
                            'tag_type': tag_type,
                            'mcid': mcid,
                            'sample_text': sample_text
                        })

                    pos = first_emc + 3

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
        return {
            "success": False,
            "error": str(e)
        }


def main():
    if len(sys.argv) < 2:
        print(json.dumps({
            "success": False,
            "error": "Usage: detect_artifact_violations.py <pdf_path>"
        }))
        sys.exit(1)

    pdf_path = sys.argv[1]
    result = detect_artifact_violations(pdf_path)

    print(json.dumps(result, indent=2))

    sys.exit(0 if result["success"] and result.get("total_violations", 0) == 0 else 1)


if __name__ == "__main__":
    main()
