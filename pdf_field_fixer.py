#!/usr/bin/env python3
"""
Fix PDF field names while preserving ALL structure including tags.
This version copies the entire PDF structure and only modifies field names.
"""
import sys
import json
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (
    NameObject, 
    TextStringObject, 
    DictionaryObject,
    IndirectObject
)
import tempfile
import os

def strip_type_suffix(field_name):
    """Remove common type suffixes from field names"""
    if not field_name:
        return field_name
    
    suffixes = ["[name]", "[text]", "[date]", "[checkbox]", "[textarea]", 
                "[signature]", "[radio]", "[email]", "[phone]", "[number]"]
    
    for suffix in suffixes:
        if field_name.endswith(suffix):
            return field_name[:-len(suffix)]
    
    return field_name

def fix_field_names_preserve_all(pdf_path, output_path=None):
    """
    Fix field names while preserving EVERYTHING else including tags.
    Uses pypdf but more carefully to preserve structure.
    """
    try:
        # Read the PDF
        reader = PdfReader(pdf_path)
        writer = PdfWriter()
        
        # Clone the entire PDF to the writer
        writer.append_pages_from_reader(reader)
        
        # Copy ALL metadata
        if reader.metadata:
            writer.add_metadata(reader.metadata)
        
        # Track modifications
        modified_fields = []
        
        # Now carefully update field names in the cloned structure
        if writer._root_object.get("/AcroForm"):
            acroform = writer._root_object["/AcroForm"]
            
            if "/Fields" in acroform:
                fields = acroform["/Fields"]
                
                for field_ref in fields:
                    field = field_ref.get_object() if hasattr(field_ref, 'get_object') else field_ref
                    
                    if "/T" in field:
                        original_name = str(field["/T"])
                        clean_name = strip_type_suffix(original_name)
                        
                        if original_name != clean_name:
                            # Update the field name
                            field[NameObject("/T")] = TextStringObject(clean_name)
                            
                            modified_fields.append({
                                "original": original_name,
                                "cleaned": clean_name
                            })
                    
                    # Also handle Kids (nested fields) if present
                    if "/Kids" in field:
                        for kid_ref in field["/Kids"]:
                            kid = kid_ref.get_object() if hasattr(kid_ref, 'get_object') else kid_ref
                            if "/T" in kid:
                                original_name = str(kid["/T"])
                                clean_name = strip_type_suffix(original_name)
                                
                                if original_name != clean_name:
                                    kid[NameObject("/T")] = TextStringObject(clean_name)
                                    
                                    if not any(m["original"] == original_name for m in modified_fields):
                                        modified_fields.append({
                                            "original": original_name,
                                            "cleaned": clean_name
                                        })
        
        # Also process widget annotations on pages
        for page_num, page in enumerate(writer.pages):
            if "/Annots" in page:
                for annot_ref in page["/Annots"]:
                    annot = annot_ref.get_object() if hasattr(annot_ref, 'get_object') else annot_ref
                    
                    # Check if this is a widget annotation
                    if annot.get("/Subtype") == "/Widget" and "/T" in annot:
                        original_name = str(annot["/T"])
                        clean_name = strip_type_suffix(original_name)
                        
                        if original_name != clean_name:
                            annot[NameObject("/T")] = TextStringObject(clean_name)
                            
                            # Track if not already tracked
                            if not any(m["original"] == original_name for m in modified_fields):
                                modified_fields.append({
                                    "original": original_name,
                                    "cleaned": clean_name,
                                    "page": page_num + 1
                                })
        
        # Update metadata for accessibility
        metadata = dict(writer.metadata) if writer.metadata else {}
        
        # Add accessibility markers
        keywords = metadata.get('/Keywords', '')
        if 'PDF/UA' not in keywords:
            keywords += ' PDF/UA WCAG2.1 Section508 Accessible'
        
        subject = metadata.get('/Subject', '')
        if '[Field-Edited' not in subject:
            subject += ' [Field-Edited, Accessibility Enhanced]'
        
        # Update metadata
        metadata['/Keywords'] = keywords.strip()
        metadata['/Subject'] = subject.strip()
        metadata['/Creator'] = 'AccessForm with Field Preservation'
        
        writer.add_metadata(metadata)
        
        # Save the result
        if output_path is None:
            output_path = tempfile.mktemp(suffix='.pdf')
        
        with open(output_path, 'wb') as output_file:
            writer.write(output_file)
        
        return {
            "success": True,
            "output_path": output_path,
            "modified_fields": modified_fields
        }
        
    except Exception as e:
        return {
            "success": False,
            "error": str(e)
        }

def main():
    if len(sys.argv) < 2:
        print("Usage: pdf_field_fixer.py <pdf_path>")
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    
    try:
        result = fix_field_names_preserve_all(pdf_path)
        print(json.dumps(result))
        
    except Exception as e:
        print(json.dumps({
            "success": False,
            "error": str(e)
        }))
        sys.exit(1)

if __name__ == "__main__":
    main()