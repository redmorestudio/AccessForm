#!/usr/bin/env python3
import fitz  # PyMuPDF
import sys

def test_field_modification(pdf_path):
    """Test if PyMuPDF can actually modify field names"""
    try:
        doc = fitz.open(pdf_path)
        print(f"Opened PDF: {pdf_path}")
        print(f"Pages: {len(doc)}")
        
        field_count = 0
        for page_num in range(len(doc)):
            page = doc[page_num]
            widgets = list(page.widgets())
            
            print(f"\nPage {page_num + 1}: {len(widgets)} widgets")
            
            for widget in widgets:
                field_count += 1
                print(f"  Field {field_count}:")
                print(f"    Original name: '{widget.field_name}'")
                print(f"    Type: {widget.field_type}")
                print(f"    Type value: {widget.field_type_string}")
                
                # Try to modify the field name
                if widget.field_name and "[" in widget.field_name:
                    new_name = widget.field_name.split("[")[0]
                    print(f"    Attempting to change to: '{new_name}'")
                    widget.field_name = new_name
                    widget.update()
                    print(f"    After update: '{widget.field_name}'")
        
        if field_count > 0:
            # Save to test output
            output_path = pdf_path.replace(".pdf", "_pymupdf_test.pdf")
            doc.save(output_path)
            print(f"\nSaved to: {output_path}")
            
            # Re-open to verify changes
            doc2 = fitz.open(output_path)
            print("\nVerifying saved PDF:")
            for page_num in range(len(doc2)):
                page = doc2[page_num]
                for widget in page.widgets():
                    if widget.field_name:
                        print(f"  Field name in saved PDF: '{widget.field_name}'")
            doc2.close()
        else:
            print("No form fields found in PDF")
            
        doc.close()
        
    except Exception as e:
        print(f"Error: {e}")
        import traceback
        traceback.print_exc()

if __name__ == "__main__":
    if len(sys.argv) > 1:
        test_field_modification(sys.argv[1])
    else:
        print("Usage: test_pymupdf.py <pdf_path>")