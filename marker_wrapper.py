#!/usr/bin/env python3
"""
Wrapper script to use Marker v1.10.0+ for PDF to Markdown conversion
"""
import sys
import os
import json
from pathlib import Path

def convert_pdf_to_markdown(pdf_path):
    """
    Convert a PDF to markdown using Marker v1.10.0+
    """
    try:
        # Import marker v1.10.0+ components
        from marker.converters.pdf import PdfConverter
        from marker.models import create_model_dict
        from marker.output import text_from_rendered
        from marker.config.parser import ConfigParser

        # Create configuration
        config = {
            "output_format": "markdown",
            # Disable image extraction for now (can be enabled later)
            "disable_image_extraction": True,
        }
        config_parser = ConfigParser(config)

        # Create converter with models
        converter = PdfConverter(
            config=config_parser.generate_config_dict(),
            artifact_dict=create_model_dict(),
            processor_list=config_parser.get_processors(),
            renderer=config_parser.get_renderer(),
        )

        # Convert the PDF
        rendered = converter(pdf_path)

        # Extract text from rendered output
        markdown_content, metadata_dict, images = text_from_rendered(rendered)

        # Extract additional metadata if available
        extra_info = {}
        if metadata_dict:
            extra_info = metadata_dict

        return {
            "success": True,
            "markdown": markdown_content,
            "metadata": extra_info,
            "error": None
        }

    except Exception as e:
        import traceback
        return {
            "success": False,
            "markdown": None,
            "error": f"{str(e)}\n{traceback.format_exc()}"
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
