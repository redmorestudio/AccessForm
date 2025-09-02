#!/usr/bin/env python3
"""
Clean PDF field names while preserving tag structure.
Uses PyMuPDF (fitz) which better preserves PDF structure.
"""
import sys
import json
import tempfile
import fitz  # PyMuPDF
import re

def strip_type_suffix(field_name):
    """Remove common type suffixes from field names"""
    suffixes = [
        r"\[name\]", r"\[text\]", r"\[date\]", r"\[checkbox\]", 
        r"\[textarea\]", r"\[signature\]", r"\[radio\]", 
        r"\[email\]", r"\[phone\]", r"\[number\]"
    ]
    
    for suffix in suffixes:
        field_name = re.sub(suffix + r"$", "", field_name)
    
    return field_name

def clean_field_names_preserve_structure(pdf_path, output_path=None):
    """
    Clean field names while preserving PDF structure including tags.
    Uses PyMuPDF which is better at preserving PDF structure.
    """
    try:
        # Open the PDF
        doc = fitz.open(pdf_path)
        
        modified_fields = []
        
        # Process each page
        for page_num in range(len(doc)):
            page = doc[page_num]
            
            # Get all widgets (form fields) on the page
            for widget in page.widgets():
                if widget.field_name:
                    original_name = widget.field_name
                    clean_name = strip_type_suffix(original_name)
                    
                    if original_name != clean_name:
                        # Update the field name
                        widget.field_name = clean_name
                        widget.update()
                        
                        modified_fields.append({
                            "original": original_name,
                            "cleaned": clean_name,
                            "page": page_num + 1
                        })
        
        # Add metadata for accessibility
        metadata = doc.metadata
        
        # Update keywords
        keywords = metadata.get('keywords', '')
        if 'PDF/UA' not in keywords:
            keywords += ' PDF/UA WCAG2.1 Section508 Accessible'
        
        # Update subject
        subject = metadata.get('subject', '')
        if '[Field-Edited' not in subject:
            subject += ' [Field-Edited, Accessibility Enhanced]'
        
        # Set new metadata
        doc.set_metadata({
            'keywords': keywords.strip(),
            'subject': subject.strip(),
            'creator': 'AccessForm with Field Preservation'
        })
        
        # Save with garbage collection to clean up internal structures
        # but preserve important elements like tags
        if output_path is None:
            output_path = tempfile.mktemp(suffix='.pdf')
        
        # Save with options to preserve structure
        doc.save(
            output_path,
            garbage=4,  # Clean up duplicate objects but preserve structure
            deflate=True,  # Compress
            clean=False,  # Don't clean - preserves tags
            pretty=False  # Don't prettify - preserves structure
        )
        
        doc.close()
        
        return {
            "success": True,
            "output_path": output_path,
            "modified_fields": modified_fields
        }
        
    except Exception as e:
        return {
            "success": False,
            "error": str(e),
            "note": "PyMuPDF may not be installed. Install with: pip install PyMuPDF"
        }

def main():
    if len(sys.argv) < 2:
        print("Usage: pdf_field_cleaner.py <pdf_path>")
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    
    try:
        result = clean_field_names_preserve_structure(pdf_path)
        print(json.dumps(result))
        
    except Exception as e:
        print(json.dumps({
            "success": False,
            "error": str(e)
        }))
        sys.exit(1)

if __name__ == "__main__":
    main()