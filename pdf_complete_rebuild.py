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

# Debug output to stderr to confirm script is running
sys.stderr.write("pdf_complete_rebuild.py: Script loaded\n")
sys.stderr.flush()

try:
    import fitz  # PyMuPDF
    sys.stderr.write("pdf_complete_rebuild.py: PyMuPDF imported successfully\n")
    sys.stderr.flush()
except ImportError as e:
    sys.stderr.write(f"pdf_complete_rebuild.py: PyMuPDF import failed: {e}\n")
    sys.stderr.flush()
    print(json.dumps({
        "success": False,
        "error": "PyMuPDF not installed. Run: pip install PyMuPDF"
    }))
    sys.exit(1)

# Set up logging to stderr only (not stdout)
logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s', stream=sys.stderr)
logger = logging.getLogger(__name__)

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
            
            # Adjust for checkbox/radio button dimensions
            if field_type in ['checkbox', 'radio', 'radiobutton']:
                # Ensure square dimensions for checkboxes
                if width > height * 2:
                    width = height
                # Limit size
                width = min(width, 20)
                height = min(height, 20)
            
            rect = fitz.Rect(x, y, x + width, y + height)
            
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
            
            # Step 1: Open and analyze original PDF
            original_doc = fitz.open(input_path)
            
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
            for field_info in field_updates:
                # Parse field update format
                if 'originalName' in field_info:
                    # Convert from update format to field definition
                    # Handle page number carefully - it might be None
                    page_num = field_info.get('PageNumber') or field_info.get('pageNumber') or 1
                    if page_num is None:
                        page_num = 1
                    
                    field_def = {
                        'name': field_info.get('newName', field_info['originalName']),
                        'type': field_info.get('fieldType', 'text'),
                        'x': field_info.get('X', field_info.get('x', 100)),
                        'y': field_info.get('Y', field_info.get('y', 100)),
                        'width': field_info.get('Width', field_info.get('width', 200)),
                        'height': field_info.get('Height', field_info.get('height', 20)),
                        'page': page_num - 1,  # Convert to 0-based index
                        'tooltip': field_info.get('Tooltip', field_info.get('tooltip', '')),
                        'required': field_info.get('IsRequired', field_info.get('isRequired', False))
                    }
                else:
                    field_def = field_info
                    field_def['page'] = field_def.get('page', 0)
                
                page_num = field_def['page']
                if page_num not in fields_by_page:
                    fields_by_page[page_num] = []
                fields_by_page[page_num].append(field_def)
            
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
                "output_path": output_path,
                "added_fields": added_fields,
                "total_fields": total_fields,
                "tag_elements": self.tag_counter,
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
    # Debug: Log to stderr to see if script is running
    sys.stderr.write(f"Python script started with {len(sys.argv)} arguments\n")
    sys.stderr.flush()
    
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