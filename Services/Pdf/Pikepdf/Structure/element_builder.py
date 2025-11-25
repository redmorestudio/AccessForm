#!/usr/bin/env python3
"""
Structure element builder - creates PDF structure elements from model.
Single responsibility: Build individual structure element dictionaries.
"""

import pikepdf
import logging
from typing import Dict, Any, Optional

logger = logging.getLogger(__name__)


class ElementBuilder:
    """Build PDF structure elements from StructureNode model."""

    @staticmethod
    def build_element(
        pdf: pikepdf.Pdf,
        node_data: Dict[str, Any],
        parent_ref: Optional[pikepdf.Object] = None
    ) -> pikepdf.Dictionary:
        """
        Build structure element from node data.

        Args:
            pdf: pikepdf.Pdf object
            node_data: Dictionary with node properties:
                {
                    'role': str (e.g., 'H1', 'P', 'TD'),
                    'id': str (optional),
                    'alt': str (optional),
                    'actual_text': str (optional),
                    'lang': str (optional)
                }
            parent_ref: Reference to parent element (optional)

        Returns:
            Structure element Dictionary
        """
        # Create structure element dictionary
        element = pikepdf.Dictionary({
            '/Type': pikepdf.Name('/StructElem'),
            '/S': pikepdf.Name(f'/{node_data["role"]}')
        })

        # Add parent reference if provided
        if parent_ref is not None:
            element['/P'] = parent_ref

        # Add optional attributes
        if 'id' in node_data and node_data['id']:
            element['/ID'] = pikepdf.String(node_data['id'])

        if 'alt' in node_data and node_data['alt']:
            element['/Alt'] = pikepdf.String(node_data['alt'])

        if 'actual_text' in node_data and node_data['actual_text']:
            element['/ActualText'] = pikepdf.String(node_data['actual_text'])

        if 'lang' in node_data and node_data['lang']:
            element['/Lang'] = pikepdf.String(node_data['lang'])

        # Make element indirect (required for structure tree)
        element = pdf.make_indirect(element)

        logger.debug(f"[ELEMENT-BUILDER] Created {node_data['role']} element")

        return element

    @staticmethod
    def build_struct_tree_root(pdf: pikepdf.Pdf) -> pikepdf.Dictionary:
        """
        Create StructTreeRoot dictionary.

        Args:
            pdf: pikepdf.Pdf object

        Returns:
            StructTreeRoot Dictionary
        """
        struct_tree_root = pikepdf.Dictionary({
            '/Type': pikepdf.Name('/StructTreeRoot')
        })

        # Make indirect
        struct_tree_root = pdf.make_indirect(struct_tree_root)

        # Add to document catalog
        pdf.Root['/StructTreeRoot'] = struct_tree_root

        # Add MarkInfo
        mark_info = pikepdf.Dictionary({
            '/Marked': True
        })
        pdf.Root['/MarkInfo'] = mark_info

        logger.info("[ELEMENT-BUILDER] Created StructTreeRoot")

        return struct_tree_root

    @staticmethod
    def add_role_map(pdf: pikepdf.Pdf, role_mappings: Dict[str, str]) -> None:
        """
        Add RoleMap to StructTreeRoot.

        Args:
            pdf: pikepdf.Pdf object
            role_mappings: Dictionary mapping custom roles to standard roles
                Example: {'Header': 'H', 'Footer': 'P'}
        """
        if '/StructTreeRoot' not in pdf.Root:
            logger.warning("[ELEMENT-BUILDER] No StructTreeRoot for RoleMap")
            return

        role_map = pikepdf.Dictionary({
            pikepdf.Name(f'/{k}'): pikepdf.Name(f'/{v}')
            for k, v in role_mappings.items()
        })

        pdf.Root.StructTreeRoot['/RoleMap'] = role_map

        logger.info(f"[ELEMENT-BUILDER] Added RoleMap with {len(role_mappings)} mappings")


# Standalone test function
def test_element_builder():
    """Test element builder."""
    logging.basicConfig(level=logging.INFO)
    logger.info("[ELEMENT-BUILDER-TEST] Module loaded successfully")


if __name__ == '__main__':
    test_element_builder()
