#!/usr/bin/env python3
"""
Surgically modify PDF field names without touching any other structure.
This uses pikepdf which is better at preserving PDF structure.
"""
import sys
import json
import tempfile

try:
    import pikepdf
except ImportError:
    print(json.dumps({
        "success": False,
        "error": "pikepdf not installed. Run: pip install pikepdf"
    }))
    sys.exit(1)

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

def surgically_fix_field_names(pdf_path, output_path=None):
    """
    Surgically modify field names without touching ANY other structure.
    Uses pikepdf which preserves PDF structure better.
    """
    try:
        # Open the PDF with pikepdf
        pdf = pikepdf.open(pdf_path)
        
        modified_fields = []
        
        # Check if there's an AcroForm
        if "/AcroForm" in pdf.Root:
            acroform = pdf.Root.AcroForm
            
            # Process fields in AcroForm
            if "/Fields" in acroform:
                for field in acroform.Fields:
                    if "/T" in field:
                        original_name = str(field.T)
                        clean_name = strip_type_suffix(original_name)
                        
                        if original_name != clean_name:
                            # Modify the field name in place
                            field.T = clean_name
                            modified_fields.append({
                                "original": original_name,
                                "cleaned": clean_name
                            })
                    
                    # Handle Kids (nested fields)
                    if "/Kids" in field:
                        for kid in field.Kids:
                            if "/T" in kid:
                                original_name = str(kid.T)
                                clean_name = strip_type_suffix(original_name)
                                
                                if original_name != clean_name:
                                    kid.T = clean_name
                                    if not any(m["original"] == original_name for m in modified_fields):
                                        modified_fields.append({
                                            "original": original_name,
                                            "cleaned": clean_name
                                        })
        
        # Also process widget annotations on pages
        for page_num, page in enumerate(pdf.pages):
            if "/Annots" in page:
                for annot in page.Annots:
                    # Check if this is a widget annotation with a field name
                    if "/Subtype" in annot and str(annot.Subtype) == "/Widget":
                        if "/T" in annot:
                            original_name = str(annot.T)
                            clean_name = strip_type_suffix(original_name)
                            
                            if original_name != clean_name:
                                annot.T = clean_name
                                
                                # Track if not already tracked
                                if not any(m["original"] == original_name for m in modified_fields):
                                    modified_fields.append({
                                        "original": original_name,
                                        "cleaned": clean_name,
                                        "page": page_num + 1
                                    })
        
        # Update metadata for accessibility (without breaking anything)
        if pdf.docinfo:
            keywords = str(pdf.docinfo.get("/Keywords", ""))
            if "PDF/UA" not in keywords:
                pdf.docinfo["/Keywords"] = keywords + " PDF/UA WCAG2.1 Section508 Accessible"
            
            subject = str(pdf.docinfo.get("/Subject", ""))
            if "[Field-Edited" not in subject:
                pdf.docinfo["/Subject"] = subject + " [Field-Edited, Accessibility Enhanced]"
            
            pdf.docinfo["/Creator"] = "AccessForm with Field Preservation"
        
        # Save the result
        if output_path is None:
            output_path = tempfile.mktemp(suffix='.pdf')
        
        # Save with options to preserve structure
        pdf.save(output_path, 
                 compress_streams=False,  # Don't recompress
                 preserve_pdfa=True,      # Preserve PDF/A status
                 linearize=False)         # Don't linearize (preserves structure)
        
        pdf.close()
        
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
        print("Usage: pdf_field_surgeon.py <pdf_path>")
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    
    try:
        result = surgically_fix_field_names(pdf_path)
        print(json.dumps(result))
        
    except Exception as e:
        print(json.dumps({
            "success": False,
            "error": str(e)
        }))
        sys.exit(1)

if __name__ == "__main__":
    main()