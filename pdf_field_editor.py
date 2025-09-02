#!/usr/bin/env python3
"""
Complete PDF field editor that preserves field names without type suffixes.
This completely bypasses Syncfusion's field handling which adds unwanted suffixes.
"""
import sys
import json
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (
    NameObject, 
    TextStringObject, 
    DictionaryObject,
    ArrayObject,
    IndirectObject,
    NumberObject,
    BooleanObject
)
import tempfile
import os

def strip_type_suffix(field_name):
    """Remove common type suffixes from field names"""
    suffixes = ["[name]", "[text]", "[date]", "[checkbox]", "[textarea]", "[signature]", "[radio]", "[email]", "[phone]", "[number]"]
    for suffix in suffixes:
        if field_name.endswith(suffix):
            return field_name[:-len(suffix)]
    return field_name

def clean_field_names_deep(pdf_path, output_path=None):
    """
    Deep clean all field names in a PDF, removing type suffixes at every level.
    This function modifies fields in:
    1. The AcroForm fields array
    2. Page annotations
    3. Field kids (nested fields)
    """
    try:
        reader = PdfReader(pdf_path)
        writer = PdfWriter()
        
        # Track all field modifications for reporting
        modified_fields = []
        
        # Copy all pages first
        for page in reader.pages:
            writer.add_page(page)
        
        # Copy metadata
        if reader.metadata:
            writer.add_metadata(reader.metadata)
        
        # Process AcroForm if it exists
        if "/AcroForm" in reader.trailer["/Root"]:
            acroform = reader.trailer["/Root"]["/AcroForm"]
            
            # Create a new AcroForm object
            new_acroform = DictionaryObject()
            
            # Copy all AcroForm properties
            for key, value in acroform.items():
                if key == "/Fields":
                    # Process fields specially
                    new_fields = ArrayObject()
                    
                    for field_ref in value:
                        field = field_ref.get_object()
                        new_field = process_field_recursive(field, modified_fields)
                        # Add the processed field to writer and get reference
                        field_obj = writer._add_object(new_field)
                        new_fields.append(field_obj)
                    
                    new_acroform[NameObject("/Fields")] = new_fields
                else:
                    # Copy other properties as-is
                    new_acroform[key] = value
            
            # Set the new AcroForm
            writer._root_object[NameObject("/AcroForm")] = writer._add_object(new_acroform)
        
        # Also process annotations on each page
        for page_idx, page in enumerate(writer.pages):
            if "/Annots" in page:
                new_annots = ArrayObject()
                
                for annot_ref in page["/Annots"]:
                    annot = annot_ref.get_object()
                    new_annot = process_annotation(annot, modified_fields)
                    
                    # Add to writer and get reference
                    annot_obj = writer._add_object(new_annot)
                    new_annots.append(annot_obj)
                
                # Update the page's annotations
                page[NameObject("/Annots")] = new_annots
        
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

def process_field_recursive(field, modified_fields):
    """Process a field and all its children recursively"""
    new_field = DictionaryObject()
    
    # Copy all field properties
    for key, value in field.items():
        if key == "/T":  # Field name
            original_name = str(value)
            clean_name = strip_type_suffix(original_name)
            
            if original_name != clean_name:
                modified_fields.append({
                    "original": original_name,
                    "cleaned": clean_name
                })
            
            new_field[NameObject("/T")] = TextStringObject(clean_name)
            
        elif key == "/TU":  # Tooltip
            # Also clean tooltip if it matches field name pattern
            original_tooltip = str(value)
            clean_tooltip = strip_type_suffix(original_tooltip)
            new_field[NameObject("/TU")] = TextStringObject(clean_tooltip)
            
        elif key == "/Kids":  # Child fields
            new_kids = ArrayObject()
            for kid_ref in value:
                kid = kid_ref.get_object()
                new_kid = process_field_recursive(kid, modified_fields)
                new_kids.append(new_kid)
            new_field[NameObject("/Kids")] = new_kids
            
        else:
            # Copy other properties as-is
            new_field[key] = value
    
    return new_field

def process_annotation(annot, modified_fields):
    """Process an annotation (widget)"""
    new_annot = DictionaryObject()
    
    for key, value in annot.items():
        if key == "/T":  # Field name in annotation
            original_name = str(value)
            clean_name = strip_type_suffix(original_name)
            
            if original_name != clean_name:
                # Track if not already tracked
                if not any(m["original"] == original_name for m in modified_fields):
                    modified_fields.append({
                        "original": original_name,
                        "cleaned": clean_name
                    })
            
            new_annot[NameObject("/T")] = TextStringObject(clean_name)
            
        elif key == "/TU":  # Tooltip in annotation
            original_tooltip = str(value)
            clean_tooltip = strip_type_suffix(original_tooltip)
            new_annot[NameObject("/TU")] = TextStringObject(clean_tooltip)
            
        else:
            new_annot[key] = value
    
    return new_annot

def add_accessibility_metadata(pdf_path, output_path=None):
    """Add accessibility metadata to a PDF without modifying fields"""
    try:
        reader = PdfReader(pdf_path)
        writer = PdfWriter()
        
        # Copy all pages
        for page in reader.pages:
            writer.add_page(page)
        
        # Copy and enhance metadata
        metadata = reader.metadata if reader.metadata else {}
        
        # Add accessibility markers
        keywords = metadata.get('/Keywords', '')
        if 'PDF/UA' not in keywords:
            keywords += ' PDF/UA WCAG2.1 Section508 Accessible'
        
        subject = metadata.get('/Subject', '')
        if '[Field-Edited' not in subject:
            subject += ' [Field-Edited, Accessibility Enhanced]'
        
        # Ensure language is set
        language = metadata.get('/Language', 'en-US')
        
        # Update metadata
        writer.add_metadata({
            '/Keywords': keywords.strip(),
            '/Subject': subject.strip(),
            '/Language': language,
            '/Creator': 'AccessForm with Field Preservation'
        })
        
        # Copy AcroForm if exists (without modifying fields)
        if "/AcroForm" in reader.trailer["/Root"]:
            writer._root_object[NameObject("/AcroForm")] = reader.trailer["/Root"]["/AcroForm"]
        
        # Save
        if output_path is None:
            output_path = tempfile.mktemp(suffix='.pdf')
        
        with open(output_path, 'wb') as output_file:
            writer.write(output_file)
        
        return output_path
        
    except Exception as e:
        print(f"Error adding metadata: {e}", file=sys.stderr)
        raise

def main():
    if len(sys.argv) < 2:
        print("Usage: pdf_field_editor.py <pdf_path> [--add-metadata-only]")
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    metadata_only = len(sys.argv) > 2 and sys.argv[2] == "--add-metadata-only"
    
    try:
        if metadata_only:
            # Just add metadata without touching fields
            output_path = add_accessibility_metadata(pdf_path)
            print(json.dumps({
                "success": True,
                "output_path": output_path,
                "metadata_added": True
            }))
        else:
            # Clean field names
            result = clean_field_names_deep(pdf_path)
            
            if result["success"]:
                # Also add metadata to the cleaned PDF
                final_path = add_accessibility_metadata(result["output_path"])
                
                # Clean up temp file
                if result["output_path"] != final_path:
                    try:
                        os.remove(result["output_path"])
                    except:
                        pass
                
                result["output_path"] = final_path
                result["metadata_added"] = True
            
            print(json.dumps(result))
        
    except Exception as e:
        print(json.dumps({
            "success": False,
            "error": str(e)
        }))
        sys.exit(1)

if __name__ == "__main__":
    main()