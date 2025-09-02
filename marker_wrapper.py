#!/usr/bin/env python3
"""
Wrapper script to use Marker for PDF to Markdown conversion
"""
import sys
import os
import tempfile
import json
from pathlib import Path

def convert_pdf_to_markdown(pdf_path):
    """
    Convert a PDF to markdown using Marker
    """
    try:
        # Import marker components
        from marker.single import convert_single_pdf
        from marker.models import create_model_dict
        from marker.config import Config
        
        # Create configuration
        config = Config()
        config.disable_ocr = True  # We already have text from PDF
        config.extract_images = False  # Don't extract images for now
        
        # Create models (will download if needed)
        model_dict = create_model_dict()
        
        # Convert the PDF
        output_dir = tempfile.mkdtemp()
        markdown_path, images, metadata = convert_single_pdf(
            pdf_path,
            model_dict,
            output_dir,
            config=config
        )
        
        # Read the markdown
        with open(markdown_path, 'r', encoding='utf-8') as f:
            markdown_content = f.read()
        
        # Extract additional metadata if available
        extra_info = {}
        if metadata:
            # Try to extract useful information from metadata
            if hasattr(metadata, 'pages'):
                extra_info['page_count'] = len(metadata.pages)
                extra_info['pages'] = []
                for page in metadata.pages:
                    page_info = {}
                    if hasattr(page, 'blocks'):
                        page_info['blocks'] = []
                        for block in page.blocks:
                            block_info = {}
                            if hasattr(block, 'bbox'):
                                block_info['bbox'] = block.bbox
                            if hasattr(block, 'text'):
                                block_info['text'] = str(block.text)[:100]  # First 100 chars
                            if hasattr(block, 'block_type'):
                                block_info['type'] = block.block_type
                            page_info['blocks'].append(block_info)
                    extra_info['pages'].append(page_info)
        
        # Check for any form field detection in metadata
        if metadata and hasattr(metadata, 'form_fields'):
            extra_info['form_fields'] = metadata.form_fields
        
        return {
            "success": True,
            "markdown": markdown_content,
            "metadata": extra_info,
            "error": None
        }
        
    except Exception as e:
        return {
            "success": False,
            "markdown": None,
            "error": str(e)
        }

def main():
    if len(sys.argv) != 2:
        print(json.dumps({
            "success": False,
            "error": "Usage: marker_wrapper.py <pdf_path>"
        }))
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    
    if not os.path.exists(pdf_path):
        print(json.dumps({
            "success": False,
            "error": f"PDF file not found: {pdf_path}"
        }))
        sys.exit(1)
    
    result = convert_pdf_to_markdown(pdf_path)
    print(json.dumps(result))

if __name__ == "__main__":
    main()