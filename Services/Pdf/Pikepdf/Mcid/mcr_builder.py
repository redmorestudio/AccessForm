#!/usr/bin/env python3
"""
MCR (Marked Content Reference) kid builder.
Single responsibility: Create MCR dictionaries in structure tree elements.
"""

import pikepdf
import logging
from typing import List, Dict, Any

logger = logging.getLogger(__name__)


class McrBuilder:
    """Build MCR kids for structure tree elements."""

    @staticmethod
    def add_mcr_kids(
        pdf: pikepdf.Pdf,
        element_mcid_map: Dict[str, List[Dict[str, Any]]]
    ) -> int:
        """
        Add MCR kids to structure elements.

        Args:
            pdf: pikepdf.Pdf object
            element_mcid_map: Dictionary mapping element IDs to MCID references:
                {
                    'element_id': [
                        {'page_index': int, 'mcid': int},
                        ...
                    ],
                    ...
                }

        Returns:
            Number of MCR kids created

        Note: Element ID format is "/Parent#/Child#" for hierarchy navigation
        """
        logger.info(f"[MCR-BUILDER] Adding MCR kids for {len(element_mcid_map)} elements")

        total_mcr_count = 0

        for element_id, mcid_refs in element_mcid_map.items():
            try:
                # Find element in structure tree by ID
                element = McrBuilder._find_element_by_id(pdf, element_id)

                if element is None:
                    logger.warning(f"[MCR-BUILDER] Element not found: {element_id}")
                    continue

                # Add MCR kids to element
                mcr_count = McrBuilder._add_mcrs_to_element(pdf, element, mcid_refs)
                total_mcr_count += mcr_count

            except Exception as e:
                logger.error(f"[MCR-BUILDER] Failed to add MCRs to {element_id}: {e}")

        logger.info(f"[MCR-BUILDER] Created {total_mcr_count} MCR kids total")
        return total_mcr_count

    @staticmethod
    def _find_element_by_id(pdf: pikepdf.Pdf, element_id: str) -> pikepdf.Dictionary:
        """
        Find structure element by ID.

        Args:
            pdf: pikepdf.Pdf object
            element_id: Element ID string (e.g., "/0/1/2")

        Returns:
            Structure element Dictionary or None if not found
        """
        if '/StructTreeRoot' not in pdf.Root:
            return None

        # Parse element ID into path
        path_parts = [p for p in element_id.split('/') if p]

        # Navigate structure tree
        current = pdf.Root.StructTreeRoot

        for part in path_parts:
            # Get /K value safely
            try:
                kids_value = current.get('/K')
            except:
                kids_value = None

            if kids_value is None:
                logger.debug(f"[MCR-BUILDER] No /K at path element {part}")
                return None

            index = int(part)

            # Handle array vs single kid
            if isinstance(kids_value, pikepdf.Array):
                if index >= len(kids_value):
                    logger.debug(f"[MCR-BUILDER] Index {index} out of bounds (len={len(kids_value)})")
                    return None
                current = kids_value[index]
            else:
                # Single kid - only valid if index is 0
                if index != 0:
                    logger.debug(f"[MCR-BUILDER] Single kid but index={index} (expected 0)")
                    return None
                current = kids_value

            # Verify we got a dictionary
            if not isinstance(current, pikepdf.Dictionary):
                logger.debug(f"[MCR-BUILDER] Navigation result is not Dictionary: {type(current)}")
                return None

        return current

    @staticmethod
    def _add_mcrs_to_element(
        pdf: pikepdf.Pdf,
        element: pikepdf.Dictionary,
        mcid_refs: List[Dict[str, Any]]
    ) -> int:
        """
        Add MCR dictionaries to structure element's /K array.

        Args:
            pdf: pikepdf.Pdf object
            element: Structure element to modify
            mcid_refs: List of MCID references

        Returns:
            Number of MCRs created
        """
        # Get or create /K array - use try/get to avoid pikepdf membership test issues
        try:
            k_value = element.get('/K')
        except:
            k_value = None

        if k_value is None:
            # No /K exists, create empty array
            element['/K'] = pikepdf.Array()
            k_array = element['/K']
        elif isinstance(k_value, pikepdf.Array):
            # Already an array, use it
            k_array = k_value
        else:
            # Single value, convert to array
            k_array = pikepdf.Array([k_value])
            element['/K'] = k_array

        # Add MCR for each MCID reference
        for ref in mcid_refs:
            mcr = McrBuilder._create_mcr_dict(pdf, ref['page_index'], ref['mcid'])
            k_array.append(mcr)

        logger.debug(f"[MCR-BUILDER] Added {len(mcid_refs)} MCRs to element")
        return len(mcid_refs)

    @staticmethod
    def _create_mcr_dict(pdf: pikepdf.Pdf, page_index: int, mcid: int) -> pikepdf.Dictionary:
        """
        Create MCR dictionary.

        Format: << /Type /MCR /Pg page_ref /MCID mcid >>

        Args:
            pdf: pikepdf.Pdf object
            page_index: Zero-based page index
            mcid: MCID number

        Returns:
            MCR Dictionary
        """
        page = pdf.pages[page_index]

        mcr = pikepdf.Dictionary({
            '/Type': pikepdf.Name('/MCR'),
            '/Pg': page.obj,
            '/MCID': mcid
        })

        return mcr

    @staticmethod
    def verify_mcr_kids(pdf: pikepdf.Pdf) -> Dict[str, Any]:
        """
        Verify MCR kids in structure tree.

        Args:
            pdf: pikepdf.Pdf object

        Returns:
            Dictionary with verification results:
            {
                'total_mcr_count': int,
                'elements_with_mcrs': int,
                'valid': bool
            }
        """
        if '/StructTreeRoot' not in pdf.Root:
            return {'total_mcr_count': 0, 'elements_with_mcrs': 0, 'valid': False}

        result = {'total_mcr_count': 0, 'elements_with_mcrs': 0, 'valid': False}

        def count_mcrs(node):
            if not isinstance(node, pikepdf.Dictionary):
                return

            if '/K' in node:
                k = node.K
                if isinstance(k, list):
                    has_mcr = False
                    for kid in k:
                        if isinstance(kid, pikepdf.Dictionary):
                            if kid.get('/Type') == '/MCR':
                                result['total_mcr_count'] += 1
                                has_mcr = True
                            else:
                                count_mcrs(kid)
                    if has_mcr:
                        result['elements_with_mcrs'] += 1
                elif isinstance(k, pikepdf.Dictionary):
                    if k.get('/Type') == '/MCR':
                        result['total_mcr_count'] += 1
                        result['elements_with_mcrs'] += 1
                    else:
                        count_mcrs(k)

        count_mcrs(pdf.Root.StructTreeRoot)

        result['valid'] = result['total_mcr_count'] > 0

        logger.info(f"[MCR-BUILDER] Verification: {result['total_mcr_count']} MCRs in "
                   f"{result['elements_with_mcrs']} elements")

        return result


# Standalone test function
def test_mcr_builder():
    """Test MCR builder."""
    logging.basicConfig(level=logging.INFO)
    logger.info("[MCR-BUILDER-TEST] Module loaded successfully")


if __name__ == '__main__':
    test_mcr_builder()
