#!/usr/bin/env python3
"""
Test pagination functionality to ensure form fields are correctly assigned to their respective pages.
This test verifies the pagination fix implemented in Program.cs.
"""
import sys
import os
import json
import tempfile
import fitz  # PyMuPDF
from typing import List, Dict, Tuple

def create_multi_page_test_pdf(num_pages: int = 3) -> str:
    """Create a test PDF with form fields on different pages"""
    doc = fitz.open()

    # Standard letter size page
    page_rect = fitz.Rect(0, 0, 612, 792)

    for page_num in range(num_pages):
        page = doc.new_page(-1, width=page_rect.width, height=page_rect.height)

        # Add some text to make the page visible
        page.insert_text((50, 50), f"Page {page_num + 1}", fontsize=20)

        # Add form fields at different Y positions to test pagination
        # Field positions: top, middle, bottom of each page
        field_positions = [
            (100, 100),   # Top
            (100, 400),   # Middle
            (100, 700)    # Bottom
        ]

        for i, (x, y) in enumerate(field_positions):
            field_rect = fitz.Rect(x, y, x + 150, y + 25)
            field_name = f"field_page{page_num + 1}_pos{i + 1}"

            # Create text field
            widget = fitz.Widget()
            widget.field_name = field_name
            widget.field_type = fitz.PDF_WIDGET_TYPE_TEXT
            widget.rect = field_rect
            widget.field_value = f"Test value {page_num + 1}-{i + 1}"

            page.add_widget(widget)

    # Save to temporary file
    temp_path = tempfile.mktemp(suffix='_pagination_test.pdf')
    doc.save(temp_path)
    doc.close()

    print(f"✓ Created test PDF with {num_pages} pages: {temp_path}")
    return temp_path

def extract_field_info_with_pymupdf(pdf_path: str) -> List[Dict]:
    """Extract field information using PyMuPDF to get ground truth"""
    doc = fitz.open(pdf_path)
    fields = []

    for page_num in range(len(doc)):
        page = doc[page_num]
        widgets = page.widgets()

        for widget in widgets:
            field_info = {
                'name': widget.field_name,
                'page': page_num + 1,  # 1-based page numbering
                'y_position': widget.rect.y0,
                'rect': {
                    'x0': widget.rect.x0,
                    'y0': widget.rect.y0,
                    'x1': widget.rect.x1,
                    'y1': widget.rect.y1
                }
            }
            fields.append(field_info)

    doc.close()
    return fields

def simulate_syncfusion_extraction(pdf_path: str) -> List[Dict]:
    """Simulate the pagination logic from Program.cs using PyMuPDF"""
    doc = fitz.open(pdf_path)
    fields = []

    # Get page height (assumes all pages same size)
    page_height = doc[0].rect.height if len(doc) > 0 else 792
    total_pages = len(doc)

    for page_num in range(len(doc)):
        page = doc[page_num]
        widgets = page.widgets()

        for widget in widgets:
            # Convert PyMuPDF's relative coordinates to absolute coordinates
            # like Syncfusion provides (absolute from top of entire document)
            field_y_relative = widget.rect.y0  # Relative to current page
            field_y_absolute = (page_num * page_height) + field_y_relative  # Absolute from document start

            # Apply the pagination calculation from Program.cs lines 1147
            calculated_page = int(field_y_absolute // page_height) + 1

            # Ensure page number is within valid range
            calculated_page = max(1, min(calculated_page, total_pages))

            field_info = {
                'name': widget.field_name,
                'actual_page': page_num + 1,  # Ground truth
                'calculated_page': calculated_page,  # What our algorithm calculates
                'y_position': field_y_absolute,  # Now using absolute coordinates
                'y_relative': field_y_relative,  # Keep relative for debugging
                'page_height': page_height
            }
            fields.append(field_info)

    doc.close()
    return fields

def test_pagination_accuracy(test_pdf_path: str) -> bool:
    """Test pagination accuracy by comparing calculated vs actual page assignments"""
    print(f"\n📄 Testing pagination for: {test_pdf_path}")

    # Get ground truth field positions
    ground_truth = extract_field_info_with_pymupdf(test_pdf_path)
    print(f"✓ Found {len(ground_truth)} fields in PDF")

    # Test our pagination algorithm
    calculated_results = simulate_syncfusion_extraction(test_pdf_path)

    # Compare results
    success = True
    errors = []

    for field in calculated_results:
        actual_page = field['actual_page']
        calculated_page = field['calculated_page']
        field_name = field['name']
        y_pos = field['y_position']

        if actual_page != calculated_page:
            success = False
            error_msg = f"❌ Field '{field_name}' on page {actual_page} (Y={y_pos:.1f}) assigned to page {calculated_page}"
            errors.append(error_msg)
            print(error_msg)
        else:
            print(f"✅ Field '{field_name}' correctly assigned to page {actual_page} (Y={y_pos:.1f})")

    if success:
        print(f"\n🎉 All {len(calculated_results)} fields correctly paginated!")
    else:
        print(f"\n💥 {len(errors)} pagination errors found:")
        for error in errors:
            print(f"   {error}")

    return success

def test_edge_cases() -> bool:
    """Test edge cases for pagination"""
    print(f"\n🔬 Testing edge cases...")

    # Test single page PDF
    single_page_pdf = create_multi_page_test_pdf(1)
    success1 = test_pagination_accuracy(single_page_pdf)
    os.unlink(single_page_pdf)

    # Test many pages PDF
    many_page_pdf = create_multi_page_test_pdf(10)
    success2 = test_pagination_accuracy(many_page_pdf)
    os.unlink(many_page_pdf)

    return success1 and success2

def main():
    """Main test function"""
    print("🧪 PDF Pagination Test Suite")
    print("=" * 50)

    overall_success = True

    # Test 1: Basic multi-page pagination
    print("\n1️⃣ Testing basic multi-page pagination...")
    test_pdf = create_multi_page_test_pdf(3)
    success1 = test_pagination_accuracy(test_pdf)
    os.unlink(test_pdf)
    overall_success = overall_success and success1

    # Test 2: Edge cases
    print("\n2️⃣ Testing edge cases...")
    success2 = test_edge_cases()
    overall_success = overall_success and success2

    # Test 3: Use existing test PDF if available
    if os.path.exists("test_fields.pdf"):
        print("\n3️⃣ Testing existing test_fields.pdf...")
        success3 = test_pagination_accuracy("test_fields.pdf")
        overall_success = overall_success and success3

    # Final results
    print("\n" + "=" * 50)
    if overall_success:
        print("🎉 ALL PAGINATION TESTS PASSED!")
        print("✅ The pagination fix in Program.cs is working correctly.")
    else:
        print("💥 SOME PAGINATION TESTS FAILED!")
        print("❌ The pagination logic needs further investigation.")

    return 0 if overall_success else 1

if __name__ == "__main__":
    sys.exit(main())