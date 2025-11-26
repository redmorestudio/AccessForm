#!/usr/bin/env python3
"""
Structure hierarchy builder - connects parent-child relationships.
Single responsibility: Build structure tree hierarchy from flat node list.
"""

import pikepdf
import logging
from typing import List, Dict, Any

logger = logging.getLogger(__name__)


class HierarchyBuilder:
    """Build structure tree hierarchy from nodes."""

    @staticmethod
    def build_hierarchy(
        pdf: pikepdf.Pdf,
        struct_tree_root: pikepdf.Dictionary,
        nodes: List[Dict[str, Any]]
    ) -> Dict[str, pikepdf.Dictionary]:
        """
        Build structure hierarchy and return element map.

        Args:
            pdf: pikepdf.Pdf object
            struct_tree_root: StructTreeRoot dictionary
            nodes: List of node dictionaries from StructureTree model:
                [
                    {
                        'id': str,
                        'role': str,
                        'children': List[Dict],
                        ... (other properties)
                    },
                    ...
                ]

        Returns:
            Dictionary mapping node IDs to element objects
        """
        logger.info(f"[HIERARCHY-BUILDER] Building hierarchy for {len(nodes)} nodes")

        # Build element map
        element_map = {}

        # Create root-level elements
        root_elements = []

        for node in nodes:
            element, child_elements = HierarchyBuilder._build_node_tree(
                pdf, node, struct_tree_root, element_map
            )
            root_elements.append(element)

        # Add root elements to StructTreeRoot /K array
        if len(root_elements) == 1:
            struct_tree_root['/K'] = root_elements[0]
        else:
            struct_tree_root['/K'] = pikepdf.Array(root_elements)

        logger.info(f"[HIERARCHY-BUILDER] Created hierarchy with {len(element_map)} total elements")

        return element_map

    @staticmethod
    def _build_node_tree(
        pdf: pikepdf.Pdf,
        node: Dict[str, Any],
        parent_ref: pikepdf.Object,
        element_map: Dict[str, pikepdf.Dictionary]
    ) -> tuple:
        """
        Recursively build node and its children.

        Args:
            pdf: pikepdf.Pdf object
            node: Node dictionary
            parent_ref: Parent element reference
            element_map: Map to populate with node ID -> element

        Returns:
            Tuple of (element, list of child elements)
        """
        from .element_builder import ElementBuilder

        # Create element for this node
        element = ElementBuilder.build_element(pdf, node, parent_ref)

        # Store in element map
        if 'id' in node:
            element_map[node['id']] = element
            logger.info(f"[HIERARCHY-BUILDER] Created element: id={node['id']}, role={node.get('role')}")

        # Process children
        child_elements = []

        if 'children' in node and node['children']:
            for child_node in node['children']:
                child_element, _ = HierarchyBuilder._build_node_tree(
                    pdf, child_node, element, element_map
                )
                child_elements.append(child_element)

            # Add children to element's /K array
            if len(child_elements) == 1:
                element['/K'] = child_elements[0]
            else:
                element['/K'] = pikepdf.Array(child_elements)

        return element, child_elements

    @staticmethod
    def validate_hierarchy(pdf: pikepdf.Pdf) -> Dict[str, Any]:
        """
        Validate structure hierarchy.

        Args:
            pdf: pikepdf.Pdf object

        Returns:
            Validation results dictionary
        """
        if '/StructTreeRoot' not in pdf.Root:
            return {'valid': False, 'error': 'No StructTreeRoot'}

        struct_root = pdf.Root.StructTreeRoot

        if '/K' not in struct_root:
            return {'valid': False, 'error': 'StructTreeRoot has no /K'}

        # Count elements
        element_count = HierarchyBuilder._count_elements(struct_root)

        result = {
            'valid': element_count > 0,
            'element_count': element_count
        }

        logger.info(f"[HIERARCHY-BUILDER] Validation: {element_count} elements, "
                   f"valid={result['valid']}")

        return result

    @staticmethod
    def _count_elements(node: pikepdf.Dictionary) -> int:
        """
        Recursively count structure elements.

        Args:
            node: Structure node

        Returns:
            Number of elements
        """
        count = 1  # Count this node

        if '/K' in node:
            k = node.K
            if isinstance(k, list):
                for kid in k:
                    if isinstance(kid, pikepdf.Dictionary):
                        if kid.get('/Type') != '/MCR':  # Don't count MCRs
                            count += HierarchyBuilder._count_elements(kid)
            elif isinstance(k, pikepdf.Dictionary):
                if k.get('/Type') != '/MCR':
                    count += HierarchyBuilder._count_elements(k)

        return count


# Standalone test function
def test_hierarchy_builder():
    """Test hierarchy builder."""
    logging.basicConfig(level=logging.INFO)
    logger.info("[HIERARCHY-BUILDER-TEST] Module loaded successfully")


if __name__ == '__main__':
    test_hierarchy_builder()
