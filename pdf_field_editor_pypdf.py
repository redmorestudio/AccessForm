#!/usr/bin/env python3
"""
PDF field editor using pypdf for better form field handling.
This properly updates form fields including names, types, positions, and sizes.
"""
import sys
import json
import tempfile
import os
import logging
from pathlib import Path

try:
    from pypdf import PdfReader, PdfWriter
    from pypdf.generic import (
        NameObject, 
        TextStringObject, 
        NumberObject, 
        ArrayObject,
        DictionaryObject,
        IndirectObject
    )
except ImportError:
    print(json.dumps({
        "success": False,
        "error": "pypdf not installed. Run: pip install pypdf"
    }))
    sys.exit(1)

# Set up logging
logging.basicConfig(level=logging.DEBUG, format='%(levelname)s: %(message)s')

def update_fields_comprehensive(pdf_path, field_updates):
    """
    Update form fields including names, types, positions, and sizes.
    
    Args:
        pdf_path: Path to the input PDF
        field_updates: List of dicts with field update information
        
    Returns:
        Dict with success status and output path
    """
    try:
        # Read the PDF
        reader = PdfReader(pdf_path)
        writer = PdfWriter()
        
        # Copy all pages
        for page in reader.pages:
            writer.add_page(page)
        
        # Track modifications
        modified_fields = []
        
        # Create field update map
        update_map = {}
        for update in field_updates:
            original = update.get('originalName', '')
            if original:
                update_map[original] = update
                # Also handle fields with type suffixes
                field_type = update.get('fieldType', '')
                if field_type:
                    update_map[f"{original}[{field_type}]"] = update
        
        logging.info(f"Processing {len(update_map)} field updates")
        
        # Get form fields
        if '/AcroForm' in reader.trailer['/Root']:
            acroform = reader.trailer['/Root']['/AcroForm']
            
            if '/Fields' in acroform:
                fields = acroform['/Fields']
                
                for field_ref in fields:
                    field = field_ref.get_object()
                    
                    # Get current field name
                    if '/T' in field:
                        current_name = field['/T']
                        if isinstance(current_name, bytes):
                            current_name = current_name.decode('utf-8')
                        elif hasattr(current_name, 'original_bytes'):
                            current_name = current_name.original_bytes.decode('utf-8')
                        else:
                            current_name = str(current_name)
                        
                        logging.debug(f"Found field: {current_name}")
                        
                        if current_name in update_map:
                            update = update_map[current_name]
                            new_name = update.get('newName', current_name)
                            
                            # Update field name
                            if new_name != current_name:
                                field[NameObject('/T')] = TextStringObject(new_name)
                                logging.info(f"Updated field name from '{current_name}' to '{new_name}'")
                            
                            # Update field type if needed
                            new_type = update.get('fieldType', '')
                            if new_type:
                                if new_type == 'checkbox':
                                    field[NameObject('/FT')] = NameObject('/Btn')
                                    field[NameObject('/Ff')] = NumberObject(0)  # Clear flags
                                elif new_type == 'radio':
                                    field[NameObject('/FT')] = NameObject('/Btn')
                                    field[NameObject('/Ff')] = NumberObject(49152)  # Radio button flags
                                elif new_type == 'dropdown' or new_type == 'combobox':
                                    field[NameObject('/FT')] = NameObject('/Ch')
                                    field[NameObject('/Ff')] = NumberObject(131072)  # Combo box flags
                                elif new_type == 'listbox':
                                    field[NameObject('/FT')] = NameObject('/Ch')
                                    field[NameObject('/Ff')] = NumberObject(0)  # List box flags
                                else:  # Default to text
                                    field[NameObject('/FT')] = NameObject('/Tx')
                                    field[NameObject('/Ff')] = NumberObject(0)
                                
                                logging.info(f"Updated field type to '{new_type}'")
                            
                            # Update position and size if provided
                            if all(k in update for k in ['X', 'Y', 'Width', 'Height']):
                                # Create new rectangle [x1, y1, x2, y2]
                                x = update['X']
                                y = update['Y']
                                width = update['Width']
                                height = update['Height']
                                
                                # PDF coordinates are bottom-left origin
                                # We may need to adjust based on page height
                                rect = ArrayObject([
                                    NumberObject(x),
                                    NumberObject(y),
                                    NumberObject(x + width),
                                    NumberObject(y + height)
                                ])
                                
                                # Update the annotation rectangle if it exists
                                if '/Kids' in field:
                                    # Field has widget annotations
                                    for kid_ref in field['/Kids']:
                                        kid = kid_ref.get_object()
                                        kid[NameObject('/Rect')] = rect
                                        logging.info(f"Updated widget position to ({x}, {y}) with size ({width}x{height})")
                                elif '/Rect' in field:
                                    # Direct annotation
                                    field[NameObject('/Rect')] = rect
                                    logging.info(f"Updated field position to ({x}, {y}) with size ({width}x{height})")
                            
                            # Update tooltip if provided
                            if 'tooltip' in update and update['tooltip']:
                                field[NameObject('/TU')] = TextStringObject(update['tooltip'])
                                logging.info(f"Updated tooltip to '{update['tooltip']}'")
                            
                            # Mark as required if specified
                            if 'isRequired' in update and update['isRequired']:
                                if '/Ff' in field:
                                    flags = field['/Ff']
                                    if isinstance(flags, NumberObject):
                                        field[NameObject('/Ff')] = NumberObject(flags | 2)  # Set required flag
                                else:
                                    field[NameObject('/Ff')] = NumberObject(2)
                                logging.info("Set field as required")
                            
                            modified_fields.append({
                                'original': current_name,
                                'new': new_name,
                                'type': new_type if new_type else 'unchanged'
                            })
        
        # Save the modified PDF
        output_path = tempfile.mktemp(suffix='.pdf')
        
        with open(output_path, 'wb') as output_file:
            writer.write(output_file)
        
        logging.info(f"Successfully saved modified PDF to {output_path}")
        
        return {
            "success": True,
            "output_path": output_path,
            "modified_fields": modified_fields,
            "message": f"Successfully updated {len(modified_fields)} fields"
        }
        
    except Exception as e:
        logging.error(f"Error processing PDF: {e}")
        import traceback
        traceback.print_exc()
        return {
            "success": False,
            "error": str(e)
        }

def main():
    """Main entry point"""
    if len(sys.argv) < 3:
        print(json.dumps({
            "success": False,
            "error": "Usage: pdf_field_editor_pypdf.py <pdf_path> <field_updates_json_or_file>"
        }))
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    field_updates_arg = sys.argv[2]
    
    if not os.path.exists(pdf_path):
        print(json.dumps({
            "success": False,
            "error": f"PDF file not found: {pdf_path}"
        }))
        sys.exit(1)
    
    # Check if the second argument is a file path or JSON string
    if os.path.exists(field_updates_arg):
        # It's a file path, read the JSON from file
        try:
            with open(field_updates_arg, 'r') as f:
                field_updates = json.load(f)
        except (json.JSONDecodeError, IOError) as e:
            print(json.dumps({
                "success": False,
                "error": f"Failed to read JSON from file: {e}"
            }))
            sys.exit(1)
    else:
        # It's inline JSON
        try:
            field_updates = json.loads(field_updates_arg)
        except json.JSONDecodeError as e:
            print(json.dumps({
                "success": False,
                "error": f"Invalid JSON: {e}"
            }))
            sys.exit(1)
    
    result = update_fields_comprehensive(pdf_path, field_updates)
    print(json.dumps(result))
    
    sys.exit(0 if result["success"] else 1)

if __name__ == "__main__":
    main()