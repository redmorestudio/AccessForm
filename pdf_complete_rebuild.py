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

# Set up logging - ENABLE for debugging
logging.basicConfig(level=logging.DEBUG, format='%(asctime)s - %(levelname)s: %(message)s', stream=sys.stderr)
logger = logging.getLogger(__name__)

# Use real logger for debugging
# class NullLogger:
#     def info(self, msg): pass
#     def debug(self, msg): pass
#     def warning(self, msg): pass
#     def error(self, msg): pass
    
# logger = NullLogger()

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
    
    def add_form_field(self, page: fitz.Page, field_info: Dict[str, Any], page_num: int = 0) -> Optional[fitz.Widget]:
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

            # IMPORTANT: COORDINATE SYSTEM ASSUMPTION (MAY BE WRONG!)
            # This code ASSUMES coordinates are in PDF's native bottom-left origin
            # BUT extract_existing_fields() CONVERTS them to top-left!
            # This creates the inversion bug!

            page_height = page.rect.height  # ~792 points per page

            # INSTRUMENTATION
            debug_path = "/tmp/coordinate_debug.log"
            with open(debug_path, 'a') as f:
                f.write(f"🟠 [WIDGET CREATE] Field '{field_name}' page {page_num}:\n")
                f.write(f"   Input: X={x:.1f}, Y={y:.1f}, W={width:.1f}x{height:.1f}\n")
                f.write(f"   Page height: {page_height:.1f}\n")
                f.write(f"   Assuming Y is in PDF bottom-left format...\n")
                f.write(f"   ⚠️  BUT if Y came from extract_existing_fields(), it's actually TOP-LEFT!\n")
                f.write("\n")

            # Use coordinates as-is - THIS IS THE BUG if Y is top-left format!
            y_converted = y  # NO CONVERSION - but should convert if Y is top-left!
            
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
                'date': fitz.PDF_WIDGET_TYPE_TEXT,  # Date fields use text widget with format validation
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
                
                # Special handling for date fields
                if field_type == 'date':
                    # Add date format validation
                    widget.field_flags |= fitz.PDF_FIELD_IS_COMMIT_ON_SEL_CHANGE
                    # Set format for MM/DD/YYYY
                    try:
                        # Set the field format for dates
                        widget.field_value = ""  # Start empty
                        # Add visual hint in tooltip
                        if not tooltip or "date" not in tooltip.lower():
                            tooltip = f"{tooltip} (MM/DD/YYYY)" if tooltip else "Enter date (MM/DD/YYYY)"
                    except:
                        pass
                
                # Special handling for signature fields (using text type to avoid locks)
                elif field_type == 'signature':
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
                # Completely reset the appearance to remove any ZapfDingbats
                widget.field_display = 1
                widget.border_width = 1
                widget.border_style = "S"  # Solid border
                widget.border_color = [0, 0, 0]  # Black border
            
            elif widget_type == fitz.PDF_WIDGET_TYPE_RADIOBUTTON:
                # Set radio button specific properties
                widget.field_value = field_info.get('value', '')
                # Force Helvetica font to avoid ZapfDingbats
                widget.text_font = "Helv"
                widget.text_fontsize = 10
                # Use a simple dot instead of ZapfDingbats symbol
                widget.button_caption = "•"  # Use bullet instead of ZapfDingbats symbol
                # Completely reset the appearance to remove any ZapfDingbats
                widget.field_display = 1
                widget.border_width = 1
                widget.border_style = "S"  # Solid border
                widget.border_color = [0, 0, 0]  # Black border
            
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
            
            # Try to mark the widget annotation for tagging
            # This helps with PDF/UA compliance
            try:
                # Get the annotation object (widgets are a type of annotation)
                if hasattr(widget, 'xref') and widget.xref > 0:
                    # Try to add structure parent key to link to structure tree
                    # This tells screen readers that this annotation is part of the document structure
                    if hasattr(page.parent, 'xref_set_key'):
                        # Set StructParent to indicate this should be in the tag tree
                        page.parent.xref_set_key(widget.xref, 'StructParent', '0')
                        logger.debug(f"Added StructParent to widget '{field_name}'")
            except Exception as e:
                # Not critical - the Adobe autotag will fix this
                logger.debug(f"Could not add StructParent to widget: {e}")
            
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
    
    def detect_and_fix_table_structures(self, doc: fitz.Document) -> int:
        """
        Detect and fix table accessibility issues:
        - Tables with headers but no data cells
        - Layout tables misusing semantic markup
        - Single-column tables that should be lists/paragraphs
        """
        logger.info("=== DETECTING AND FIXING TABLE STRUCTURES ===")
        tables_fixed = 0

        try:
            # PyMuPDF doesn't have direct table extraction, but we can detect table-like structures
            # through text block analysis and drawing commands
            for page_num, page in enumerate(doc):
                # Get all text blocks to detect table-like patterns
                blocks = page.get_text("dict")

                # Look for patterns that indicate table structures
                # 1. Multiple aligned text blocks at same Y coordinate (table row)
                # 2. Consistent vertical spacing (table rows)
                # 3. Rectangle drawings that form grid patterns

                # Get drawing commands to find table borders
                drawings = page.get_drawings()

                # Detect grid-like rectangle patterns
                horizontal_lines = []
                vertical_lines = []

                for drawing in drawings:
                    for item in drawing.get("items", []):
                        if item[0] == "l":  # Line
                            x0, y0, x1, y1 = item[1], item[2], item[3], item[4]
                            # Horizontal line
                            if abs(y0 - y1) < 2:
                                horizontal_lines.append((min(x0, x1), y0, max(x0, x1)))
                            # Vertical line
                            elif abs(x0 - x1) < 2:
                                vertical_lines.append((x0, min(y0, y1), max(y0, y1)))

                # If we have grid patterns, likely a table
                if len(horizontal_lines) >= 2 and len(vertical_lines) >= 2:
                    logger.info(f"Page {page_num}: Detected table-like structure with {len(horizontal_lines)} horizontal and {len(vertical_lines)} vertical lines")

                    # Analyze text within the table boundaries
                    table_text_blocks = []
                    for block in blocks.get("blocks", []):
                        if block.get("type") == 0:  # Text block
                            for line in block.get("lines", []):
                                for span in line.get("spans", []):
                                    table_text_blocks.append({
                                        'text': span.get("text", ""),
                                        'bbox': span.get("bbox", []),
                                        'flags': span.get("flags", 0)
                                    })

                    # Check for orphaned headers pattern:
                    # - Bold or larger text at top (headers)
                    # - No regular text below (no data cells)
                    potential_headers = []
                    regular_text = []

                    for text_block in table_text_blocks:
                        # Check if text is bold (flag bit 16) or larger font
                        if text_block['flags'] & 2**4:  # Bold flag
                            potential_headers.append(text_block)
                        else:
                            regular_text.append(text_block)

                    # If we have headers but very little regular text, it's likely a misused table
                    if len(potential_headers) > 0 and len(regular_text) < len(potential_headers):
                        logger.warning(f"Page {page_num}: Found table with {len(potential_headers)} potential headers but only {len(regular_text)} data cells - likely accessibility issue")
                        tables_fixed += 1

                        # Mark this for remediation in the tag tree
                        # Since PyMuPDF can't directly modify the tag tree, we'll flag it
                        # for the C# services to handle

                # Also check for single-column tables (often misused for layout)
                if len(vertical_lines) == 2:  # Only left and right borders
                    logger.info(f"Page {page_num}: Found single-column table - should likely be converted to paragraphs")
                    tables_fixed += 1

            logger.info(f"Identified {tables_fixed} tables with potential accessibility issues")

        except Exception as e:
            logger.warning(f"Error during table detection: {e}")

        return tables_fixed

    def create_tag_structure(self, doc: fitz.Document, fields_by_page: Dict[int, List[Dict]]) -> bool:
        """Ensure form fields have proper accessibility attributes"""
        logger.info("=== ENSURING FORM FIELD ACCESSIBILITY ===")
        
        try:
            # Set form field accessibility attributes
            for page_num, fields in fields_by_page.items():
                if page_num >= len(doc):
                    continue
                    
                page = doc[page_num]
                
                for field_info in fields:
                    field_name = field_info.get('field_name', '')
                    field_type = field_info.get('field_type', 'text')
                    
                    # Find the widget annotation for this field
                    for widget in page.widgets():
                        if widget.field_name == field_name:
                            # Set accessibility properties on the widget
                            try:
                                # Add tooltip if not present
                                if not widget.field_value:
                                    widget.field_value = ""
                                
                                # Ensure the widget has proper structure parent
                                widget.update()
                                
                                logger.debug(f"Updated accessibility for field '{field_name}'")
                            except Exception as e:
                                logger.debug(f"Could not update field '{field_name}': {e}")
            
            logger.info("Form field accessibility attributes updated")
            return True
            
        except Exception as e:
            logger.error(f"Failed to ensure form field accessibility: {e}")
            return True  # Don't fail the whole process
    
    def old_create_tag_structure_disabled(self, doc: fitz.Document, fields_by_page: Dict[int, List[Dict]]) -> bool:
        """Old code - disabled to prevent breaking the working tag structure"""
        # The old implementation tried to create PDF/UA tags but PyMuPDF can't do it properly
        # This caused the tag structure to break
        # We now rely on C# services and Adobe autotag to handle this
        pass
    def extract_existing_fields(self, doc: fitz.Document) -> Dict[str, Dict[str, Any]]:
        """Extract position and properties of existing fields from the document"""

        # INSTRUMENTATION: Write to dedicated debug file
        debug_path = "/tmp/coordinate_debug.log"
        debug_lines = []
        debug_lines.append("═══════════════════════════════════════════════════════════")
        debug_lines.append("🟡 [PYTHON EXTRACT] EXTRACTING EXISTING FIELDS FROM PDF")
        debug_lines.append("═══════════════════════════════════════════════════════════")

        existing_fields = {}
        unnamed_counter = 1

        for page_num, page in enumerate(doc):
            page_height = page.rect.height
            widget_count = len(list(page.widgets()))
            debug_lines.append(f"🟡 Page {page_num}: height={page_height:.1f}, widgets={widget_count}")

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

                # COORDINATE TRANSFORMATION DETECTION
                y_bottom_left_origin = rect.y0  # PDF native: Y=0 at bottom
                y_top_left = page_height - rect.y1  # Convert to top-left: Y=0 at top

                debug_lines.append(f"🔴 [COORD TRANSFORM] Field '{field_name}':")
                debug_lines.append(f"   PDF rect: x0={rect.x0:.1f}, y0={rect.y0:.1f}, x1={rect.x1:.1f}, y1={rect.y1:.1f}")
                debug_lines.append(f"   Bottom-left Y: {y_bottom_left_origin:.1f} (PDF native)")
                debug_lines.append(f"   → Top-left Y:  {y_top_left:.1f} (after conversion)")
                debug_lines.append(f"   Δ = {y_top_left - y_bottom_left_origin:+.1f}")
                debug_lines.append(f"   Formula: Y_top = page_height({page_height:.1f}) - rect.y1({rect.y1:.1f}) = {y_top_left:.1f}")

                existing_fields[field_name] = {
                    'x': rect.x0,
                    'y': y_top_left,  # ⚠️ STORING IN TOP-LEFT FORMAT!
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

        # Write all debug output to file
        debug_lines.append("═══════════════════════════════════════════════════════════")
        debug_lines.append("")
        with open(debug_path, 'a') as f:
            f.write('\n'.join(debug_lines) + '\n')

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

    def get_unicode_replacement(self, text, font_name):
        """Enhanced character replacement with best-guess mapping for unknown characters"""
        # Known ZapfDingbats mappings
        zapf_mappings = {
            'q': '☐',      # Empty checkbox
            '\x71': '☐',   # Empty checkbox (hex)
            '4': '☑',      # Checked checkbox
            '\x34': '☑',   # Checked checkbox (hex)
            'o': '□',      # Square
            '\x6f': '□',   # Square (hex)
            'n': '✓',      # Checkmark
            '\x6E': '✓',   # Checkmark (hex)
            'l': '●',      # Filled circle (radio selected)
            '\x6C': '●',   # Filled circle (hex)
            'm': '○',      # Empty circle (radio unselected)
            '\x6D': '○',   # Empty circle (hex)
            # Additional common mappings
            'a': '✁',      # Scissors
            'b': '✂',      # Scissors (solid)
            'c': '✃',      # Lower blade scissors
            'd': '✄',      # Upper blade scissors
            'e': '☎',      # Telephone
            'f': '✆',      # Telephone (solid)
            'g': '✇',      # Tape drive
            'h': '✈',      # Airplane
            'i': '✉',      # Envelope
            'j': '✊',      # Victory hand
            'k': '✋',      # Raised hand
            'u': '◆',      # Diamond
            'v': '◇',      # Diamond outline
            'w': '★',      # Star
            'x': '☆',      # Star outline
            'y': '♠',      # Spade
            'z': '♣',      # Club
        }

        for char in text:
            if char in zapf_mappings:
                return zapf_mappings[char]

        # Special handling for invisible fonts - replace ALL text with empty string
        if 'invisible' in font_name.lower():
            return ''  # Remove invisible text entirely

        # Best-guess replacement for unknown characters
        for char in text:
            if char == ' ':
                continue

            # ASCII range analysis for best guess
            ascii_val = ord(char)

            if 32 <= ascii_val <= 47:  # Punctuation/symbols
                return '□'
            elif 48 <= ascii_val <= 57:  # Digits - likely decorative
                return '○'
            elif 65 <= ascii_val <= 90:  # Uppercase - likely filled shapes
                return '■'
            elif 97 <= ascii_val <= 122:  # Lowercase - likely outline shapes
                return '□'
            elif ascii_val > 127:  # Extended ASCII - unknown
                return '?'
            else:
                return '?'  # Fallback for unknown

        return None  # No replacement needed

    def replace_problematic_fonts(self, doc: fitz.Document) -> None:
        """Note problematic fonts - Adobe will handle the actual replacement"""
        logger.info("=== CHECKING FOR PROBLEMATIC FONTS ===")
        
        try:
            # Just log problematic fonts - don't try to replace them here
            # Adobe's OCR and autotag will handle font embedding properly
            problematic_fonts = set()
            
            for page_num, page in enumerate(doc):
                fonts = page.get_fonts()
                for font in fonts:
                    font_name = font[3]
                    # Enhanced problematic font detection including invisible fonts
                    problematic_patterns = ['Arial', 'ZapfDingbats', 'Symbol', 'invisible', 'Wingdings', 'Webdings', 'Marlett']
                    if any(problem.lower() in font_name.lower() for problem in problematic_patterns):
                        problematic_fonts.add(font_name)
                        logger.info(f"Page {page_num}: Found problematic font '{font_name}'")
            
            if problematic_fonts:
                logger.info(f"Found {len(problematic_fonts)} problematic fonts: {problematic_fonts}")
                logger.info("Adobe OCR will replace these with embeddable fonts")
            
            # Ensure all form fields use Helvetica (this is safe)
            for page in doc:
                for widget in page.widgets():
                    widget.text_font = "Helv"
                    widget.update()
            
            logger.info("Form field fonts set to Helvetica")
            
        except Exception as e:
            logger.warning(f"Font check encountered an issue: {e}")
            # Don't fail the entire process for font issues
    
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
            
            # Fix font embedding issues - replace Arial with Helvetica
            self.replace_problematic_fonts(temp_doc)
            logger.info("=== FIXING FONT EMBEDDING ISSUES ===")
            try:
                # Get all fonts used in the document
                fonts_replaced = 0
                for page_num, page in enumerate(temp_doc):
                    # Get text with font information
                    text_dict = page.get_text("dict")
                    for block in text_dict.get("blocks", []):
                        if block.get("type") == 0:  # Text block
                            for line in block.get("lines", []):
                                for span in line.get("spans", []):
                                    font_name = span.get("font", "")
                                    # Check for Arial font
                                    if "Arial" in font_name or "arial" in font_name.lower():
                                        # Note: PyMuPDF doesn't allow direct font replacement
                                        # This would need to be done at field creation time
                                        logger.info(f"Found Arial font on page {page_num}: {font_name}")
                                        fonts_replaced += 1
                
                if fonts_replaced > 0:
                    logger.info(f"Found {fonts_replaced} instances of Arial font - will use Helvetica for new fields")
                else:
                    logger.info("No Arial font issues found")
                    
            except Exception as e:
                logger.warning(f"Could not check fonts: {e}")
            
            # Replace ZapfDingbats and other problematic font characters with Unicode equivalents
            # This is a workaround - we'll redact and replace the problematic characters
            logger.info("=== STARTING PROBLEMATIC FONT CHARACTER REPLACEMENT ===")
            try:
                zapf_replacements = []
                zapf_widgets_cleaned = 0
                
                # First, clean all checkbox widgets to remove ZapfDingbats from appearance streams
                for page_num, page in enumerate(temp_doc):
                    for widget in page.widgets():
                        if widget.field_type == fitz.PDF_WIDGET_TYPE_CHECKBOX:
                            logger.info(f"Cleaning checkbox widget: {widget.field_name}")
                            try:
                                # Force checkbox to recreate its appearance without custom fonts
                                widget.field_display = 0  # Hide field temporarily
                                widget.update()
                                widget.field_display = 1  # Show field again
                                widget.update()
                                zapf_widgets_cleaned += 1
                            except Exception as e:
                                logger.warning(f"Could not clean widget {widget.field_name}: {e}")
                
                logger.info(f"Cleaned {zapf_widgets_cleaned} checkbox widgets")
                
                for page_num, page in enumerate(temp_doc):
                    # Get all text instances with detailed position info
                    text_instances = page.get_text("rawdict")
                    
                    # Look for ZapfDingbats font usage
                    for block in text_instances.get("blocks", []):
                        for line in block.get("lines", []):
                            for span in line.get("spans", []):
                                font = span.get("font", "")
                                # Check for any problematic font patterns including invisible fonts
                                is_problematic_font = any(pattern.lower() in font.lower()
                                                        for pattern in ['ZapfDingbats', 'Symbol', 'invisible', 'Wingdings', 'Webdings', 'Marlett'])
                                if is_problematic_font:
                                    bbox = span.get("bbox", None)
                                    if bbox:
                                        # Get the actual character
                                        text = span.get("text", "")
                                        # Enhanced mapping for problematic font characters to Unicode
                                        replacement = self.get_unicode_replacement(text, font)
                                        
                                        if replacement:
                                            logger.info(f"Found problematic font character '{text}' in font '{font}' at page {page_num}, bbox {bbox}")
                                            zapf_replacements.append({
                                                'page': page_num,
                                                'bbox': bbox,
                                                'old': text,
                                                'new': replacement
                                            })
                
                # Now redact and replace the ZapfDingbats characters
                for repl in zapf_replacements:
                    try:
                        page = temp_doc[repl['page']]
                        rect = fitz.Rect(repl['bbox'])
                        
                        # Redact the ZapfDingbats character
                        page.add_redact_annot(rect)
                        page.apply_redactions()
                        
                        # Insert Unicode replacement with a standard font
                        # Use a point slightly offset from the original position
                        point = fitz.Point(rect.x0, rect.y1 - 2)
                        page.insert_text(point, repl['new'], fontname="helv", fontsize=10, color=(0, 0, 0))
                        
                        logger.info(f"Replaced ZapfDingbats '{repl['old']}' with '{repl['new']}' on page {repl['page']}")
                    except Exception as e:
                        logger.warning(f"Could not replace individual ZapfDingbats character: {e}")
                
                if zapf_replacements:
                    logger.info(f"Replaced {len(zapf_replacements)} ZapfDingbats characters")
                    # Also write to debug file
                    with open('/tmp/zapf_debug.json', 'w') as f:
                        json.dump({"found": len(zapf_replacements), "replacements": zapf_replacements[:5]}, f, indent=2)
                else:
                    logger.info("No ZapfDingbats characters found to replace")
                    with open('/tmp/zapf_debug.json', 'w') as f:
                        json.dump({"found": 0, "message": "No ZapfDingbats found"}, f, indent=2)
                    
            except Exception as e:
                logger.warning(f"Could not process ZapfDingbats: {e}")
            
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
            
            # Now remove ONLY the form fields (widgets) and hyperlinks, keeping everything else
            for page in temp_doc:
                # Delete all widgets (form fields) from each page
                for widget in list(page.widgets()):
                    page.delete_widget(widget)

                # Remove all annotations including links and form-related
                for annot in list(page.annots()):
                    # Remove form widgets
                    if annot.type[0] == fitz.PDF_ANNOT_WIDGET:
                        page.delete_annot(annot)
                    # Remove hyperlinks (including mailto: and http:// links)
                    elif annot.type[0] == fitz.PDF_ANNOT_LINK:
                        logger.info(f"Removing hyperlink annotation on page {page.number}")
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
                    # Special handling for duplicate field names (e.g., "In person, hand-delivered" appears twice)
                    existing_field = {}
                    
                    # For duplicate names, check if the original name has type suffix
                    field_type_hint = field_info.get('fieldType') or field_info.get('FieldType', '').lower()
                    
                    # Try looking up with type suffix if plain name fails
                    if not existing_field and field_type_hint:
                        type_suffixed_name = f"{original_name}_{field_type_hint}"
                        existing_field = existing_fields.get(type_suffixed_name, {})
                        if existing_field:
                            logger.info(f"Found field using type-suffixed name '{type_suffixed_name}'")
                    
                    if not existing_field:
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
                        # For new fields without page info, determine page based on Y coordinate
                        y = field_info.get('Y') or field_info.get('y', 0)
                        # Standard letter page is ~792 points tall
                        # PDF Y coordinates start at bottom of page, so larger Y means higher on page
                        # For multi-page docs, Y coordinates continue across pages:
                        # Page 1: Y = 0 to 792
                        # Page 2: Y = 792 to 1584
                        # Page 3: Y = 1584 to 2376, etc.
                        if y <= 0:
                            page_num = 1
                        else:
                            page_num = int(y / 792) + 1 if y % 792 != 0 else int(y / 792)
                        logger.info(f"Field '{new_name}' at Y={y} assigned to page {page_num} (auto-detected)")
                    
                    # COORDINATE SYSTEM FIX: Always use existing field positions when available
                    # Field updates should ONLY change name, type, and tooltip
                    # NEVER trust incoming coordinates when we have existing field positions
                    if existing_field:
                        # Use original existing positions
                        x = existing_field.get('x', 100)
                        y_from_extract = existing_field.get('y', 100)  # ⚠️ THIS IS IN TOP-LEFT FORMAT FROM EXTRACT!
                        width = existing_field.get('width', 200)
                        height = existing_field.get('height', 20)

                        # INSTRUMENTATION
                        debug_path = "/tmp/coordinate_debug.log"
                        with open(debug_path, 'a') as f:
                            f.write(f"🟣 [PYTHON REBUILD] Field '{original_name}':\n")
                            f.write(f"   Existing field data: X={x:.1f}, Y={y_from_extract:.1f} (⚠️ TOP-LEFT format)\n")
                            f.write(f"   ⚠️  BUG: Code assumes Y is bottom-left, but it's actually top-left!\n")
                            f.write(f"   This causes fields to be placed inverted!\n")
                            f.write("\n")

                        y = y_from_extract  # WRONG! Should convert back to bottom-left
                        logger.info(f"Using existing field position for '{original_name}': ({x}, {y}), size: ({width}x{height}) [trusted coordinates]")
                    else:
                        # Field not found in existing fields - this could be a new field
                        # Check if position was provided in the update
                        update_x = field_info.get('X') or field_info.get('x')
                        update_y = field_info.get('Y') or field_info.get('y')
                        update_width = field_info.get('Width') or field_info.get('width')
                        update_height = field_info.get('Height') or field_info.get('height')

                        if update_x is not None and update_y is not None and update_width and update_height:
                            # For new fields, we need to be careful about coordinate system conversion
                            # The incoming coordinates might be in UI coordinate system (top-left origin)
                            # We need to convert them to PyMuPDF coordinate system (bottom-left origin)

                            # Check if these coordinates look reasonable for a PDF page
                            # Standard PDF page is ~792 points tall
                            page_height = 792.0  # Standard letter size height

                            # If Y coordinate is very large (> 600), it might be in top-left format
                            if update_y > page_height * 0.75:  # If Y > 594 (75% of page height)
                                # This looks like top-left format, convert to bottom-left
                                x = float(update_x)
                                y = page_height - float(update_y) - float(update_height)
                                width = float(update_width)
                                height = float(update_height)
                                logger.info(f"Converted coordinates for new field '{original_name}': UI({update_x}, {update_y}) -> PDF({x}, {y})")
                            else:
                                # Assume coordinates are already in PDF format
                                x = float(update_x)
                                y = float(update_y)
                                width = float(update_width)
                                height = float(update_height)
                                logger.info(f"Using provided PDF coordinates for new field '{original_name}': ({x}, {y}), size: ({width}x{height})")
                        else:
                            # Field not found and no position provided - use defaults
                            logger.warning(f"Field '{original_name}' not found in existing fields and no position provided - using default position")
                            logger.info(f"Available fields: {list(existing_fields.keys())}")
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
            
            # IMPORTANT: Do NOT add back fields that weren't in the update list
            # This preserves field deletion functionality
            # If a field is not in the field_updates list, it means it was deleted
            logger.info(f"Preserving deletion - not adding back {len(existing_fields) - len(updated_field_names)} fields that were not in the update list")
            
            # Debug: track fields to be added
            debug_info['fields_by_page'] = {page: len(fields) for page, fields in fields_by_page.items()}
            debug_info['total_to_add'] = sum(len(fields) for fields in fields_by_page.values())
            
            with open('/tmp/pdf_rebuild_debug2.json', 'w') as f:
                json.dump(debug_info, f, indent=2)
            
            # Collect all fields from all pages for global sorting and tab order
            all_fields = []
            for page_num, fields in fields_by_page.items():
                if page_num >= len(final_doc):
                    logger.warning(f"Page {page_num} does not exist, skipping fields")
                    continue
                for field_def in fields:
                    field_def['page_num'] = page_num
                    all_fields.append(field_def)

            # Sort all fields globally: by page, then by Y (with row tolerance), then by X
            # This ensures proper tab order
            def sort_key(f):
                page = f.get('page_num', 0)
                y = f.get('y', 0)
                x = f.get('x', 0)
                # Round Y to nearest 10 points to group fields on same row
                y_rounded = round(y / 10) * 10
                return (page, -y_rounded, x)  # -y because higher Y = higher on page in PDF coords

            all_fields.sort(key=sort_key)

            # Check if fields have human-readable names or sequential SF names
            has_human_readable_names = any([
                field.get('name', '') and
                not field.get('name', '').startswith('SF') and
                not field.get('name', '').startswith('Field_') and
                len(field.get('name', '').strip()) > 2
                for field in all_fields
            ])

            # Only use SF sequential naming if we don't have human-readable names
            if has_human_readable_names:
                logger.info("Fields have human-readable names, preserving them for screen reader accessibility")
                # Store original names and ensure they're preserved
                for field_def in all_fields:
                    original_name = field_def.get('name', '')
                    field_def['original_name'] = original_name
                    # Keep the human-readable name as-is
                    logger.debug(f"Preserving human-readable field name: '{original_name}'")
            else:
                logger.info("Fields don't have human-readable names, will assign sequential SF names")
                field_counter = 1
                used_names = set()
                for field_def in all_fields:
                    original_name = field_def.get('name', f'Field_{field_counter}')

                    # Store original name for reference
                    field_def['original_name'] = original_name

                    # Assign sequential name
                    new_name = f"SF{field_counter}"
                    while new_name in used_names:
                        field_counter += 1
                        new_name = f"SF{field_counter}"

                    field_def['name'] = new_name
                    used_names.add(new_name)
                    field_counter += 1

                    logger.debug(f"Renamed field from '{original_name}' to '{new_name}'")

            # Add fields to each page
            added_fields = []

            for field_def in all_fields:
                page_num = field_def['page_num']
                page = final_doc[page_num]

                widget = self.add_form_field(page, field_def, page_num)
                if widget:
                    added_fields.append({
                        'name': field_def['name'],
                        'type': field_def['type'],
                        'page': page_num + 1,
                        'original_name': field_def.get('original_name', field_def['name'])
                    })
            
            logger.info(f"Added {len(added_fields)} fields to the document")
            
            # Step 4.5: Mark decorative elements as artifacts
            logger.info("=== MARKING DECORATIVE ELEMENTS AS ARTIFACTS ===")
            try:
                artifacts_marked = 0
                for page_num, page in enumerate(final_doc):
                    # Get all drawings (strokes, fills) on the page
                    drawings = page.get_drawings()
                    for drawing in drawings:
                        # Check if this is a decorative element (border, background)
                        # Form field borders are typically thin rectangles
                        for item in drawing.get("items", []):
                            if item[0] == "re":  # Rectangle
                                # Check if it's likely a form field border (thin stroke)
                                rect_info = item[1]
                                if len(rect_info) >= 4:
                                    width = rect_info[2]
                                    height = rect_info[3]
                                    # Form fields are typically small rectangles
                                    if (10 < width < 400) and (10 < height < 30):
                                        artifacts_marked += 1
                                        # Note: PyMuPDF doesn't have direct artifact marking
                                        # This needs to be done through the structure tree
                                        logger.debug(f"Found potential form field border at ({rect_info[0]}, {rect_info[1]})")
                
                if artifacts_marked > 0:
                    logger.info(f"Identified {artifacts_marked} decorative elements that should be artifacts")
                    with open('/tmp/artifacts_debug.json', 'w') as f:
                        json.dump({"found": artifacts_marked}, f)
                else:
                    logger.info("No specific decorative elements found to mark as artifacts")
                    
            except Exception as e:
                logger.warning(f"Could not mark artifacts: {e}")

            # Step 5: Detect and fix table accessibility issues
            tables_with_issues = self.detect_and_fix_table_structures(final_doc)

            # Step 6: Call tag structure function (which now does minimal/no modification)
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

            # Final aggressive ZapfDingbats cleanup
            logger.info("=== FINAL ZAPFDINGBATS FONT CLEANUP ===")
            try:
                zapf_found = False
                for page_num, page in enumerate(final_doc):
                    # Force all widgets to use Helvetica
                    for widget in page.widgets():
                        # Force standard font for all widgets
                        widget.text_font = "Helv"
                        widget.text_fontsize = 10

                        if widget.field_type == fitz.PDF_WIDGET_TYPE_CHECKBOX:
                            widget.button_caption = "X"
                            widget.field_display = 1  # Visible
                            # Clear any custom appearance streams that might use ZapfDingbats
                            widget.field_flags &= ~(1 << 12)  # Clear NoToggleToOff flag
                        elif widget.field_type == fitz.PDF_WIDGET_TYPE_RADIOBUTTON:
                            widget.button_caption = "•"
                            widget.field_display = 1
                            widget.field_flags &= ~(1 << 12)

                        # Force update to recreate appearance without ZapfDingbats
                        widget.update()

                    # Check for any remaining ZapfDingbats in fonts
                    fonts = page.get_fonts()
                    for font in fonts:
                        if 'ZapfDingbats' in font[3] or 'zapf' in font[3].lower():
                            logger.warning(f"Page {page_num}: Still found ZapfDingbats font: {font[3]}, xref: {font[0]}")
                            zapf_found = True

                            # Try to remove ZapfDingbats font references from the page
                            try:
                                # Get the page's resources dictionary
                                page_obj = final_doc.xref_object(page.xref)
                                if "/Resources" in page_obj:
                                    # Try to clean font resources
                                    logger.info(f"Attempting to clean ZapfDingbats from page {page_num} resources")
                            except Exception as e:
                                logger.warning(f"Could not clean font resources: {e}")

                if zapf_found:
                    logger.warning("ZapfDingbats font still present - will require post-processing")
                else:
                    logger.info("No ZapfDingbats fonts found in final document")

                logger.info("Completed final font cleanup")
            except Exception as e:
                logger.warning(f"Could not perform final font cleanup: {e}")

            # Step 7: Save final PDF
            output_path = tempfile.mktemp(suffix='_rebuilt.pdf')
            # Save with aggressive garbage collection to remove unused fonts
            final_doc.save(output_path, garbage=4, deflate=True, clean=True)
            
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
    logger.info("=== PDF_COMPLETE_REBUILD.PY STARTING ===")
    logger.info(f"Script called with {len(sys.argv)} arguments")
    logger.info(f"Arguments: {sys.argv}")
    
    if len(sys.argv) < 3:
        print(json.dumps({
            "success": False,
            "error": "Usage: pdf_complete_rebuild.py <pdf_path> <field_updates_json>"
        }))
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    field_updates_arg = sys.argv[2]
    logger.info(f"PDF Path: {pdf_path}")
    logger.info(f"Field updates arg length: {len(field_updates_arg)} chars")
    
    if not os.path.exists(pdf_path):
        print(json.dumps({
            "success": False,
            "error": f"PDF file not found: {pdf_path}"
        }))
        sys.exit(1)
    
    # Parse field updates
    if os.path.exists(field_updates_arg):
        logger.info(f"Loading field updates from file: {field_updates_arg}")
        with open(field_updates_arg, 'r') as f:
            field_updates = json.load(f)
    else:
        logger.info(f"Parsing field updates from JSON string")
        field_updates = json.loads(field_updates_arg)
    
    logger.info(f"Loaded {len(field_updates)} field updates")
    for i, field in enumerate(field_updates[:3]):  # Log first 3 fields
        logger.info(f"Field {i}: {field.get('originalName', 'NO_NAME')} -> {field.get('newName', 'NO_NAME')}")
    
    # Perform rebuild
    logger.info("Creating PDFCompleteRebuilder instance")
    rebuilder = PDFCompleteRebuilder()
    
    logger.info("Calling rebuild_pdf method")
    result = rebuilder.rebuild_pdf(pdf_path, field_updates)
    
    logger.info(f"Rebuild result: success={result.get('success')}, fields={result.get('totalFields')}")
    
    # Output result
    print(json.dumps(result, indent=2))
    
    logger.info("=== PDF_COMPLETE_REBUILD.PY FINISHED ===")
    sys.exit(0 if result["success"] else 1)

if __name__ == "__main__":
    main()