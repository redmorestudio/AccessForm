#!/usr/bin/env python3
"""
Analyze PDF tag structure and form fields for Green Bay Remote Work form
"""

import sys
import json
from pathlib import Path
import PyPDF2
from PyPDF2 import PdfReader
import fitz  # PyMuPDF for better tag tree access

def analyze_pdf_structure(pdf_path):
    """Analyze PDF tag structure and form fields"""

    print(f"Analyzing PDF: {pdf_path}")
    print("=" * 80)

    # Basic PDF info using PyPDF2
    try:
        reader = PdfReader(pdf_path)
        print(f"\n1. BASIC PDF INFO:")
        print(f"   Pages: {len(reader.pages)}")
        print(f"   Has forms: {'/AcroForm' in reader.trailer['/Root']}")

        # Check for structure tree (tags)
        if '/StructTreeRoot' in reader.trailer['/Root']:
            print(f"   Has structure tree: Yes")
            struct_tree = reader.trailer['/Root']['/StructTreeRoot']
            if '/K' in struct_tree:
                print(f"   Root structure elements: {len(struct_tree.get('/K', []))}")
        else:
            print(f"   Has structure tree: No")

        # Check for marked content
        if '/MarkInfo' in reader.trailer['/Root']:
            mark_info = reader.trailer['/Root']['/MarkInfo']
            print(f"   Marked content: {mark_info.get('/Marked', False)}")

        # Analyze form fields
        if '/AcroForm' in reader.trailer['/Root']:
            acroform = reader.trailer['/Root']['/AcroForm']
            fields = acroform.get('/Fields', [])
            print(f"\n2. FORM FIELDS:")
            print(f"   Total fields: {len(fields)}")

            # Categorize fields
            field_types = {}
            for field_ref in fields[:10]:  # Sample first 10
                field = field_ref.get_object()
                ft = field.get('/FT', 'Unknown')
                field_types[str(ft)] = field_types.get(str(ft), 0) + 1

                # Check if field has parent structure element
                if '/StructParent' in field:
                    print(f"   Field has StructParent: {field.get('/T', 'Unnamed')}")

            print(f"   Field types: {field_types}")

    except Exception as e:
        print(f"PyPDF2 analysis error: {e}")

    # More detailed analysis with PyMuPDF
    print(f"\n3. DETAILED STRUCTURE ANALYSIS (PyMuPDF):")
    try:
        doc = fitz.open(pdf_path)

        # Check for structure tree
        for page_num, page in enumerate(doc, 1):
            print(f"\n   Page {page_num}:")

            # Get page text with structure info
            text_dict = page.get_text("dict")

            # Count blocks and their types
            blocks = text_dict.get("blocks", [])
            text_blocks = sum(1 for b in blocks if b.get("type") == 0)
            image_blocks = sum(1 for b in blocks if b.get("type") == 1)

            print(f"   - Text blocks: {text_blocks}")
            print(f"   - Image blocks: {image_blocks}")

            # Check for widgets (form fields)
            widgets = page.widgets()
            widget_count = 0
            widget_types = {}
            for widget in widgets:
                widget_count += 1
                widget_type = widget.field_type_string
                widget_types[widget_type] = widget_types.get(widget_type, 0) + 1

                # Check field properties
                if widget_count <= 5:  # Show first 5 fields
                    print(f"   - Field: {widget.field_name or 'Unnamed'}")
                    print(f"     Type: {widget_type}")
                    print(f"     Value: {widget.field_value or 'Empty'}")

            if widget_count > 0:
                print(f"   - Total widgets: {widget_count}")
                print(f"   - Widget types: {widget_types}")

        doc.close()

    except Exception as e:
        print(f"PyMuPDF analysis error: {e}")

    # Try to extract tag tree structure
    print(f"\n4. TAG TREE STRUCTURE:")
    try:
        with open(pdf_path, 'rb') as f:
            reader = PdfReader(f)

            if '/StructTreeRoot' in reader.trailer['/Root']:
                struct_root = reader.trailer['/Root']['/StructTreeRoot']

                def analyze_struct_element(elem, level=0, max_level=3):
                    """Recursively analyze structure elements"""
                    if level > max_level:
                        return

                    indent = "  " * level

                    if isinstance(elem, dict):
                        elem_type = elem.get('/S', 'Unknown')
                        print(f"{indent}- Type: {elem_type}")

                        # Check for Alt text
                        if '/Alt' in elem:
                            print(f"{indent}  Alt: {elem['/Alt'][:50]}...")

                        # Check for actual text
                        if '/ActualText' in elem:
                            print(f"{indent}  ActualText: {elem['/ActualText'][:50]}...")

                        # Check children
                        if '/K' in elem:
                            children = elem['/K']
                            if isinstance(children, list):
                                print(f"{indent}  Children: {len(children)}")
                                for child in children[:2]:  # First 2 children
                                    if hasattr(child, 'get_object'):
                                        analyze_struct_element(child.get_object(), level + 1)
                            elif hasattr(children, 'get_object'):
                                analyze_struct_element(children.get_object(), level + 1)

                # Analyze root structure
                if '/K' in struct_root:
                    root_kids = struct_root['/K']
                    if isinstance(root_kids, list):
                        print(f"Root has {len(root_kids)} children:")
                        for i, kid in enumerate(root_kids[:3]):  # First 3 root elements
                            print(f"\nRoot child {i+1}:")
                            if hasattr(kid, 'get_object'):
                                analyze_struct_element(kid.get_object(), 1)

    except Exception as e:
        print(f"Tag tree analysis error: {e}")

    # Check for common accessibility issues
    print(f"\n5. ACCESSIBILITY CHECK:")
    try:
        reader = PdfReader(pdf_path)

        issues = []

        # Check for title
        if '/Info' in reader.trailer:
            info = reader.trailer['/Info']
            if '/Title' not in info or not info['/Title']:
                issues.append("Missing document title")
        else:
            issues.append("No document info dictionary")

        # Check for language
        if '/Lang' not in reader.trailer['/Root']:
            issues.append("Missing document language")

        # Check for PDF/UA identifier
        if '/Metadata' in reader.trailer['/Root']:
            print("   Has XMP metadata (good for PDF/UA)")
        else:
            issues.append("No XMP metadata")

        # Report issues
        if issues:
            print("   Issues found:")
            for issue in issues:
                print(f"   - {issue}")
        else:
            print("   No basic accessibility issues found")

    except Exception as e:
        print(f"Accessibility check error: {e}")

if __name__ == "__main__":
    pdf_path = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/Wisconsin/Green Bay/Remote Work & Alt. Schedule Request Form (Fillable Ver.)_202510021602086175-messingabout2.pdf"

    if Path(pdf_path).exists():
        analyze_pdf_structure(pdf_path)
    else:
        print(f"File not found: {pdf_path}")