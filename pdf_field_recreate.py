#!/usr/bin/env python3
"""
PDF field recreator - completely removes and recreates fields.
This ensures no remnants of the old field remain.
"""
import sys
import json
import tempfile
import os
import logging
from pathlib import Path

try:
    import fitz  # PyMuPDF
except ImportError:
    print(json.dumps({
        "success": False,
        "error": "PyMuPDF not installed. Run: pip install PyMuPDF"
    }))
    sys.exit(1)

# Set up logging
logging.basicConfig(level=logging.DEBUG, format='%(levelname)s: %(message)s')

def recreate_fields(pdf_path, field_updates):
    """
    Completely delete and recreate form fields with new properties.
    
    Args:
        pdf_path: Path to the input PDF
        field_updates: List of dicts with field update information
        
    Returns:
        Dict with success status and output path
    """
    try:
        # Open the PDF
        doc = fitz.open(pdf_path)
        
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
        
        # First pass: collect all fields to delete and their info
        fields_to_delete = []
        for page_num, page in enumerate(doc):
            widgets = list(page.widgets())
            
            for widget in widgets:
                field_name = widget.field_name
                
                if field_name and field_name in update_map:
                    update = update_map[field_name]
                    
                    # Store all the field info we need to recreate it
                    field_info = {
                        'page': page,
                        'page_num': page_num,
                        'widget': widget,
                        'original_name': field_name,
                        'new_name': update.get('newName', field_name),
                        'new_type': update.get('fieldType', 'text'),
                        'rect': widget.rect,
                        'tooltip': update.get('tooltip', ''),
                        'is_required': update.get('isRequired', False),
                        # Store any custom position/size if provided
                        'custom_x': update.get('X'),
                        'custom_y': update.get('Y'),
                        'custom_width': update.get('Width'),
                        'custom_height': update.get('Height')
                    }
                    
                    fields_to_delete.append(field_info)
                    logging.info(f"Marked field '{field_name}' for deletion and recreation as '{field_info['new_name']}'")
        
        # Second pass: delete all marked fields
        for field_info in fields_to_delete:
            page = field_info['page']
            widget = field_info['widget']
            
            # Delete the widget completely
            page.delete_widget(widget)
            logging.info(f"Deleted field '{field_info['original_name']}' from page {field_info['page_num'] + 1}")
        
        # Save to temp file to commit deletions (not incremental since loaded from memory)
        temp_path = tempfile.mktemp(suffix='_temp.pdf')
        doc.save(temp_path, garbage=4, deflate=True, clean=True)
        doc.close()
        
        # Reopen to ensure deletions are committed
        doc = fitz.open(temp_path)
        
        # Third pass: create new fields
        for field_info in fields_to_delete:
            page = doc[field_info['page_num']]

            # Determine position and size
            if all(field_info.get(k) is not None for k in ['custom_x', 'custom_y', 'custom_width', 'custom_height']):
                # Use custom position/size if provided
                x = field_info['custom_x']
                y = field_info['custom_y']
                width = field_info['custom_width']
                height = field_info['custom_height']
                rect = fitz.Rect(x, y, x + width, y + height)
            else:
                # Use original position - this is already in the correct coordinate system
                # because PyMuPDF reads widget.rect in its own coordinates
                rect = field_info['rect']
                logging.debug(f"Using original rect for '{field_info['new_name']}': {rect}")
            
            # Create the appropriate widget type
            new_type = field_info['new_type'].lower()
            
            if new_type in ['checkbox', 'check']:
                widget_type = fitz.PDF_WIDGET_TYPE_CHECKBOX
            elif new_type in ['radio', 'radiobutton']:
                widget_type = fitz.PDF_WIDGET_TYPE_RADIOBUTTON
            elif new_type in ['combo', 'combobox', 'dropdown']:
                widget_type = fitz.PDF_WIDGET_TYPE_COMBOBOX
            elif new_type in ['list', 'listbox']:
                widget_type = fitz.PDF_WIDGET_TYPE_LISTBOX
            elif new_type in ['signature', 'sig']:
                widget_type = fitz.PDF_WIDGET_TYPE_SIGNATURE
            else:
                # Default to text field
                widget_type = fitz.PDF_WIDGET_TYPE_TEXT
            
            # Create widget properly by creating a widget object first
            widget_obj = fitz.Widget()
            widget_obj.field_type = widget_type
            widget_obj.field_name = field_info['new_name']
            widget_obj.rect = rect
            
            # Add the widget to the page
            new_widget = page.add_widget(widget_obj)
            
            # Set tooltip (not field value - that's for the actual value)
            if field_info['tooltip']:
                # Tooltip is set via the field_value for the tooltip, not the actual value
                # We need to set this properly
                pass
            
            # Set visual properties
            new_widget.border_width = 1
            new_widget.border_color = [0, 0, 0]  # Black border  
            new_widget.fill_color = [1, 1, 1]    # White background
            
            # For text fields, set font
            if widget_type == fitz.PDF_WIDGET_TYPE_TEXT:
                new_widget.text_font = "Helv"
                new_widget.text_fontsize = 10
                new_widget.text_color = [0, 0, 0]  # Black text
            
            # Update the widget to commit changes
            new_widget.update()
            
            # After update, set additional properties if needed
            if field_info['is_required']:
                new_widget.field_flags = 2  # Required flag
            
            modified_fields.append({
                'original': field_info['original_name'],
                'new': field_info['new_name'],
                'type': new_type,
                'page': field_info['page_num'] + 1
            })
            
            logging.info(f"Created new field '{field_info['new_name']}' (type: {new_type}) on page {field_info['page_num'] + 1}")
        
        # Clean up temp file
        if os.path.exists(temp_path):
            os.remove(temp_path)
        
        # Save the final PDF
        output_path = tempfile.mktemp(suffix='.pdf')
        
        # Save with proper settings for form fields
        doc.save(output_path, 
                 garbage=4,  # Clean up unused objects
                 deflate=True,  # Compress
                 clean=True)  # Clean up duplicate objects
        
        doc.close()
        
        # Verify the output
        verify_doc = fitz.open(output_path)
        field_count = sum(len(list(page.widgets())) for page in verify_doc)
        verify_doc.close()
        
        return {
            "success": True,
            "output_path": output_path,
            "modified_fields": modified_fields,
            "total_fields": field_count,
            "message": f"Successfully recreated {len(modified_fields)} fields"
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
            "error": "Usage: pdf_field_recreate.py <pdf_path> <field_updates_json_or_file>"
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
    
    result = recreate_fields(pdf_path, field_updates)
    print(json.dumps(result))
    
    sys.exit(0 if result["success"] else 1)

if __name__ == "__main__":
    main()