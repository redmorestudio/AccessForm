#!/usr/bin/env python3
"""
Test script for the complete PDF rebuild functionality.
"""
import json
import os
import sys
import tempfile
from pathlib import Path

# Add parent directory to path
sys.path.insert(0, str(Path(__file__).parent))

from pdf_complete_rebuild import PDFCompleteRebuilder

def create_test_pdf():
    """Create a simple test PDF with a field"""
    try:
        import fitz
    except ImportError:
        print("PyMuPDF not installed. Run: pip install PyMuPDF")
        return None
    
    doc = fitz.open()
    page = doc.new_page(width=612, height=792)  # Letter size
    
    # Add some text
    text = "Test PDF Form"
    page.insert_text((50, 50), text, fontsize=20)
    
    # Add a text field using the correct API
    rect = fitz.Rect(100, 100, 300, 120)
    widget = fitz.Widget()
    widget.field_type = fitz.PDF_WIDGET_TYPE_TEXT
    widget.field_name = "OldFieldName"
    widget.rect = rect
    widget.border_width = 1
    page.add_widget(widget)
    widget.update()
    
    # Add a checkbox
    rect2 = fitz.Rect(100, 150, 120, 170)
    widget2 = fitz.Widget()
    widget2.field_type = fitz.PDF_WIDGET_TYPE_CHECKBOX
    widget2.field_name = "OldCheckbox"
    widget2.rect = rect2
    page.add_widget(widget2)
    widget2.update()
    
    # Save to temp file
    temp_path = tempfile.mktemp(suffix='_test.pdf')
    doc.save(temp_path)
    doc.close()
    
    return temp_path

def test_rebuild():
    """Test the PDF rebuild functionality"""
    print("Creating test PDF...")
    test_pdf = create_test_pdf()
    if not test_pdf:
        print("Failed to create test PDF")
        return False
    
    print(f"Test PDF created at: {test_pdf}")
    
    # Define field updates
    field_updates = [
        {
            "originalName": "OldFieldName",
            "newName": "CustomerName",
            "fieldType": "text",
            "X": 100,
            "Y": 100,
            "Width": 250,
            "Height": 25,
            "PageNumber": 1,
            "Tooltip": "Enter your full name",
            "IsRequired": True
        },
        {
            "originalName": "OldCheckbox",
            "newName": "AgreeToTerms",
            "fieldType": "checkbox",
            "X": 100,
            "Y": 150,
            "Width": 20,
            "Height": 20,
            "PageNumber": 1,
            "Tooltip": "Check to agree to terms and conditions"
        },
        {
            "originalName": "NewField",
            "newName": "EmailAddress",
            "fieldType": "text",
            "X": 100,
            "Y": 200,
            "Width": 250,
            "Height": 25,
            "PageNumber": 1,
            "Tooltip": "Enter your email address"
        },
        {
            "originalName": "DateField",
            "newName": "SubmissionDate",
            "fieldType": "date",
            "X": 100,
            "Y": 250,
            "Width": 150,
            "Height": 25,
            "PageNumber": 1,
            "Tooltip": "Select the submission date"
        }
    ]
    
    print("\nTesting PDF rebuild...")
    print(f"Input PDF: {test_pdf}")
    print(f"Number of field updates: {len(field_updates)}")
    
    # Create rebuilder and process
    rebuilder = PDFCompleteRebuilder()
    result = rebuilder.rebuild_pdf(test_pdf, field_updates)
    
    print("\nRebuild Result:")
    print(json.dumps(result, indent=2))
    
    if result['success']:
        print(f"\n✓ Rebuild successful!")
        print(f"  Output PDF: {result['output_path']}")
        print(f"  Total fields: {result['total_fields']}")
        print(f"  Tag elements: {result.get('tag_elements', 0)}")
        
        # Verify the output
        try:
            import fitz
            doc = fitz.open(result['output_path'])
            print(f"\n✓ Output PDF is valid")
            print(f"  Pages: {len(doc)}")
            
            # Check fields
            for page_num, page in enumerate(doc):
                widgets = list(page.widgets())
                if widgets:
                    print(f"  Page {page_num + 1} fields:")
                    for widget in widgets:
                        print(f"    - {widget.field_name} ({widget.field_type_string})")
            
            doc.close()
            
            # Clean up
            if os.path.exists(test_pdf):
                os.remove(test_pdf)
            
            return True
            
        except Exception as e:
            print(f"\n✗ Failed to verify output: {e}")
            return False
    else:
        print(f"\n✗ Rebuild failed: {result.get('error', 'Unknown error')}")
        if 'traceback' in result:
            print("\nTraceback:")
            print(result['traceback'])
        return False

if __name__ == "__main__":
    success = test_rebuild()
    sys.exit(0 if success else 1)