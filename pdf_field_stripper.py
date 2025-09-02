#!/usr/bin/env python3
"""
Strips all form fields from a PDF and saves field metadata for re-injection.
This prevents PassportPDF from reverting field names during PDF/A conversion.
"""
import sys
import json
from pypdf import PdfReader, PdfWriter
from pypdf.generic import NameObject, DictionaryObject, ArrayObject
import tempfile

def strip_fields_and_save_metadata(pdf_path):
    """
    Remove all form fields from PDF and save their metadata.
    
    Returns:
        - Path to stripped PDF
        - JSON metadata of all fields
    """
    try:
        reader = PdfReader(pdf_path)
        writer = PdfWriter()
        
        # Store field metadata before removing
        field_metadata = []
        
        # Check if PDF has form fields
        if "/AcroForm" in reader.trailer["/Root"]:
            acroform = reader.trailer["/Root"]["/AcroForm"]
            
            if "/Fields" in acroform:
                fields = acroform["/Fields"]
                
                # Extract metadata from each field
                for field_ref in fields:
                    field = field_ref.get_object()
                    
                    field_info = {
                        "name": str(field.get("/T", "")),
                        "type": str(field.get("/FT", "")),
                        "tooltip": str(field.get("/TU", "")),
                        "value": str(field.get("/V", "")),
                        "default_value": str(field.get("/DV", "")),
                        "flags": int(field.get("/Ff", 0)),
                    }
                    
                    # Get field position from widget annotation
                    if "/Kids" in field:
                        # Field with multiple widgets
                        for kid_ref in field["/Kids"]:
                            kid = kid_ref.get_object()
                            if "/Rect" in kid:
                                rect = kid["/Rect"]
                                field_info["rect"] = [float(rect[0]), float(rect[1]), float(rect[2]), float(rect[3])]
                                field_info["page"] = 0  # Will need to determine actual page
                                break
                    elif "/Rect" in field:
                        # Direct widget annotation
                        rect = field["/Rect"]
                        field_info["rect"] = [float(rect[0]), float(rect[1]), float(rect[2]), float(rect[3])]
                        field_info["page"] = 0  # Will need to determine actual page
                    
                    # Find which page this field belongs to
                    for page_num, page in enumerate(reader.pages):
                        if "/Annots" in page:
                            for annot_ref in page["/Annots"]:
                                annot = annot_ref.get_object()
                                if annot.get("/T") == field.get("/T"):
                                    field_info["page"] = page_num
                                    break
                    
                    field_metadata.append(field_info)
        
        # Copy all pages without form fields
        for page in reader.pages:
            # Remove annotations that are form widgets
            if "/Annots" in page:
                new_annots = ArrayObject()
                for annot_ref in page["/Annots"]:
                    annot = annot_ref.get_object()
                    # Keep non-widget annotations
                    if annot.get("/Subtype") != "/Widget":
                        new_annots.append(annot_ref)
                page[NameObject("/Annots")] = new_annots
            
            writer.add_page(page)
        
        # Don't copy the AcroForm (removes all form fields)
        # Copy other document properties
        if reader.metadata:
            writer.add_metadata(reader.metadata)
        
        # Save stripped PDF
        output_path = tempfile.mktemp(suffix='_stripped.pdf')
        with open(output_path, 'wb') as output_file:
            writer.write(output_file)
        
        return {
            "success": True,
            "stripped_pdf_path": output_path,
            "field_metadata": field_metadata,
            "field_count": len(field_metadata)
        }
        
    except Exception as e:
        return {
            "success": False,
            "error": str(e)
        }

def main():
    if len(sys.argv) != 2:
        print(json.dumps({
            "success": False,
            "error": "Usage: pdf_field_stripper.py <pdf_path>"
        }))
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    result = strip_fields_and_save_metadata(pdf_path)
    print(json.dumps(result))

if __name__ == "__main__":
    main()