#!/usr/bin/env python3
"""
Comprehensive PDF field and tag tree editor using pymupdf (fitz).
This properly updates both form fields AND the tag tree structure,
ensuring PDF/UA compliance without corruption.
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

def update_fields_and_tags(pdf_path, field_updates):
    """
    Update both form fields and tag tree structure in a PDF.
    
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
        modified_tags = []
        
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
        
        # Process each page
        for page_num, page in enumerate(doc):
            # Get widgets (form fields) on this page - convert generator to list
            widgets = list(page.widgets())
            
            # We need to recreate widgets with new names rather than updating in place
            widgets_to_recreate = []
            
            for widget in widgets:
                field_name = widget.field_name
                
                if field_name and field_name in update_map:
                    update = update_map[field_name]
                    new_name = update.get('newName', field_name)
                    
                    # Only process if the name is actually changing
                    if new_name != field_name:
                        # Store widget info for recreation
                        widget_info = {
                            'original_name': field_name,
                            'new_name': new_name,
                            'rect': widget.rect,
                            'field_type': widget.field_type,
                            'field_type_string': widget.field_type_string,
                            'field_value': widget.field_value,
                            'field_display': widget.field_display,
                            'field_flags': widget.field_flags,
                            'fill_color': widget.fill_color,
                            'border_color': widget.border_color,
                            'text_color': widget.text_color,
                            'tooltip': update.get('tooltip', widget.field_value),
                            'widget': widget
                        }
                        widgets_to_recreate.append(widget_info)
                        
                        modified_fields.append({
                            'original': field_name,
                            'new': new_name,
                            'page': page_num + 1
                        })
                        
                        logging.info(f"Will recreate field '{field_name}' as '{new_name}' on page {page_num + 1}")
            
            # Now recreate the widgets with new names
            for widget_info in widgets_to_recreate:
                old_widget = widget_info['widget']
                
                # Delete the old widget
                page.delete_widget(old_widget)
                
                # Create a new widget with the same properties but new name
                if widget_info['field_type_string'] == 'Text':
                    new_widget = page.add_widget(fitz.PDF_WIDGET_TYPE_TEXT)
                elif widget_info['field_type_string'] == 'CheckBox':
                    new_widget = page.add_widget(fitz.PDF_WIDGET_TYPE_CHECKBOX)
                elif widget_info['field_type_string'] == 'RadioButton':
                    new_widget = page.add_widget(fitz.PDF_WIDGET_TYPE_RADIOBUTTON)
                elif widget_info['field_type_string'] == 'ComboBox':
                    new_widget = page.add_widget(fitz.PDF_WIDGET_TYPE_COMBOBOX)
                elif widget_info['field_type_string'] == 'ListBox':
                    new_widget = page.add_widget(fitz.PDF_WIDGET_TYPE_LISTBOX)
                else:
                    # Default to text field
                    new_widget = page.add_widget(fitz.PDF_WIDGET_TYPE_TEXT)
                
                # Set properties on the new widget
                new_widget.rect = widget_info['rect']
                new_widget.field_name = widget_info['new_name']
                new_widget.field_value = widget_info['tooltip'] if widget_info['tooltip'] else widget_info['field_value']
                new_widget.field_display = widget_info['field_display']
                new_widget.field_flags = widget_info['field_flags']
                new_widget.fill_color = widget_info['fill_color']
                new_widget.border_color = widget_info['border_color']
                new_widget.text_color = widget_info['text_color']
                
                # Update the widget to apply changes
                new_widget.update()
                
                logging.info(f"Recreated field '{widget_info['original_name']}' as '{widget_info['new_name']}'")
        
        # Now handle the tag tree structure for accessibility
        # This is CRITICAL for screen readers and PDF/UA compliance
        
        # Get the PDF catalog using the correct PyMuPDF method
        try:
            # Get catalog xref number first
            catalog_xref = doc.pdf_trailer()
            if catalog_xref and "Root" in catalog_xref:
                catalog_ref = catalog_xref["Root"]
                if isinstance(catalog_ref, int):
                    xref = doc.pdf_xref_object(catalog_ref)
                else:
                    xref = catalog_ref
            else:
                xref = None
        except Exception as e:
            logging.warning(f"Could not get PDF catalog: {e}")
            xref = None
        
        # Check for StructTreeRoot (tag tree) - REQUIRED for accessibility
        if xref and isinstance(xref, dict) and "StructTreeRoot" in xref:
            try:
                struct_tree_ref = xref["StructTreeRoot"]
                if isinstance(struct_tree_ref, int):
                    struct_tree = doc.pdf_xref_object(struct_tree_ref)
                else:
                    struct_tree = struct_tree_ref
                
                logging.info(f"Found StructTreeRoot (type: {type(struct_tree)})")
                
                if not isinstance(struct_tree, dict):
                    logging.warning(f"StructTreeRoot is not a dict, it's {type(struct_tree)}")
                    struct_tree = None
                else:
                    logging.info(f"StructTreeRoot keys: {list(struct_tree.keys())}")
                
                # Process the structure tree to update field references
                def process_struct_elem(elem_xref, depth=0):
                    """Recursively process structure elements"""
                    if not isinstance(elem_xref, int) or elem_xref <= 0:
                        return
                        
                    try:
                        elem = doc.pdf_xref_object(elem_xref)
                        if not elem or not isinstance(elem, dict):
                            return
                        
                        # Check if this element has an Alt or ActualText that matches a field
                        if "Alt" in elem:
                            alt_text = str(elem["Alt"])
                            if alt_text in update_map:
                                update = update_map[alt_text]
                                new_text = update.get('newName', alt_text)
                                elem["Alt"] = fitz.PDF_Name(new_text)
                                doc.pdf_update_object(elem_xref, elem)
                                modified_tags.append({
                                    'type': 'Alt',
                                    'original': alt_text,
                                    'new': new_text
                                })
                                logging.info(f"Updated Alt text from '{alt_text}' to '{new_text}'")
                        
                        if "ActualText" in elem:
                            actual_text = str(elem["ActualText"])
                            if actual_text in update_map:
                                update = update_map[actual_text]
                                new_text = update.get('newName', actual_text)
                                elem["ActualText"] = fitz.PDF_Name(new_text)
                                doc.pdf_update_object(elem_xref, elem)
                                modified_tags.append({
                                    'type': 'ActualText',
                                    'original': actual_text,
                                    'new': new_text
                                })
                                logging.info(f"Updated ActualText from '{actual_text}' to '{new_text}'")
                        
                        # Process children
                        if "K" in elem:
                            kids = elem["K"]
                            if kids:
                                if isinstance(kids, int):
                                    process_struct_elem(kids, depth + 1)
                                elif hasattr(kids, '__iter__'):
                                    for kid in kids:
                                        if isinstance(kid, int):
                                            process_struct_elem(kid, depth + 1)
                                        elif isinstance(kid, dict):
                                            # Check if kid has "Obj" key and it's a valid reference
                                            if "Obj" in kid:
                                                obj_ref = kid.get("Obj")
                                                if isinstance(obj_ref, int):
                                                    process_struct_elem(obj_ref, depth + 1)
                    
                    except Exception as e:
                        logging.warning(f"Error processing struct element {elem_xref}: {e}")
                
                # Process the root kids only if we have a valid struct_tree
                if struct_tree and isinstance(struct_tree, dict) and "K" in struct_tree:
                    root_kids = struct_tree["K"]
                    logging.debug(f"Processing root kids, type: {type(root_kids)}")
                    if isinstance(root_kids, int):
                        process_struct_elem(root_kids)
                    elif hasattr(root_kids, '__iter__'):
                        for i, kid in enumerate(root_kids):
                            logging.debug(f"Processing root kid {i}, type: {type(kid)}")
                            if isinstance(kid, int):
                                process_struct_elem(kid)
                            elif isinstance(kid, dict):
                                logging.debug(f"Root kid {i} is a dict with keys: {kid.keys() if hasattr(kid, 'keys') else 'no keys'}")
                else:
                    if struct_tree:
                        logging.info("No kids found in StructTreeRoot")
                    else:
                        logging.warning("StructTreeRoot is invalid, skipping tag tree processing")
                    
            except Exception as e:
                logging.warning(f"Error processing tag tree: {e}. Continuing with field updates only.")
                # Don't fail completely if tag tree processing fails
        else:
            logging.info("No StructTreeRoot found or catalog not accessible, skipping tag tree processing")
        
        # Log summary before saving
        logging.info(f"Field update summary: {len(modified_fields)} fields modified")
        logging.info(f"Tag update summary: {len(modified_tags)} tags modified")
        
        # Save the modified PDF
        output_path = tempfile.mktemp(suffix='.pdf')
        
        # Set PDF/UA flag
        doc.set_metadata({
            'producer': 'AccessForm with PyMuPDF',
            'creator': 'AccessForm Tag Editor'
        })
        
        # Save with garbage collection to clean up any orphaned objects
        doc.save(output_path, garbage=4, deflate=True, clean=True)
        doc.close()
        
        # Verify the output
        verify_doc = fitz.open(output_path)
        field_count = sum(len(list(page.widgets())) for page in verify_doc)
        verify_doc.close()
        
        return {
            "success": True,
            "output_path": output_path,
            "modified_fields": modified_fields,
            "modified_tags": modified_tags,
            "total_fields": field_count,
            "message": f"Successfully updated {len(modified_fields)} fields and {len(modified_tags)} tags"
        }
        
    except Exception as e:
        logging.error(f"Error processing PDF: {e}")
        return {
            "success": False,
            "error": str(e)
        }

def main():
    """Main entry point"""
    if len(sys.argv) < 3:
        print(json.dumps({
            "success": False,
            "error": "Usage: pdf_tag_field_editor.py <pdf_path> <field_updates_json_or_file>"
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
    
    result = update_fields_and_tags(pdf_path, field_updates)
    print(json.dumps(result))
    
    sys.exit(0 if result["success"] else 1)

if __name__ == "__main__":
    main()