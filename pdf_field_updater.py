#!/usr/bin/env python3
"""
Updates PDF form field names and properties using pypdf.
This bypasses the issues with PassportPDF reverting field changes.
"""
import sys
import json
from pypdf import PdfReader, PdfWriter
from pypdf.generic import NameObject, TextStringObject, ArrayObject
import tempfile
import os

def update_pdf_fields(pdf_path, field_updates):
    """
    Update PDF form field names and properties.
    
    Args:
        pdf_path: Path to the input PDF
        field_updates: List of dicts with 'originalName', 'newName', 'fieldType' keys
    
    Returns:
        Path to the updated PDF file
    """
    try:
        reader = PdfReader(pdf_path)
        writer = PdfWriter()
        
        # Copy all pages
        for page in reader.pages:
            writer.add_page(page)
        
        # Copy the AcroForm if it exists
        if "/AcroForm" in reader.trailer["/Root"]:
            writer._root_object.update({
                NameObject("/AcroForm"): reader.trailer["/Root"]["/AcroForm"]
            })
        
        # Get the form fields
        if writer._root_object.get("/AcroForm"):
            acroform = writer._root_object["/AcroForm"]
            
            if "/Fields" in acroform:
                fields = acroform["/Fields"]
                
                # Create a mapping of field updates
                update_map = {}
                for update in field_updates:
                    # Handle fields with type suffixes
                    original = update['originalName']
                    update_map[original] = update
                    # Also check for fields with type suffixes
                    update_map[f"{original}[{update.get('fieldType', '')}]"] = update
                
                # Process each field
                for field_ref in fields:
                    field = field_ref.get_object()
                    
                    # Get current field name
                    if "/T" in field:
                        current_name = str(field["/T"])
                        
                        # Check if this field needs updating
                        if current_name in update_map:
                            update = update_map[current_name]
                            new_name = update['newName']
                            
                            # Update the field name
                            field.update({
                                NameObject("/T"): TextStringObject(new_name)
                            })
                            
                            # Update the tooltip if provided
                            if 'tooltip' in update:
                                field.update({
                                    NameObject("/TU"): TextStringObject(update['tooltip'])
                                })
                            
                            print(f"Updated field '{current_name}' to '{new_name}'")
                    
                    # Also check annotations on each page for field widgets
                    for page_num, page in enumerate(writer.pages):
                        if "/Annots" in page:
                            for annot_ref in page["/Annots"]:
                                annot = annot_ref.get_object()
                                
                                # Check if this is a widget annotation
                                if annot.get("/Subtype") == "/Widget" and "/T" in annot:
                                    current_name = str(annot["/T"])
                                    
                                    if current_name in update_map:
                                        update = update_map[current_name]
                                        new_name = update['newName']
                                        
                                        # Update the annotation's field name
                                        annot.update({
                                            NameObject("/T"): TextStringObject(new_name)
                                        })
                                        
                                        # Update tooltip
                                        if 'tooltip' in update:
                                            annot.update({
                                                NameObject("/TU"): TextStringObject(update['tooltip'])
                                            })
        
        # Save to temporary file
        output_path = tempfile.mktemp(suffix='.pdf')
        with open(output_path, 'wb') as output_file:
            writer.write(output_file)
        
        return output_path
        
    except Exception as e:
        print(f"Error updating PDF fields: {e}", file=sys.stderr)
        raise

def main():
    if len(sys.argv) != 3:
        print("Usage: pdf_field_updater.py <pdf_path> <json_updates>")
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    json_updates = sys.argv[2]
    
    try:
        # Parse the field updates
        field_updates = json.loads(json_updates)
        
        # Update the PDF
        output_path = update_pdf_fields(pdf_path, field_updates)
        
        # Return the path to the updated PDF
        print(json.dumps({
            "success": True,
            "output_path": output_path
        }))
        
    except Exception as e:
        print(json.dumps({
            "success": False,
            "error": str(e)
        }))
        sys.exit(1)

if __name__ == "__main__":
    main()