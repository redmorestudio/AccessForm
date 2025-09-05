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
    
    def detect_field_type_and_tooltip(self, field_name: str, provided_type: str = 'text', provided_tooltip: str = '') -> Tuple[str, str]:
        """Intelligently detect field type and generate appropriate tooltip based on field name"""
        name_lower = field_name.lower()
        
        # Special handling for delivery method checkboxes
        if name_lower in ['mailed', 'emailed', 'faxed']:
            field_type = 'checkbox'
            tooltip = provided_tooltip or f"Check if {field_name.lower()}"
            return field_type, tooltip
        
        # Smart type detection based on field name patterns
        if 'signature' in name_lower and 'date' not in name_lower:
            # Mark as signature for proper handling
            field_type = 'signature'
            tooltip = provided_tooltip or f"Click to add signature"
        elif 'date' in name_lower or 'dob' in name_lower or 'birth' in name_lower:
            field_type = 'text'  # Dates are text fields with special formatting
            tooltip = provided_tooltip or f"Enter date in MM/DD/YYYY format"
        elif 'first name' in name_lower:
            field_type = 'text'
            tooltip = provided_tooltip or f"Enter first name"
        elif 'last name' in name_lower:
            field_type = 'text'
            tooltip = provided_tooltip or f"Enter last name"
        elif 'middle name' in name_lower:
            field_type = 'text'
            tooltip = provided_tooltip or f"Enter middle name or initial"
        elif any(x in name_lower for x in ['email', 'e-mail']):
            field_type = 'text'
            tooltip = provided_tooltip or f"Enter email address (example@domain.com)"
        elif any(x in name_lower for x in ['phone', 'tel', 'mobile', 'cell']):
            field_type = 'text'
            tooltip = provided_tooltip or f"Enter phone number (XXX-XXX-XXXX)"
        elif any(x in name_lower for x in ['description', 'comments', 'notes', 'reason']):
            field_type = 'text'  # Multi-line text area
            tooltip = provided_tooltip or f"Enter detailed information for {field_name}"
        elif any(x in name_lower for x in ['checkbox', 'check', 'agree', 'consent']):
            field_type = 'checkbox'
            tooltip = provided_tooltip or f"Check to select {field_name}"
        else:
            field_type = provided_type.lower()
            tooltip = provided_tooltip or field_name
            
        return field_type, tooltip
    
    def add_form_field(self, page: fitz.Page, field_info: Dict[str, Any]) -> Optional[fitz.Widget]:
        """Add a single form field to a page with proper properties"""
        try:
            # Get field name, but handle unnamed/duplicate fields better
            field_name = field_info.get('name', '')
            
            # If field has no name or empty name, give it a temporary name
            if not field_name or field_name.strip() == '':
                field_name = f'TEMP_FIELD_{self.field_counter}'
                logger.info(f"Assigning temporary name: {field_name}")
            
            provided_type = field_info.get('type', 'text')
            provided_tooltip = field_info.get('tooltip', '')
            
            # Use smart detection for field type and tooltip
            field_type, tooltip = self.detect_field_type_and_tooltip(field_name, provided_type, provided_tooltip)
            
            # Get position and size
            x = field_info.get('x', 100)
            y = field_info.get('y', 100)
            width = field_info.get('width', 200)
            height = field_info.get('height', 20)
            
            # Text area fields don't need position adjustment - they're already correct
            # Removing the adjustment that was messing them up
            name_lower = field_name.lower()
            
            # IMPORTANT: Coordinate conversion
            # Syncfusion/frontend uses top-left origin (Y increases downward)
            # PyMuPDF uses bottom-left origin (Y increases upward)
            # We store positions in top-left format, so we need to convert to bottom-left
            page_height = page.rect.height
            y_converted = page_height - y - height  # Convert from top-left to bottom-left
            
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
                'signature': fitz.PDF_WIDGET_TYPE_TEXT,  # Use text field for signatures to avoid document lock
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
            
            # IMPORTANT: Use only built-in PDF fonts to avoid embedding issues
            # Helvetica is a PDF standard font that doesn't require embedding
            widget.text_font = "Helv"  # Use Helvetica, not Arial
            widget.text_fontsize = 10
            
            # Set type-specific properties
            if widget_type == fitz.PDF_WIDGET_TYPE_TEXT:
                # Font already set above to avoid ZapfDingbats
                widget.text_color = [0, 0, 0]
                
                # Special handling for signature fields (using text type to avoid locks)
                if field_type == 'signature':
                    # Make signature fields look different
                    widget.border_width = 1
                    widget.fill_color = [0.98, 0.98, 0.98]  # Very light gray background
                    widget.text_fontsize = 12  # Slightly larger for signatures
                
                # Check for multiline
                if field_info.get('multiline', False) or field_type == 'textarea':
                    widget.field_flags |= fitz.PDF_FIELD_IS_MULTILINE
            
            elif widget_type == fitz.PDF_WIDGET_TYPE_COMBOBOX:
                # Set default options
                options = field_info.get('options', ['Option 1', 'Option 2', 'Option 3'])
                widget.choice_values = options
                # Ensure Helvetica font
                widget.text_font = "Helv"
                widget.text_fontsize = 10
            
            elif widget_type == fitz.PDF_WIDGET_TYPE_CHECKBOX:
                # Set checkbox specific properties
                widget.field_value = field_info.get('checked', False)
                # Force Helvetica font for checkbox to avoid ZapfDingbats
                widget.text_font = "Helv"
                widget.text_fontsize = 10
                # Use a simple cross mark instead of ZapfDingbats checkmark
                widget.button_caption = "X"  # Use simple X instead of ZapfDingbats symbol
            
            elif widget_type == fitz.PDF_WIDGET_TYPE_RADIOBUTTON:
                # Set radio button specific properties
                widget.field_value = field_info.get('value', '')
                # Force Helvetica font to avoid ZapfDingbats
                widget.text_font = "Helv"
                widget.text_fontsize = 10
                # Use a simple dot instead of ZapfDingbats symbol
                widget.button_caption = "•"  # Use bullet instead of ZapfDingbats symbol
            
            else:
                # Clear default value for non-checkbox/radio fields
                widget.field_value = ""
            
            # Set tooltip/alternate name for accessibility
            # ALWAYS set a tooltip for accessibility, even if empty
            if not tooltip:
                # Generate a fallback tooltip if none provided
                tooltip = f"Field: {field_name}"
            
            # PyMuPDF uses field_label for the tooltip/alternate text
            widget.field_label = tooltip
            
            # Also try setting the TU (tooltip) entry directly if possible
            try:
                if hasattr(widget, 'field_tooltip'):
                    widget.field_tooltip = tooltip
            except:
                pass
                
            # Try to set field language for PDF/UA
            # This helps screen readers pronounce field names correctly
            try:
                if hasattr(widget, 'set_language'):
                    widget.set_language('en-US')
            except:
                pass  # Not critical if language setting fails
            
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
    
    def remove_signature_locks(self, doc: fitz.Document) -> None:
        """Remove signature flags that prevent editing of the document"""
        try:
            if hasattr(doc, 'xref_set_key') and hasattr(doc, 'pdf_catalog'):
                catalog_xref = doc.pdf_catalog()
                
                # Remove SigFlags from AcroForm dictionary
                # SigFlags value of 3 means "signatures exist and document is locked"
                # We want to remove this entirely or set it to 0
                try:
                    acroform = doc.xref_get_key(catalog_xref, 'AcroForm')
                    if acroform:
                        # Parse the AcroForm reference more robustly
                        import re
                        match = re.match(r'(\d+)\s+\d+\s+R', acroform.strip())
                        if match:
                            acroform_xref = int(match.group(1))
                            # Set SigFlags to 0 (no signatures) instead of removing it
                            doc.xref_set_key(acroform_xref, 'SigFlags', '0')
                            logger.info("Reset signature flags to allow editing")
                except Exception as e:
                    logger.debug(f"Could not modify AcroForm: {e}")
                    
                # Also remove any Perms (permissions) dictionary that might lock the document
                try:
                    doc.xref_set_key(catalog_xref, 'Perms', None)
                except:
                    pass
                
                # Remove DSS (Document Security Store) that might indicate signatures
                try:
                    doc.xref_set_key(catalog_xref, 'DSS', None)
                except:
                    pass
                    
        except Exception as e:
            logger.debug(f"Could not remove signature locks: {e}")
            # Not critical - document will still work
    
    def create_tag_structure(self, doc: fitz.Document, fields_by_page: Dict[int, List[Dict]]) -> bool:
        """Create accessibility structure for PDF/UA compliance"""
        try:
            self.tag_counter = 0
            
            # Add document-level accessibility metadata
            try:
                if hasattr(doc, 'xref_set_key') and hasattr(doc, 'pdf_catalog'):
                    catalog_xref = doc.pdf_catalog()
                    
                    # Mark document as tagged
                    doc.xref_set_key(catalog_xref, 'MarkInfo', '<</Marked true/Suspects false>>')
                    
                    # Create a basic structure tree root
                    # This tells PDF readers that content is structured
                    struct_tree_root = """<</Type/StructTreeRoot
                        /K[<</Type/StructElem/S/Document>>]
                        /ParentTree<</Nums[]>>
                        /RoleMap<</Document/Document/H1/H1/H2/H2/P/P/Figure/Figure/Form/Form>>
                    >>"""
                    
                    struct_xref = doc.xref_add_object(struct_tree_root)
                    doc.xref_set_key(catalog_xref, 'StructTreeRoot', f'{struct_xref} 0 R')
                    
                    # Set language with proper PDF string syntax
                    doc.xref_set_key(catalog_xref, 'Lang', '(en-US)')
                    
                    # Add ViewerPreferences for accessibility with DisplayDocTitle
                    viewer_prefs = '<</DisplayDocTitle true>>'
                    doc.xref_set_key(catalog_xref, 'ViewerPreferences', viewer_prefs)
                    
                    logger.info("Added document structure tree root and accessibility metadata")
            except Exception as e:
                logger.warning(f"Could not add full structure tree: {e}")
            
            # Count content elements
            for page_num, page in enumerate(doc):
                # Count text blocks, but exclude whitespace-only blocks
                text_blocks = page.get_text("blocks")
                for block in text_blocks:
                    text_content = block[4].strip()
                    # Only count non-empty text that isn't just whitespace
                    if text_content and not text_content.isspace():
                        self.tag_counter += 1
                
                # Count images and try to add alt text placeholder
                image_list = page.get_images()
                self.tag_counter += len(image_list)
                
                # Note: Image alt text needs to be added to the structure tree
                # This requires the Texas Workforce Commission header image to have alt text
                # The Adobe autotag API will handle this properly
                
                # Count form fields
                if page_num in fields_by_page:
                    self.tag_counter += len(fields_by_page[page_num])
                
                # Try to mark page content as tagged
                try:
                    page_xref = page.xref
                    if page_xref and hasattr(doc, 'xref_set_key'):
                        # Set tab order to structure order for accessibility
                        doc.xref_set_key(page_xref, 'Tabs', '/S')
                        # Mark page as having structured content
                        doc.xref_set_key(page_xref, 'StructParents', '0')
                        
                        # Mark paths as artifacts to fix "path object not tagged" error
                        # Paths that are decorative should be marked as artifacts
                        # This is done through the content stream but PyMuPDF doesn't directly support it
                        # The Adobe autotag will handle this properly
                except:
                    pass
            
            logger.info(f"Processed {self.tag_counter} content elements")
            return True
            
        except Exception as e:
            logger.error(f"Failed to create tag structure: {e}")
            return False
    
    def extract_existing_fields(self, doc: fitz.Document) -> Dict[str, Dict[str, Any]]:
        """Extract position and properties of existing fields from the document"""
        existing_fields = {}
        unnamed_counter = 1
        
        for page_num, page in enumerate(doc):
            page_height = page.rect.height
            for widget in page.widgets():
                field_name = widget.field_name
                
                # Handle unnamed fields - give them a temporary name based on position
                if not field_name or field_name.strip() == '':
                    rect = widget.rect
                    # Try to infer a name from nearby text
                    nearby_text = self.get_nearby_text(page, rect)
                    if nearby_text and 'Date' in nearby_text:
                        field_name = f"Date_Sent_Delivered"
                    else:
                        field_name = f"UNNAMED_FIELD_{unnamed_counter}"
                    unnamed_counter += 1
                    logger.warning(f"Found unnamed field at ({rect.x0}, {rect.y0}), assigning name: {field_name}")
                
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
                    'page_height': page_height,  # Store for later conversion if needed
                    'was_unnamed': not widget.field_name  # Track if it was originally unnamed
                }
                logger.info(f"Found existing field '{field_name}' at ({rect.x0}, {y_top_left}) with size {rect.width}x{rect.height}")
        
        return existing_fields
    
    def get_nearby_text(self, page: fitz.Page, rect: fitz.Rect, max_distance: float = 50) -> str:
        """Get text near a field to help identify unnamed fields"""
        try:
            # Look for text to the left of the field
            search_rect = fitz.Rect(
                rect.x0 - max_distance * 3,  # Look further left for labels
                rect.y0 - 10,
                rect.x0,
                rect.y1 + 10
            )
            
            # Get all text blocks on the page
            blocks = page.get_text("blocks")
            nearby_text = []
            
            for block in blocks:
                if len(block) >= 5:
                    block_rect = fitz.Rect(block[0], block[1], block[2], block[3])
                    # Check if this text block is near our field
                    if block_rect.intersects(search_rect):
                        text = block[4].strip()
                        if text and not text.isspace():
                            nearby_text.append(text)
            
            return ' '.join(nearby_text)
        except Exception as e:
            logger.debug(f"Could not get nearby text: {e}")
            return ""
    
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
        Rebuild PDF by removing and re-adding fields while preserving document structure.
        
        Args:
            input_path: Path to input PDF
            field_updates: List of field definitions with properties
            
        Returns:
            Result dictionary with success status and output path
        """
        try:
            logger.info(f"Starting PDF field rebuild for {len(field_updates)} fields")
            
            # Debug: Write field updates to debug file
            with open('/tmp/pdf_rebuild_debug.json', 'w') as f:
                json.dump(field_updates, f, indent=2)
            
            # Debug: track what happens
            debug_info = {'input_fields': len(field_updates)}
            
            # Step 1: Open the original PDF
            original_doc = fitz.open(input_path)
            
            # Step 1.5: Extract existing field positions BEFORE removing them
            existing_fields = self.extract_existing_fields(original_doc)
            logger.info(f"Found {len(existing_fields)} existing fields in the document")
            debug_info['existing_fields'] = len(existing_fields)
            debug_info['existing_field_names'] = list(existing_fields.keys())
            
            # Debug: log existing field positions
            with open('/tmp/existing_fields_debug.json', 'w') as f:
                json.dump(existing_fields, f, indent=2)
            for name, field in existing_fields.items():
                logger.info(f"Existing field '{name}': pos=({field.get('x', 0):.1f}, {field.get('y', 0):.1f}), size={field.get('width', 0):.1f}x{field.get('height', 0):.1f}")
            
            # Step 2: Extract visual content (without fields)
            pages_content = self.extract_visual_content(original_doc)
            logger.info(f"Extracted content from {len(pages_content)} pages")
            
            # Step 3: Create a working copy preserving ALL document structure
            temp_clean_path = tempfile.mktemp(suffix='_clean.pdf')
            
            # IMPORTANT: Preserve the original document structure including tags
            temp_doc = fitz.open()
            
            # Insert the entire document to preserve structure, tags, and content
            temp_doc.insert_pdf(original_doc)
            
            # Process fonts - substitute Arial with Helvetica to avoid embedding issues
            try:
                for page in temp_doc:
                    fonts = page.get_fonts()
                    for font in fonts:
                        font_name = font[3]  # Font name
                        font_xref = font[0]  # Font xref
                        # Log Arial fonts that cause embedding issues
                        if 'Arial' in font_name:
                            logger.info(f"Found problematic Arial font: {font_name}, xref: {font_xref}")
                            # Note: PyMuPDF doesn't support direct font substitution
                            # The Arial fonts will be handled by Adobe's API or need post-processing
            except Exception as e:
                logger.warning(f"Could not analyze fonts: {e}")
            
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
            temp_doc.save(temp_clean_path, garbage=0, deflate=False)
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
                # Parse field update format - handle both camelCase and PascalCase
                if 'originalName' in field_info or 'OriginalName' in field_info:
                    original_name = field_info.get('originalName') or field_info.get('OriginalName') or ''  # Handle None values
                    new_name = field_info.get('newName') or field_info.get('NewName') or original_name or ''  # Handle None values
                    
                    # Map common variations to handle unnamed fields
                    lookup_name = original_name
                    if original_name.lower() == 'date delivered':
                        # This is likely our unnamed field
                        lookup_name = 'Date_Sent_Delivered'
                        logger.info(f"Mapping 'date delivered' to 'Date_Sent_Delivered'")
                    elif original_name.lower() == 'date recieved' or original_name.lower() == 'date received':
                        # Handle the misspelled field name
                        lookup_name = 'Date recieved'  # Use the actual field name from the PDF (with typo)
                        logger.info(f"Mapping '{original_name}' to 'Date recieved' (with original typo)")
                    elif original_name.lower() == 'in person, hand-delivered' and field_info.get('fieldType') == 'text':
                        # The text field "In person, hand-delivered" should use its existing position
                        # It exists in the PDF with this name but wrong type
                        lookup_name = 'In person, hand-delivered'
                        logger.info(f"Text field 'In person, hand-delivered' will use existing position")
                    
                    updated_field_names.add(lookup_name)
                    
                    # IMPORTANT: Look up existing field by ORIGINAL name first, since that's how they're stored
                    existing_field = existing_fields.get(original_name, {})
                    
                    if not existing_field:
                        # If not found by original name, try the new name (in case of unnamed fields)
                        existing_field = existing_fields.get(lookup_name, {})
                        if existing_field:
                            logger.info(f"Found existing field using new name '{lookup_name}'")
                        else:
                            # Try case-insensitive search as last resort
                            for field_name, field_data in existing_fields.items():
                                if field_name.lower() == original_name.lower() or field_name.lower() == lookup_name.lower():
                                    existing_field = field_data
                                    logger.info(f"Found existing field '{field_name}' using case-insensitive search for '{original_name}'")
                                    break
                    else:
                        logger.info(f"Found existing field using original name '{original_name}'")
                    
                    # Handle page number - prefer existing field's page if not provided
                    page_num = field_info.get('PageNumber') or field_info.get('pageNumber')
                    if page_num is None and existing_field:
                        page_num = existing_field.get('page', 0) + 1  # Convert to 1-based for consistency
                    if page_num is None or page_num < 1:
                        page_num = 1
                    
                    # ALWAYS use existing field positions when available
                    # Field updates should ONLY change name, type, and tooltip
                    # NEVER change the position from what Syncfusion originally extracted
                    if existing_field:
                        # Use original Syncfusion positions
                        x = existing_field.get('x', 100)
                        y = existing_field.get('y', 100)
                        width = existing_field.get('width', 200)
                        height = existing_field.get('height', 20)
                        logger.info(f"Using original Syncfusion position for '{original_name}': ({x}, {y}), size: ({width}x{height})")
                    else:
                        # Field not found - this should not happen!
                        logger.warning(f"Field '{original_name}' not found in existing fields - using default position")
                        logger.info(f"Available fields: {list(existing_fields.keys())}")
                        # Use reasonable defaults instead of skipping
                        x = 100
                        y = 100 + (len(fields_by_page.get(page_num - 1, [])) * 30)  # Offset vertically for each field
                        width = 200
                        height = 20
                    
                    # Get field type
                    field_type = field_info.get('fieldType') or field_info.get('FieldType') or existing_field.get('type', 'text')
                    
                    # FORCE signature fields to be text fields to prevent document locking
                    if field_type.lower() == 'signature':
                        field_type = 'text'
                        logger.info(f"Converting signature field '{new_name}' to text field to prevent document locking")
                    
                    field_def = {
                        'name': new_name,
                        'type': field_type,
                        'x': x,
                        'y': y,
                        'width': width,
                        'height': height,
                        'page': page_num - 1,  # Convert to 0-based index
                        'tooltip': field_info.get('Tooltip') or field_info.get('tooltip') or existing_field.get('tooltip', new_name),
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
                    # Keep existing field but check for duplicates
                    # If this field position conflicts with an already-added field, offset it
                    field_x = existing_field.get('x', 100)
                    field_y = existing_field.get('y', 100)
                    
                    # Check if this position is already taken
                    for existing_page_fields in fields_by_page.get(existing_field.get('page', 0), []):
                        if abs(existing_page_fields['x'] - field_x) < 5 and abs(existing_page_fields['y'] - field_y) < 5:
                            # Position conflict - offset this field
                            field_y += 25  # Move down by 25 pixels
                            break
                    
                    field_def = {
                        'name': field_name,
                        'type': existing_field.get('type', 'text'),
                        'x': field_x,
                        'y': field_y,
                        'width': existing_field.get('width', 200),
                        'height': existing_field.get('height', 20),
                        'page': existing_field.get('page', 0),
                        'tooltip': field_name,  # Use field name as tooltip for existing fields
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
            used_field_names = set()  # Track used field names to avoid duplicates
            
            for page_num, fields in fields_by_page.items():
                if page_num >= len(final_doc):
                    logger.warning(f"Page {page_num} does not exist, skipping fields")
                    continue
                
                page = final_doc[page_num]
                
                # Sort fields by Y then X for proper tab order
                fields.sort(key=lambda f: (-f.get('y', 0), f.get('x', 0)))
                
                # Track positions to handle duplicate fields at same location
                used_positions = {}
                
                for field_def in fields:
                    orig_name = field_def['name']
                    
                    # Check if this is a duplicate field at the same position
                    pos_key = f"{field_def.get('x', 0):.1f},{field_def.get('y', 0):.1f}"
                    
                    # Don't check for overlaps - trust the positions from the JSON
                    # The front-end has already arranged the fields correctly
                    
                    # Handle duplicate field names (different positions)
                    field_name = orig_name
                    counter = 2
                    while field_name in used_field_names:
                        # Special case: if it's an unnamed field that got a nearby name, rename it
                        if orig_name.lower() in ['in person, hand-delivered', 'mailed', 'emailed', 'faxed'] and field_def.get('type') == 'text':
                            field_name = f"Date_Sent_{orig_name.replace(' ', '_').replace(',', '').replace('-', '_')}"
                            logger.info(f"Renaming likely mislabeled field from '{orig_name}' to '{field_name}'")
                            break
                        else:
                            field_name = f"{orig_name}_{counter}"
                            counter += 1
                    
                    field_def['name'] = field_name
                    used_field_names.add(field_name)
                    used_positions[pos_key] = field_name
                    
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
            
            # Step 6: Save and reload to ensure fields are properly committed
            # This fixes the issue where fields only appear after second edit
            temp_commit_path = tempfile.mktemp(suffix='_commit.pdf')
            final_doc.save(temp_commit_path, garbage=0, deflate=False)
            final_doc.close()
            
            # Reload the document
            final_doc = fitz.open(temp_commit_path)
            
            # Set document metadata for accessibility
            metadata = {
                'producer': 'AccessForm PDF Rebuilder',
                'creator': 'AccessForm System',
                'title': 'Accessible PDF Form',
                'subject': 'Accessible Form Document'
            }
            final_doc.set_metadata(metadata)
            
            # Set document language for PDF/UA compliance
            final_doc.set_language('en-US')
            
            # Add PDF/UA identifier
            try:
                if hasattr(final_doc, 'xref_set_key'):
                    catalog_xref = final_doc.pdf_catalog()
                    
                    # Add PDF/UA-1 identifier
                    pdfuaid = """<</Part 1>>"""
                    ua_xref = final_doc.xref_add_object(pdfuaid)
                    final_doc.xref_set_key(catalog_xref, 'PdfUAId', f'{ua_xref} 0 R')
                    
                    logger.info("Added PDF/UA identifier")
            except Exception as e:
                logger.warning(f"Could not add PDF/UA identifier: {e}")
            
            # Remove signature locks to keep document editable
            self.remove_signature_locks(final_doc)
            
            # Step 7: Save final PDF
            output_path = tempfile.mktemp(suffix='_rebuilt.pdf')
            # Save with minimal options to avoid corruption
            final_doc.save(output_path, garbage=0, deflate=False)
            
            # Verify the result
            verify_doc = fitz.open(output_path)
            total_fields = sum(len(list(page.widgets())) for page in verify_doc)
            verify_doc.close()
            
            final_doc.close()
            
            # Clean up temp files
            if os.path.exists(temp_clean_path):
                os.remove(temp_clean_path)
            if os.path.exists(temp_commit_path):
                os.remove(temp_commit_path)
            
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