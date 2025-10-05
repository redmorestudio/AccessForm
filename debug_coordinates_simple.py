#!/usr/bin/env python3
import fitz  # PyMuPDF
import requests
import json
import sys
from pathlib import Path

def test_field_detection(pdf_path):
    """Test field detection and analyze coordinate system"""

    print(f"Testing with PDF: {pdf_path}")
    print("=" * 80)

    # Open PDF with PyMuPDF to get ground truth
    doc = fitz.open(pdf_path)
    page = doc[0]

    print(f"PDF Page Info:")
    print(f"  Page size: {page.rect.width} x {page.rect.height} points")
    print(f"  MediaBox: {page.mediabox}")
    print(f"  CropBox: {page.cropbox}")
    print()

    # Get fields using server detection
    print("Calling field detection API...")
    with open(pdf_path, 'rb') as f:
        files = {'file': ('test.pdf', f, 'application/pdf')}
        response = requests.post('http://localhost:5010/api/fields/load', files=files)

    if response.status_code != 200:
        print(f"ERROR: API returned {response.status_code}")
        print(response.text)
        return

    fields = response.json()
    print(f"Detected {len(fields)} fields")
    print()

    # Print coordinate details for all fields
    print("Field Coordinate Analysis:")
    print("-" * 80)
    print(f"{'Field Name':<30} {'Type':<15} {'X':<8} {'Y':<8} {'Width':<8} {'Height':<8} {'Page':<5} {'Source':<15}")
    print("-" * 80)

    for i, field in enumerate(fields):
        name = field.get('fieldName', 'Unknown')[:29]
        ftype = field.get('fieldType', 'Unknown')[:14]
        source = field.get('source', 'Unknown')[:14]

        # Check for new coordinate system
        if 'coordinates' in field and field['coordinates']:
            coords = field['coordinates']
            x = coords.get('pdfX', 0)
            y = coords.get('pdfY', 0)
            width = coords.get('pdfWidth', 0)
            height = coords.get('pdfHeight', 0)
            page_num = coords.get('pageNumber', 1)

            print(f"{name:<30} {ftype:<15} {x:<8.2f} {y:<8.2f} {width:<8.2f} {height:<8.2f} {page_num:<5} {source:<15}")

            # Detailed debug for first 3 fields
            if i < 3:
                print(f"  -> Universal Coordinates detected")
                print(f"     Source system: {coords.get('sourceSystem', 'Unknown')}")
                print(f"     Page dimensions: {coords.get('pageWidth', 0):.2f} x {coords.get('pageHeight', 0):.2f}")

                # Calculate display coordinates (top-left origin)
                page_height = coords.get('pageHeight', 792)
                display_x = x
                display_y = page_height - (y + height)
                print(f"     Display coords (top-left): ({display_x:.2f}, {display_y:.2f})")

                # Calculate percentage
                page_width = coords.get('pageWidth', 612)
                pct_x = (x / page_width) * 100
                pct_y = (display_y / page_height) * 100
                pct_w = (width / page_width) * 100
                pct_h = (height / page_height) * 100
                print(f"     CSS percentages: left:{pct_x:.1f}% top:{pct_y:.1f}% width:{pct_w:.1f}% height:{pct_h:.1f}%")
                print()
        else:
            # Legacy coordinates
            x = field.get('x', 0)
            y = field.get('y', 0)
            width = field.get('width', 0)
            height = field.get('height', 0)
            page_num = field.get('pageNumber', 1)

            print(f"{name:<30} {ftype:<15} {x:<8.2f} {y:<8.2f} {width:<8.2f} {height:<8.2f} {page_num:<5} {source:<15}")

            if i < 3:
                print(f"  -> Legacy coordinates (no Universal system)")
                print()

    # Test coordinate transformation
    print("\nTesting coordinate transformations:")
    print("-" * 80)
    test_coordinate_transforms(page.rect.width, page.rect.height)

    # Check for coordinate mismatches
    print("\nCoordinate System Check:")
    print("-" * 80)
    analyze_coordinate_issues(fields, page.rect.width, page.rect.height)

    doc.close()

def test_coordinate_transforms(page_width, page_height):
    """Test specific coordinate transformations"""

    test_cases = [
        {"name": "Top-left corner", "pdf_x": 0, "pdf_y": page_height - 30, "width": 100, "height": 30},
        {"name": "Bottom-left corner", "pdf_x": 0, "pdf_y": 0, "width": 100, "height": 30},
        {"name": "Center", "pdf_x": page_width/2 - 50, "pdf_y": page_height/2 - 15, "width": 100, "height": 30},
        {"name": "Top-right corner", "pdf_x": page_width - 100, "pdf_y": page_height - 30, "width": 100, "height": 30},
    ]

    for test in test_cases:
        print(f"{test['name']}:")
        print(f"  PDF coords (bottom-left origin): ({test['pdf_x']:.2f}, {test['pdf_y']:.2f})")

        # Convert to display coordinates (top-left origin)
        display_x = test['pdf_x']
        display_y = page_height - (test['pdf_y'] + test['height'])
        print(f"  Display coords (top-left origin): ({display_x:.2f}, {display_y:.2f})")

        # Convert to percentages
        pct_x = (test['pdf_x'] / page_width) * 100
        pct_y = (display_y / page_height) * 100
        print(f"  Percentage (for CSS): left:{pct_x:.2f}% top:{pct_y:.2f}%")
        print()

def analyze_coordinate_issues(fields, page_width, page_height):
    """Analyze potential coordinate system issues"""

    issues = []

    for field in fields:
        name = field.get('fieldName', 'Unknown')

        # Get coordinates
        if 'coordinates' in field and field['coordinates']:
            coords = field['coordinates']
            x = coords.get('pdfX', 0)
            y = coords.get('pdfY', 0)
            width = coords.get('pdfWidth', 0)
            height = coords.get('pdfHeight', 0)
        else:
            x = field.get('x', 0)
            y = field.get('y', 0)
            width = field.get('width', 0)
            height = field.get('height', 0)

        # Check for out-of-bounds coordinates
        if x < 0 or y < 0:
            issues.append(f"{name}: Negative coordinates ({x:.2f}, {y:.2f})")

        if x + width > page_width:
            issues.append(f"{name}: X overflow ({x:.2f} + {width:.2f} = {x+width:.2f} > {page_width:.2f})")

        if y + height > page_height:
            issues.append(f"{name}: Y overflow ({y:.2f} + {height:.2f} = {y+height:.2f} > {page_height:.2f})")

        # Check for suspiciously small or large dimensions
        if width < 10 or height < 5:
            issues.append(f"{name}: Very small field ({width:.2f} x {height:.2f})")

        if width > page_width * 0.9 or height > page_height * 0.5:
            issues.append(f"{name}: Very large field ({width:.2f} x {height:.2f})")

    if issues:
        print("Found potential issues:")
        for issue in issues:
            print(f"  - {issue}")
    else:
        print("No obvious coordinate issues found")

    # Check coordinate consistency
    print("\nCoordinate Consistency Check:")
    has_universal = any('coordinates' in f and f['coordinates'] for f in fields)
    has_legacy = any('coordinates' not in f or not f['coordinates'] for f in fields)

    if has_universal and has_legacy:
        print("  WARNING: Mixed coordinate systems detected!")
        print("  Some fields use Universal, others use Legacy")
    elif has_universal:
        print("  All fields use Universal coordinate system ✓")
    elif has_legacy:
        print("  All fields use Legacy coordinate system")

    # Check for Y-axis inversion issues
    print("\nY-Axis Analysis:")
    if fields:
        avg_y = sum(f.get('coordinates', {}).get('pdfY', f.get('y', 0)) for f in fields) / len(fields)
        if avg_y > page_height / 2:
            print(f"  Average Y position: {avg_y:.2f} (upper half of page)")
            print("  This suggests PDF coordinates (bottom-left origin)")
        else:
            print(f"  Average Y position: {avg_y:.2f} (lower half of page)")
            print("  This might indicate inverted Y-axis or display coordinates")

def main():
    if len(sys.argv) < 2:
        # Look for PDFs in the current directory
        pdfs = list(Path('.').glob('*.pdf'))
        if pdfs:
            pdf_path = str(pdfs[0])
            print(f"No PDF specified, using: {pdf_path}")
        else:
            print("Usage: python debug_coordinates_simple.py <pdf_file>")
            sys.exit(1)
    else:
        pdf_path = sys.argv[1]

    if not Path(pdf_path).exists():
        print(f"Error: PDF file not found: {pdf_path}")
        sys.exit(1)

    test_field_detection(pdf_path)

if __name__ == "__main__":
    main()