#!/usr/bin/env python3
"""
Core PDF utilities for pikepdf operations.
Single responsibility: Basic PDF I/O and validation.
"""

import pikepdf
import logging
from pathlib import Path
from typing import Optional, Dict, Any

logger = logging.getLogger(__name__)


class PdfUtils:
    """Basic PDF operations - open, save, validate."""

    @staticmethod
    def open_pdf(input_path: str) -> pikepdf.Pdf:
        """
        Open a PDF file.

        Args:
            input_path: Path to input PDF

        Returns:
            Opened pikepdf.Pdf object

        Raises:
            FileNotFoundError: If PDF doesn't exist
            pikepdf.PdfError: If PDF is corrupted
        """
        logger.info(f"[PDF-UTILS] Opening PDF: {input_path}")

        if not Path(input_path).exists():
            raise FileNotFoundError(f"PDF not found: {input_path}")

        try:
            pdf = pikepdf.open(input_path)
            page_count = len(pdf.pages)
            logger.info(f"[PDF-UTILS] Successfully opened PDF with {page_count} pages")
            return pdf
        except Exception as e:
            logger.error(f"[PDF-UTILS] Failed to open PDF: {e}")
            raise

    @staticmethod
    def save_pdf(pdf: pikepdf.Pdf, output_path: str) -> None:
        """
        Save a PDF file.

        Args:
            pdf: pikepdf.Pdf object to save
            output_path: Path to save PDF

        Raises:
            OSError: If save fails
        """
        logger.info(f"[PDF-UTILS] Saving PDF to: {output_path}")

        try:
            pdf.save(output_path)
            file_size = Path(output_path).stat().st_size
            logger.info(f"[PDF-UTILS] Successfully saved PDF ({file_size} bytes)")
        except Exception as e:
            logger.error(f"[PDF-UTILS] Failed to save PDF: {e}")
            raise

    @staticmethod
    def validate_structure_tree(pdf: pikepdf.Pdf) -> Dict[str, Any]:
        """
        Validate structure tree existence and basic properties.

        Args:
            pdf: pikepdf.Pdf object to validate

        Returns:
            Dictionary with validation results:
            {
                'has_struct_tree': bool,
                'has_kids': bool,
                'kid_count': int,
                'valid': bool
            }
        """
        logger.info("[PDF-UTILS] Validating structure tree")

        result = {
            'has_struct_tree': False,
            'has_kids': False,
            'kid_count': 0,
            'valid': False
        }

        # Check for StructTreeRoot
        if '/StructTreeRoot' not in pdf.Root:
            logger.warning("[PDF-UTILS] No StructTreeRoot found")
            return result

        result['has_struct_tree'] = True
        struct_root = pdf.Root.StructTreeRoot

        # Check for kids
        if '/K' not in struct_root:
            logger.warning("[PDF-UTILS] StructTreeRoot has no /K (kids)")
            return result

        result['has_kids'] = True

        # Count kids
        kids = struct_root.K
        if isinstance(kids, list):
            result['kid_count'] = len(kids)
        else:
            result['kid_count'] = 1

        result['valid'] = result['has_kids'] and result['kid_count'] > 0

        logger.info(f"[PDF-UTILS] Structure tree validation: {result}")
        return result

    @staticmethod
    def get_pdf_info(pdf: pikepdf.Pdf) -> Dict[str, Any]:
        """
        Get basic PDF information for debugging.

        Args:
            pdf: pikepdf.Pdf object

        Returns:
            Dictionary with PDF info
        """
        return {
            'page_count': len(pdf.pages),
            'pdf_version': str(pdf.pdf_version),
            'has_struct_tree': '/StructTreeRoot' in pdf.Root,
            'has_metadata': '/Metadata' in pdf.Root,
        }


# Standalone test function
def test_pdf_utils():
    """Test PDF utilities with a sample PDF."""
    logging.basicConfig(level=logging.INFO)

    # Test would go here
    logger.info("[PDF-UTILS-TEST] Module loaded successfully")


if __name__ == '__main__':
    test_pdf_utils()
