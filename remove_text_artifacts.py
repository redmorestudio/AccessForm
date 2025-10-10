#!/usr/bin/env python3
"""
Remove artifact markers from text content in PDFs.
This fixes the "Tagged content present inside an artifact" error where
PassportPDF incorrectly marks form field labels as decorative artifacts.
"""

import sys
import fitz  # PyMuPDF

def remove_text_artifacts(input_pdf_path, output_pdf_path=None):
    """
    Remove /Artifact markers from text content while keeping actual artifacts
    (lines, borders, decorative graphics) as artifacts.
    """
    if output_pdf_path is None:
        output_pdf_path = input_pdf_path

    doc = fitz.open(input_pdf_path)
    artifacts_removed = 0

    for page_num, page in enumerate(doc, start=1):
        # Get the page's content stream
        content = page.read_contents().decode('latin-1', errors='ignore')

        # Track if we modified anything
        modified = False
        new_content = []
        lines = content.split('\n')

        i = 0
        while i < len(lines):
            line = lines[i]

            # Look for artifact markers: /Artifact BMC ... EMC
            if '/Artifact' in line and 'BMC' in line:
                # Found start of artifact block
                # Look ahead to find the matching EMC
                artifact_start = i
                artifact_content = [line]
                i += 1

                # Collect all lines until we find EMC
                depth = 1
                has_text_operators = False

                while i < len(lines) and depth > 0:
                    current = lines[i]
                    artifact_content.append(current)

                    # Check if this artifact contains text operators
                    # Text operators: Tj, TJ, ', "
                    if any(op in current for op in ['Tj', 'TJ', "'"]):
                        has_text_operators = True

                    # Check for EMC to close the artifact block
                    if 'EMC' in current:
                        depth -= 1

                    i += 1

                # If this artifact block contains text, remove the artifact markers
                if has_text_operators:
                    # Remove the /Artifact BMC line
                    # Keep the content between BMC and EMC
                    # Remove the EMC line

                    # Skip first line (/Artifact BMC) and last line (EMC)
                    content_lines = artifact_content[1:-1]
                    new_content.extend(content_lines)

                    artifacts_removed += 1
                    modified = True
                    print(f"Page {page_num}: Removed artifact marker from text content")
                else:
                    # Keep the artifact as-is (it's probably a decorative graphic)
                    new_content.extend(artifact_content)
            else:
                # Regular content, keep it
                new_content.append(line)
                i += 1

        # If we modified the content, update the page
        if modified:
            new_content_str = '\n'.join(new_content)

            # Clean and update the page content
            page.clean_contents()
            page.set_contents(new_content_str.encode('latin-1', errors='ignore'))

            print(f"Page {page_num}: Updated content stream")

    # Save the modified PDF
    doc.save(output_pdf_path, garbage=4, deflate=True, clean=True)
    doc.close()

    print(f"\nTotal artifact markers removed from text: {artifacts_removed}")
    return artifacts_removed

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python remove_text_artifacts.py <input_pdf> [output_pdf]")
        sys.exit(1)

    input_pdf = sys.argv[1]
    output_pdf = sys.argv[2] if len(sys.argv) > 2 else input_pdf

    try:
        count = remove_text_artifacts(input_pdf, output_pdf)
        print(f"Successfully processed {input_pdf}")
        print(f"Removed {count} artifact markers from text content")
        sys.exit(0)
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        sys.exit(1)
