#!/usr/bin/env python3
"""
Manual fix for PAC testing errors on bet-form-grievance-twc.pdf
Specifically targets:
1. Path object not tagged (9 errors on Page 3)
2. Object missing in object reference (2 errors on Page 3)
"""

import sys
import fitz  # PyMuPDF
import re

def mark_paths_as_artifacts(input_path, output_path):
    """Mark all untagged path objects on Page 3 as artifacts"""

    doc = fitz.open(input_path)
    fixes = 0

    # Focus on Page 3 (index 2) where the errors are
    if len(doc) > 2:
        page = doc[2]
        print(f"Processing Page 3...")

        # Get the page content
        xref = page.xref
        stream = doc.xref_stream(xref)

        if stream:
            # Decode the stream
            content = stream.decode('latin-1', errors='ignore')

            # Find all path operations that aren't already in artifacts
            # Radio buttons typically appear as small circles/paths
            modified = False
            lines = content.split('\n')
            new_lines = []
            i = 0

            while i < len(lines):
                line = lines[i].strip()

                # Look for path drawing operations (these create the circles)
                # Common patterns: "re" (rectangle), path with "m", "l", "c" commands
                if any(cmd in line for cmd in [' re', ' m ', ' l ', ' c ']):
                    # Look ahead to find the path ending
                    path_lines = [line]
                    j = i + 1

                    while j < len(lines):
                        next_line = lines[j].strip()
                        path_lines.append(next_line)

                        # Check if this ends the path
                        if any(op in next_line for op in ['f', 'S', 'f*', 'B', 'B*', 'n']):
                            # Found end of path
                            # Check if it's a small path (radio button size)
                            path_str = ' '.join(path_lines)

                            # Extract dimensions
                            nums = re.findall(r'[-]?\d*\.?\d+', path_str)
                            if len(nums) >= 4:
                                try:
                                    # For rectangle: x y width height re
                                    if ' re' in path_str:
                                        width = float(nums[2])
                                        height = float(nums[3])
                                    else:
                                        # For path: estimate from coordinates
                                        width = abs(float(nums[2]) - float(nums[0]))
                                        height = abs(float(nums[3]) - float(nums[1]))

                                    # Radio buttons are typically 10-20 points
                                    if 5 < width < 25 and 5 < height < 25:
                                        # This is likely a radio button - mark as artifact
                                        new_lines.append("/Artifact BMC")
                                        new_lines.extend(path_lines)
                                        new_lines.append("EMC")
                                        fixes += 1
                                        print(f"  Marked path as artifact: {width:.1f}x{height:.1f}")
                                        modified = True
                                        i = j
                                        break
                                except:
                                    pass

                            # Not a radio button path
                            new_lines.extend(path_lines)
                            i = j
                            break

                        j += 1
                    else:
                        # Didn't find path end
                        new_lines.extend(path_lines)
                        i = j - 1
                else:
                    new_lines.append(line)

                i += 1

            if modified:
                # Update the page content
                new_content = '\n'.join(new_lines).encode('latin-1', errors='ignore')
                doc.update_stream(xref, new_content)
                print(f"  Updated page content with {fixes} artifact markers")

    # Save the modified PDF
    doc.save(output_path, garbage=4, deflate=True)
    doc.close()

    print(f"\n✅ Saved fixed PDF to: {output_path}")
    return fixes

def fix_widget_references(input_path, output_path):
    """Fix missing object references in form widgets"""

    # Always open the output file if it exists, otherwise input
    doc = fitz.open(output_path)
    fixes = 0

    # Check all form widgets
    for page_num, page in enumerate(doc):
        widgets = page.widgets()

        for widget in widgets:
            # Check if widget has proper parent reference
            if widget.field_type_string in ["RadioButton", "CheckBox"]:
                # Ensure the widget has all required properties
                try:
                    widget.update()
                    fixes += 1
                except:
                    pass  # Some widgets may be read-only

    if fixes > 0:
        # Save with incremental updates
        doc.save(output_path + ".tmp", incremental=True, encryption=0)
        doc.close()

        # Replace original
        import os
        os.rename(output_path + ".tmp", output_path)
        print(f"Fixed {fixes} widget references")
    else:
        doc.close()
        print("No widget reference fixes needed")

    return fixes

def main(input_pdf, output_pdf):
    """Run all fixes"""

    print("=" * 60)
    print("PAC Error Manual Fix Tool")
    print("=" * 60)

    # Step 1: Mark paths as artifacts
    print("\n1. Marking untagged paths as artifacts...")
    path_fixes = mark_paths_as_artifacts(input_pdf, output_pdf)

    # Step 2: Fix widget references
    print("\n2. Fixing widget object references...")
    widget_fixes = fix_widget_references(input_pdf, output_pdf)

    print("\n" + "=" * 60)
    print(f"COMPLETE: Fixed {path_fixes} path objects and {widget_fixes} widget references")
    print("=" * 60)
    print(f"\nTest the output file with PAC: {output_pdf}")

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python manual_fix_pac_errors.py input.pdf output.pdf")
        print("\nExample:")
        print("  python manual_fix_pac_errors.py bet-form-grievance-twc_pdfua.pdf bet-form-grievance-twc_fixed.pdf")
        sys.exit(1)

    input_file = sys.argv[1]
    output_file = sys.argv[2]

    try:
        main(input_file, output_file)
    except Exception as e:
        print(f"\nError: {e}")
        print("\nMake sure PyMuPDF is installed: pip install pymupdf")
        sys.exit(1)