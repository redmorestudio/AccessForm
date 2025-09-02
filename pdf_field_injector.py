#!/usr/bin/env python3
"""
Re-injects form fields into a PDF after PDF/A conversion.
Uses saved metadata to recreate fields with correct names.
"""
import sys
import json
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (
    NameObject, TextStringObject, DictionaryObject, 
    ArrayObject, NumberObject, BooleanObject, IndirectObject
)
import tempfile

def inject_fields(pdf_path, field_metadata_json):
    """
    Add form fields back to a PDF using saved metadata.
    
    Args:
        pdf_path: Path to PDF/A converted PDF
        field_metadata_json: JSON string containing field metadata
    """
    try:
        reader = PdfReader(pdf_path)
        writer = PdfWriter()
        
        # Parse field metadata
        field_metadata = json.loads(field_metadata_json)
        
        # Copy all pages
        for page in reader.pages:
            writer.add_page(page)
        
        # Create AcroForm if it doesn't exist
        if "/AcroForm" not in writer._root_object:
            writer._root_object.update({
                NameObject("/AcroForm"): DictionaryObject({
                    NameObject("/Fields"): ArrayObject()
                })
            })
        
        acroform = writer._root_object["/AcroForm"]
        if "/Fields" not in acroform:
            acroform[NameObject("/Fields")] = ArrayObject()
        
        fields_array = acroform["/Fields"]
        
        # Recreate each field from metadata
        for field_info in field_metadata:
            # Create field dictionary
            field_dict = DictionaryObject()
            
            # Set field name (most important!)
            field_dict.update({
                NameObject("/T"): TextStringObject(field_info["name"])
            })
            
            # Set field type
            if field_info.get("type"):
                type_map = {
                    "/Tx": "/Tx",  # Text
                    "/Btn": "/Btn",  # Button (checkbox, radio)
                    "/Ch": "/Ch",  # Choice (dropdown, list)
                    "/Sig": "/Sig"  # Signature
                }
                ft = field_info["type"]
                if ft in type_map:
                    field_dict.update({
                        NameObject("/FT"): NameObject(type_map[ft])
                    })
            
            # Set tooltip if present
            if field_info.get("tooltip"):
                field_dict.update({
                    NameObject("/TU"): TextStringObject(field_info["tooltip"])
                })
            
            # Set field flags
            if field_info.get("flags"):
                field_dict.update({
                    NameObject("/Ff"): NumberObject(field_info["flags"])
                })
            
            # Create widget annotation if we have position info
            if "rect" in field_info and "page" in field_info:
                page_num = field_info["page"]
                if 0 <= page_num < len(writer.pages):
                    page = writer.pages[page_num]
                    
                    # Create widget annotation
                    widget = DictionaryObject()
                    widget.update({
                        NameObject("/Type"): NameObject("/Annot"),
                        NameObject("/Subtype"): NameObject("/Widget"),
                        NameObject("/Rect"): ArrayObject([
                            NumberObject(field_info["rect"][0]),
                            NumberObject(field_info["rect"][1]),
                            NumberObject(field_info["rect"][2]),
                            NumberObject(field_info["rect"][3])
                        ]),
                        NameObject("/P"): page.indirect_reference,
                        NameObject("/T"): TextStringObject(field_info["name"]),
                        NameObject("/Parent"): field_dict  # Link to field
                    })
                    
                    # Add appearance streams for visibility
                    widget.update({
                        NameObject("/AP"): DictionaryObject({
                            NameObject("/N"): DictionaryObject()  # Normal appearance
                        })
                    })
                    
                    # Add widget to page annotations
                    if "/Annots" not in page:
                        page[NameObject("/Annots")] = ArrayObject()
                    
                    page["/Annots"].append(writer._add_object(widget))
                    
                    # Link field to widget
                    field_dict.update({
                        NameObject("/Kids"): ArrayObject([writer._add_object(widget)])
                    })
            
            # Add field to form
            fields_array.append(writer._add_object(field_dict))
        
        # Save PDF with re-injected fields
        output_path = tempfile.mktemp(suffix='_with_fields.pdf')
        with open(output_path, 'wb') as output_file:
            writer.write(output_file)
        
        return {
            "success": True,
            "output_path": output_path,
            "fields_added": len(field_metadata)
        }
        
    except Exception as e:
        return {
            "success": False,
            "error": str(e)
        }

def main():
    if len(sys.argv) != 3:
        print(json.dumps({
            "success": False,
            "error": "Usage: pdf_field_injector.py <pdf_path> <field_metadata_json>"
        }))
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    field_metadata_json = sys.argv[2]
    
    result = inject_fields(pdf_path, field_metadata_json)
    print(json.dumps(result))

if __name__ == "__main__":
    main()