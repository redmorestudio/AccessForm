#!/usr/bin/env python3
"""
Structure tree cleaner - removes old structure tree.
Single responsibility: Clean old structure tree from PDF.
"""

import pikepdf
import logging

logger = logging.getLogger(__name__)


class TreeCleaner:
    """Remove old structure tree from PDF."""

    @staticmethod
    def remove_structure_tree(pdf: pikepdf.Pdf) -> bool:
        """
        Remove existing structure tree from PDF.

        Args:
            pdf: pikepdf.Pdf object to clean

        Returns:
            True if structure tree was removed, False if none existed

        Modifies PDF in place.
        """
        if '/StructTreeRoot' not in pdf.Root:
            logger.info("[TREE-CLEANER] No structure tree to remove")
            return False

        logger.info("[TREE-CLEANER] Removing existing structure tree")

        # Remove StructTreeRoot
        del pdf.Root['/StructTreeRoot']

        # Remove MarkInfo if present
        if '/MarkInfo' in pdf.Root:
            logger.info("[TREE-CLEANER] Removing MarkInfo")
            del pdf.Root['/MarkInfo']

        # Clean up page structure parent references
        TreeCleaner._clean_page_struct_parents(pdf)

        logger.info("[TREE-CLEANER] Structure tree removed successfully")
        return True

    @staticmethod
    def _clean_page_struct_parents(pdf: pikepdf.Pdf) -> None:
        """
        Remove /StructParents from all pages.

        Args:
            pdf: pikepdf.Pdf object

        Modifies pages in place.
        """
        cleaned_count = 0

        for page in pdf.pages:
            if '/StructParents' in page:
                del page['/StructParents']
                cleaned_count += 1

        if cleaned_count > 0:
            logger.info(f"[TREE-CLEANER] Cleaned /StructParents from {cleaned_count} pages")

    @staticmethod
    def verify_clean(pdf: pikepdf.Pdf) -> bool:
        """
        Verify structure tree was removed.

        Args:
            pdf: pikepdf.Pdf object

        Returns:
            True if no structure tree exists
        """
        has_struct_tree = '/StructTreeRoot' in pdf.Root
        has_mark_info = '/MarkInfo' in pdf.Root

        is_clean = not has_struct_tree and not has_mark_info

        if is_clean:
            logger.info("[TREE-CLEANER] Verification: PDF is clean")
        else:
            logger.warning("[TREE-CLEANER] Verification failed: structure remnants found")

        return is_clean


# Standalone test function
def test_tree_cleaner():
    """Test tree cleaner."""
    logging.basicConfig(level=logging.INFO)
    logger.info("[TREE-CLEANER-TEST] Module loaded successfully")


if __name__ == '__main__':
    test_tree_cleaner()
