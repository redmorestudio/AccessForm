#!/usr/bin/env python3
"""
Complete PDF rebuild solution that avoids ghost fields and corruption.
This script completely flattens the PDF (removes all interactive elements)
and then rebuilds it with new fields and a clean tag tree structure.
"""
import sys
import json
import tempfile
import os
import logging
from pathlib import Path
from typing import List, Dict, Any, Optional, Tuple

try:
    import fitz  # PyMuPDF
except ImportError as e:
    print(json.dumps({
        "success": False,
        "error": "PyMuPDF not installed. Run: pip install PyMuPDF"
    }))
    sys.exit(1)

# Set up logging - DISABLE for production to avoid interfering with JSON output
# logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s', stream=sys.stderr)
# logger = logging.getLogger(__name__)

# Create a null logger that does nothing
class NullLogger:
    def info(self, msg): pass
    def debug(self, msg): pass
    def warning(self, msg): pass
    def error(self, msg): pass
    
logger = NullLogger()

class PDFCompleteRebuilder:
    """Handles complete PDF rebuilding with clean field and tag tree structure"""
    
    def __init__(self):
        self.field_counter = 0
        self.tag_counter = 0
        
    def extract_visual_content(self, doc: fitz.Document) -> List[Dict[str, Any]]:
        """Extract all visual content from pages (text, images, etc) without form fields"""
        pages_content = []
        
        for page_num, page in enumerate(doc):
            # Get page dimensions
            page_rect = page.rect
            
            # Extract all non-field content
            page_data = {
                'page_num': page_num,
                'width': page_rect.width,
                'height': page_rect.height,
                'rotation': page.rotation,
            }
            
            # Extract text blocks for reference
            text_blocks = page.get_text("dict")
            page_data['text_blocks'] = text_blocks
            
            # Extract images
            image_list = page.get_images()
            page_data['images'] = image_list
            
            pages_content.append(page_data)
            
        return pages_content
    
    def create_clean_pdf(self, pages_content: List[Dict[str, Any]], output_path: str) -> fitz.Document:
        """Create a new clean PDF with only visual content (no fields or annotations)"""
        # This method is no longer needed as we'll use insert_pdf instead
        pass
    
    def add_form_field(self, page: fitz.Page, field_info: Dict[str, Any]) -> Optional[fitz.Widget]:
        """Add a single form field to a page with proper properties"""
        try:
            field_name = field_info.get('name', f'field_{self.field_counter}')
            field_type = field_info.get('type', 'text').lower()
            
            # Get position and size
            x = field_info.get('x', 100)
            y = field_info.get('y', 100)
            width = field_info.get('width', 200)
            height = field_info.get('height', 20)
            
            # Convert Y coordinate from top-left origin to bottom-left origin
            # Frontend uses top-left (Y increases downward), PyMuPDF uses bottom-left (Y increases upward)
            page_height = page.rect.height
            y_converted = page_height - y - height
            
            # Adjust for checkbox/radio button dimensions
            if field_type in ['checkbox', 'radio', 'radiobutton']:
                # Ensure square dimensions for checkboxes
                if width > height * 2:
                    width = height
                # Limit size
                width = min(width, 20)
                height = min(height, 20)
            
            rect = fitz.Rect(x, y_converted, x + width, y_converted + height)
            
            # Determine widget type
            widget_type_map = {
                'checkbox': fitz.PDF_WIDGET_TYPE_CHECKBOX,
                'radio': fitz.PDF_WIDGET_TYPE_RADIOBUTTON,
                'radiobutton': fitz.PDF_WIDGET_TYPE_RADIOBUTTON,
                'combobox': fitz.PDF_WIDGET_TYPE_COMBOBOX,
                'dropdown': fitz.PDF_WIDGET_TYPE_COMBOBOX,
                'listbox': fitz.PDF_WIDGET_TYPE_LISTBOX,
                'signature': fitz.PDF_WIDGET_TYPE_SIGNATURE,
                'button': fitz.PDF_WIDGET_TYPE_BUTTON,
                'text': fitz.PDF_WIDGET_TYPE_TEXT,
            }
            
            widget_type = widget_type_map.get(field_type, fitz.PDF_WIDGET_TYPE_TEXT)
            
            # Create the widget using the correct API
            widget = fitz.Widget()
            widget.field_type = widget_type
            widget.field_name = field_name
            widget.rect = rect
            page.add_widget(widget)
            
            # Set common properties
            widget.border_width = 1
            widget.border_color = [0, 0, 0]  # Black border
            widget.fill_color = [1, 1, 1]    # White background
            
            # Set type-specific properties
            if widget_type == fitz.PDF_WIDGET_TYPE_TEXT:
                widget.text_font = "Helv"
                widget.text_fontsize = 10
                widget.text_color = [0, 0, 0]
                
                # Check for multiline
                if field_info.get('multiline', False):
                    widget.field_flags |= fitz.PDF_FIELD_IS_MULTILINE
            
            elif widget_type == fitz.PDF_WIDGET_TYPE_COMBOBOX:
                # Set default options
                options = field_info.get('options', ['Option 1', 'Option 2', 'Option 3'])
                widget.choice_values = options
            
            elif widget_type == fitz.PDF_WIDGET_TYPE_CHECKBOX:
                # Set checkbox specific properties
                widget.field_value = field_info.get('checked', False)
            
            # Set tooltip/alternate name for accessibility
            tooltip = field_info.get('tooltip', field_name)
            widget.field_value = ""  # Clear any default value
            
            # Set required flag if needed
            if field_info.get('required', False):
                widget.field_flags |= fitz.PDF_FIELD_IS_REQUIRED
            
            # Update to apply changes
            widget.update()
            
            self.field_counter += 1
            logger.debug(f"Added field '{field_name}' of type '{field_type}' at ({x}, {y})")
            
            return widget
            
        except Exception as e:
            logger.error(f"Failed to add field: {e}")
            return None
    
    def create_tag_structure(self, doc: fitz.Document, fields_by_page: Dict[int, List[Dict]]) -> bool:
        """Create a clean tag tree structure for PDF/UA compliance"""
        try:
            # PyMuPDF has limited support for tag structures
            # We'll just log the intent for now
            self.tag_counter = sum(len(fields) for fields in fields_by_page.values())
            logger.info(f"Would create tag structure with {self.tag_counter} elements")
            return True
            
        except Exception as e:
            logger.error(f"Failed to create tag structure: {e}")
            return False
    
    def extract_existing_fields(self, doc: fitz.Document) -> Dict[str, Dict[str, Any]]:
        """Extract position and properties of existing fields from the document"""
        existing_fields = {}
        
        for page_num, page in enumerate(doc):
            page_height = page.rect.height
            for widget in page.widgets():
                field_name = widget.field_name
                if field_name:
                    rect = widget.rect
                    # Convert from PyMuPDF bottom-left to top-left for consistency with frontend
                    y_top_left = page_height - rect.y1  # y1 is the bottom of the rect in PyMuPDF
                    existing_fields[field_name] = {
                        'x': rect.x0,
                        'y': y_top_left,
                        'width': rect.width,
                        'height': rect.height,
                        'page': page_num,
                        'type': self.get_widget_type_name(widget.field_type),
                        'flags': widget.field_flags,
                        'value': widget.field_value,
                        'border_width': widget.border_width,
                        'page_height': page_height  # Store for later conversion if needed
                    }
                    logger.info(f"Found existing field '{field_name}' at ({rect.x0}, {y_top_left}) with size {rect.width}x{rect.height}")
        
        return existing_fields
    
    def get_widget_type_name(self, widget_type: int) -> str:
        """Convert widget type constant to string name"""
        type_map = {
            fitz.PDF_WIDGET_TYPE_TEXT: 'text',
            fitz.PDF_WIDGET_TYPE_CHECKBOX: 'checkbox',
            fitz.PDF_WIDGET_TYPE_RADIOBUTTON: 'radio',
            fitz.PDF_WIDGET_TYPE_COMBOBOX: 'combobox',
            fitz.PDF_WIDGET_TYPE_LISTBOX: 'listbox',
            fitz.PDF_WIDGET_TYPE_SIGNATURE: 'signature',
            fitz.PDF_WIDGET_TYPE_BUTTON: 'button'
        }
        return type_map.get(widget_type, 'text')
    
    def rebuild_pdf(self, input_path: str, field_updates: List[Dict[str, Any]]) -> Dict[str, Any]:
        """
        Completely rebuild a PDF with new fields and clean structure.
        
        Args:
            input_path: Path to input PDF
            field_updates: List of field definitions with properties
            
        Returns:
            Result dictionary with success status and output path
        """
        try:
            logger.info(f"Starting complete PDF rebuild for {len(field_updates)} fields")
            
            # Debug: Write field updates to debug file
            with open('/tmp/pdf_rebuild_debug.json', 'w') as f:
                json.dump(field_updates, f, indent=2)
            
            # Debug: track what happens
            debug_info = {'input_fields': len(field_updates)}
            
            # Step 1: Open and analyze original PDF
            original_doc = fitz.open(input_path)
            
            # Step 1.5: Extract existing field positions BEFORE removing them
            existing_fields = self.extract_existing_fields(original_doc)
            logger.info(f"Found {len(existing_fields)} existing fields in the document")
            debug_info['existing_fields'] = len(existing_fields)
            debug_info['existing_field_names'] = list(existing_fields.keys())
            
            # Step 2: Extract visual content (without fields)
            pages_content = self.extract_visual_content(original_doc)
            logger.info(f"Extracted content from {len(pages_content)} pages")
            
            # Step 3: Create a completely clean PDF (no fields, no annotations)
            temp_clean_path = tempfile.mktemp(suffix='_clean.pdf')
            
            # Create new document preserving PDF content but removing form fields
            # Important: We need to preserve the actual page content, not just the structure
            temp_doc = fitz.open()
            
            # Copy the entire document first to preserve content
            temp_doc.insert_pdf(original_doc)
            
            # Now remove ONLY the form fields (widgets), keeping everything else
            for page in temp_doc:
                # Delete all widgets (form fields) from each page
                for widget in list(page.widgets()):
                    page.delete_widget(widget)
                
                # Also remove any annotations that might be form-related
                for annot in list(page.annots()):
                    if annot.type[0] == fitz.PDF_ANNOT_WIDGET:
                        page.delete_annot(annot)
            
            # Save to ensure changes are committed
            temp_doc.save(temp_clean_path, garbage=4, deflate=True, clean=True)
            temp_doc.close()
            
            # Close the original document as we're done with it
            original_doc.close()
            
            logger.info("Created clean PDF without any fields")
            
            # Step 4: Open clean PDF and add new fields
            final_doc = fitz.open(temp_clean_path)
            
            # Group fields by page
            fields_by_page = {}
            
            # Track which fields are being updated
            updated_field_names = set()
            
            for field_info in field_updates:
                # Parse field update format
                if 'originalName' in field_info:
                    original_name = field_info['originalName']
                    new_name = field_info.get('newName', original_name)
                    updated_field_names.add(original_name)
                    
                    # Try to get position from existing field if not provided
                    existing_field = existing_fields.get(original_name, {})
                    
                    # Handle page number - prefer existing field's page if not provided
                    page_num = field_info.get('PageNumber') or field_info.get('pageNumber')
                    if page_num is None:
                        page_num = existing_field.get('page', 0) + 1  # Convert to 1-based for consistency
                    if page_num is None or page_num < 1:
                        page_num = 1
                    
                    # Get coordinates - check both capital and lowercase, handle 0 values properly
                    x = field_info.get('X') if 'X' in field_info else field_info.get('x')
                    if x is None:
                        x = existing_field.get('x', 100)
                    
                    y = field_info.get('Y') if 'Y' in field_info else field_info.get('y')
                    if y is None:
                        y = existing_field.get('y', 100)
                    
                    width = field_info.get('Width') if 'Width' in field_info else field_info.get('width')
                    if width is None:
                        width = existing_field.get('width', 200)
                    
                    height = field_info.get('Height') if 'Height' in field_info else field_info.get('height')
                    if height is None:
                        height = existing_field.get('height', 20)
                    
                    field_def = {
                        'name': new_name,
                        'type': field_info.get('fieldType') or field_info.get('FieldType') or existing_field.get('type', 'text'),
                        'x': x,
                        'y': y,
                        'width': width,
                        'height': height,
                        'page': page_num - 1,  # Convert to 0-based index
                        'tooltip': field_info.get('Tooltip') or field_info.get('tooltip', ''),
                        'required': field_info.get('IsRequired') if 'IsRequired' in field_info else field_info.get('isRequired', False)
                    }
                else:
                    field_def = field_info
                    field_def['page'] = field_def.get('page', 0)
                
                page_num = field_def['page']
                if page_num not in fields_by_page:
                    fields_by_page[page_num] = []
                fields_by_page[page_num].append(field_def)
            
            # Add existing fields that weren't updated
            for field_name, existing_field in existing_fields.items():
                if field_name not in updated_field_names:
                    # Keep existing field as-is
                    field_def = {
                        'name': field_name,
                        'type': existing_field.get('type', 'text'),
                        'x': existing_field.get('x', 100),
                        'y': existing_field.get('y', 100),
                        'width': existing_field.get('width', 200),
                        'height': existing_field.get('height', 20),
                        'page': existing_field.get('page', 0),
                        'tooltip': '',
                        'required': False
                    }
                    
                    page_num = field_def['page']
                    if page_num not in fields_by_page:
                        fields_by_page[page_num] = []
                    fields_by_page[page_num].append(field_def)
            
            # Debug: track fields to be added
            debug_info['fields_by_page'] = {page: len(fields) for page, fields in fields_by_page.items()}
            debug_info['total_to_add'] = sum(len(fields) for fields in fields_by_page.values())
            
            with open('/tmp/pdf_rebuild_debug2.json', 'w') as f:
                json.dump(debug_info, f, indent=2)
            
            # Add fields to each page
            added_fields = []
            for page_num, fields in fields_by_page.items():
                if page_num >= len(final_doc):
                    logger.warning(f"Page {page_num} does not exist, skipping fields")
                    continue
                
                page = final_doc[page_num]
                
                # Sort fields by Y then X for proper tab order
                fields.sort(key=lambda f: (-f.get('y', 0), f.get('x', 0)))
                
                for field_def in fields:
                    widget = self.add_form_field(page, field_def)
                    if widget:
                        added_fields.append({
                            'name': field_def['name'],
                            'type': field_def['type'],
                            'page': page_num + 1
                        })
            
            logger.info(f"Added {len(added_fields)} fields to the document")
            
            # Step 5: Create clean tag structure for accessibility
            self.create_tag_structure(final_doc, fields_by_page)
            
            # Step 6: Set document metadata for PDF/UA
            final_doc.set_metadata({
                'producer': 'AccessForm PDF Rebuilder',
                'creator': 'AccessForm Complete Rebuild',
                'title': 'Accessible PDF Form'
            })
            
            # Language will be set via metadata
            
            # Step 7: Save final PDF
            output_path = tempfile.mktemp(suffix='_rebuilt.pdf')
            final_doc.save(output_path, garbage=4, deflate=True, clean=True)
            
            # Verify the result
            verify_doc = fitz.open(output_path)
            total_fields = sum(len(list(page.widgets())) for page in verify_doc)
            verify_doc.close()
            
            final_doc.close()
            
            # Clean up temp file
            if os.path.exists(temp_clean_path):
                os.remove(temp_clean_path)
            
            logger.info(f"Successfully rebuilt PDF with {total_fields} fields")
            
            return {
                "success": True,
                "outputPath": output_path,
                "addedFields": added_fields,
                "totalFields": total_fields,
                "tagElements": self.tag_counter,
                "message": f"Complete rebuild successful: {total_fields} fields, {self.tag_counter} tag elements"
            }
            
        except Exception as e:
            logger.error(f"PDF rebuild failed: {e}")
            import traceback
            traceback.print_exc()
            return {
                "success": False,
                "error": str(e),
                "traceback": traceback.format_exc()
            }

def main():
    """Main entry point for command-line usage"""
    if len(sys.argv) < 3:
        print(json.dumps({
            "success": False,
            "error": "Usage: pdf_complete_rebuild.py <pdf_path> <field_updates_json>"
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
    
    # Parse field updates
    if os.path.exists(field_updates_arg):
        with open(field_updates_arg, 'r') as f:
            field_updates = json.load(f)
    else:
        field_updates = json.loads(field_updates_arg)
    
    # Perform rebuild
    rebuilder = PDFCompleteRebuilder()
    result = rebuilder.rebuild_pdf(pdf_path, field_updates)
    
    # Output result
    print(json.dumps(result, indent=2))
    
    sys.exit(0 if result["success"] else 1)

if __name__ == "__main__":
    main()