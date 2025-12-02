#!/usr/bin/env python3
"""
Spatial mapper - matches content segments to structure nodes based on bounding box overlap.
Uses IoU (Intersection over Union) for spatial matching.
"""

import logging
from typing import List, Dict, Any, Optional, Tuple

logger = logging.getLogger(__name__)


class SpatialMapper:
    """
    Map content segments to structure nodes using spatial overlap (IoU).
    """

    # IoU threshold for considering a match
    DEFAULT_IOU_THRESHOLD = 0.3

    # Vertical margin to extend segment bboxes (in PDF points)
    # This accounts for page header/margin areas where headings appear
    # but segment bbox extraction doesn't reach
    VERTICAL_MARGIN_EXTENSION = 50.0

    # Horizontal margin to extend segment bboxes (in PDF points)
    # Segments from content streams often have narrow X ranges that miss
    # heading elements positioned elsewhere on the page
    HORIZONTAL_MARGIN_EXTENSION = 150.0

    @staticmethod
    def _reset_debug_flags():
        """Reset debug flags so we can see first bbox per page on each run."""
        for attr in list(vars(SpatialMapper).keys()):
            if attr.startswith('_debug_'):
                delattr(SpatialMapper, attr)

    @staticmethod
    def map_segments_to_nodes(
        segments: List[Dict[str, Any]],
        nodes: List[Dict[str, Any]],
        iou_threshold: float = DEFAULT_IOU_THRESHOLD
    ) -> Dict[str, List[Dict[str, Any]]]:
        """
        Map content segments to structure nodes based on spatial overlap.

        Args:
            segments: List of segments with 'page_index', 'mcid', 'bbox' (x, y, width, height)
            nodes: List of structure nodes with 'id', 'page_index', 'target_bounds' or 'bounds'
            iou_threshold: Minimum IoU score to consider a match (default 0.3)

        Returns:
            element_mcid_map: Dict mapping node_id -> list of {page_index, mcid}
        """
        logger.info(f"[SPATIAL-MAPPER] Mapping {len(segments)} segments to structure nodes")
        logger.info(f"[SPATIAL-MAPPER] Threshold: {iou_threshold}, margins: V={SpatialMapper.VERTICAL_MARGIN_EXTENSION}pt, H={SpatialMapper.HORIZONTAL_MARGIN_EXTENSION}pt")

        # Reset debug flags for each run so we can see first bbox per page
        SpatialMapper._reset_debug_flags()

        # Flatten node hierarchy to get all leaf and container nodes with bounds
        logger.info(f"[SPATIAL-MAPPER] Input: {len(nodes)} root nodes")
        all_nodes = SpatialMapper._flatten_nodes(nodes)
        logger.info(f"[SPATIAL-MAPPER] Flattened to {len(all_nodes)} total nodes with bounds")

        # Build element_mcid_map
        element_mcid_map = {}
        matched_count = 0
        fallback_count = 0

        for segment in segments:
            page_index = segment.get('page_index', 0)
            mcid = segment['mcid']
            seg_bbox = segment.get('bbox')

            if seg_bbox is None:
                # No bbox for this segment - fallback to root
                node_id = '/0'
                fallback_count += 1
                logger.debug(f"[SPATIAL-MAPPER] Segment (mcid={mcid}) has no bbox - fallback to '{node_id}'")
            else:
                # Find best matching node by IoU
                best_node_id, best_iou = SpatialMapper._find_best_node(
                    seg_bbox,
                    page_index,
                    all_nodes,
                    iou_threshold
                )

                if best_node_id:
                    node_id = best_node_id
                    matched_count += 1
                    logger.debug(f"[SPATIAL-MAPPER] Segment (mcid={mcid}) matched to '{node_id}' (IoU={best_iou:.3f})")
                else:
                    # No spatial match - fallback to root
                    node_id = '/0'
                    fallback_count += 1
                    logger.debug(f"[SPATIAL-MAPPER] Segment (mcid={mcid}) no match - fallback to '{node_id}'")

            # Add to element_mcid_map
            if node_id not in element_mcid_map:
                element_mcid_map[node_id] = []

            element_mcid_map[node_id].append({
                'page_index': page_index,
                'mcid': mcid
            })

        # Log statistics
        logger.info(f"[SPATIAL-MAPPER] Mapping complete:")
        logger.info(f"  - Spatial matches: {matched_count} ({matched_count/len(segments)*100:.1f}%)")
        logger.info(f"  - Fallback to root: {fallback_count} ({fallback_count/len(segments)*100:.1f}%)")
        logger.info(f"  - Nodes with content: {len(element_mcid_map)}")

        return element_mcid_map

    @staticmethod
    def _flatten_nodes(nodes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """
        Recursively flatten node hierarchy to get all nodes with bounds.

        Args:
            nodes: List of nodes with potential 'children'

        Returns:
            Flat list of all nodes (including containers and leaves)
        """
        flat_nodes = []
        nodes_inspected = 0
        nodes_with_bounds = 0
        nodes_without_bounds = 0

        def recurse(node_list):
            nonlocal nodes_inspected, nodes_with_bounds, nodes_without_bounds
            for node in node_list:
                nodes_inspected += 1
                # Add this node if it has bounds (either target_bounds or bounds)
                target_bounds = node.get('target_bounds') or node.get('targetBounds')
                bounds = node.get('bounds')

                if target_bounds or bounds:
                    flat_nodes.append(node)
                    nodes_with_bounds += 1
                else:
                    nodes_without_bounds += 1
                    if nodes_inspected <= 5:  # Log first 5 nodes without bounds
                        logger.debug(f"[SPATIAL-MAPPER] Node {node.get('id', 'unknown')} has no bounds (role={node.get('role', 'unknown')})")

                # Recurse into children
                children = node.get('children', [])
                if children:
                    recurse(children)

        recurse(nodes)
        logger.info(f"[SPATIAL-MAPPER] Flattening stats: inspected={nodes_inspected}, with_bounds={nodes_with_bounds}, without_bounds={nodes_without_bounds}")
        return flat_nodes

    @staticmethod
    def _extend_bbox(
        bbox: Dict[str, float],
        vertical_margin: float,
        horizontal_margin: float
    ) -> Dict[str, float]:
        """
        Extend a bounding box by adding margins in both directions.

        Vertical: Extends upward (higher y in PDF coords) for header content.
        Horizontal: Extends both left and right to capture page-wide content.

        Args:
            bbox: Original bbox {x, y, width, height}
            vertical_margin: Points to add to height (extending upward)
            horizontal_margin: Points to add to width (split between left and right)

        Returns:
            Extended bbox with expanded bounds
        """
        # Extend left by half the horizontal margin, right by the other half
        # Also clamp x to >= 0 to avoid negative coordinates
        new_x = max(0, bbox['x'] - horizontal_margin)
        new_width = bbox['width'] + horizontal_margin * 2  # Extend both sides

        return {
            'x': new_x,
            'y': bbox['y'],  # Keep same bottom
            'width': new_width,
            'height': bbox['height'] + vertical_margin  # Extend upward
        }

    @staticmethod
    def _find_best_node(
        seg_bbox: Dict[str, float],
        page_index: int,
        nodes: List[Dict[str, Any]],
        threshold: float
    ) -> Tuple[Optional[str], float]:
        """
        Find the best matching node for a segment based on IoU.

        Args:
            seg_bbox: Segment bbox {x, y, width, height}
            page_index: Segment's page index
            nodes: List of all nodes with bounds
            threshold: Minimum IoU to accept

        Returns:
            (node_id, iou_score) or (None, 0.0) if no match above threshold
        """
        best_node_id = None
        best_iou = 0.0

        # Extend segment bbox to capture header/margin content and page-wide elements
        extended_bbox = SpatialMapper._extend_bbox(
            seg_bbox,
            SpatialMapper.VERTICAL_MARGIN_EXTENSION,
            SpatialMapper.HORIZONTAL_MARGIN_EXTENSION
        )

        # Debug: Log first segment bbox per page for coordinate analysis
        page_debug_key = f'_debug_page_{page_index}'
        if not hasattr(SpatialMapper, page_debug_key):
            setattr(SpatialMapper, page_debug_key, True)
            logger.info(f"[SPATIAL-DEBUG] Page {page_index} segment bbox: x={seg_bbox['x']:.1f}, y={seg_bbox['y']:.1f}, w={seg_bbox['width']:.1f}, h={seg_bbox['height']:.1f} -> extended: x={extended_bbox['x']:.1f}, w={extended_bbox['width']:.1f}, h={extended_bbox['height']:.1f}")

        for node in nodes:
            # Check page match
            node_page = node.get('page_index') or node.get('pageIndex', 0)
            if node_page != page_index:
                continue

            # Get node bounds (prefer target_bounds from layout plan)
            node_bounds = node.get('target_bounds') or node.get('targetBounds') or node.get('bounds')
            if not node_bounds:
                continue

            # Debug: Log first node bbox per page for coordinate analysis
            node_debug_key = f'_debug_node_page_{page_index}'
            if not hasattr(SpatialMapper, node_debug_key):
                setattr(SpatialMapper, node_debug_key, True)
                logger.info(f"[SPATIAL-DEBUG] Page {page_index} node bounds: x={node_bounds['x']:.1f}, y={node_bounds['y']:.1f}, w={node_bounds['width']:.1f}, h={node_bounds['height']:.1f}, role={node.get('role', '?')}")

            # Calculate overlap score using containment-based approach
            # Check if node is contained within the extended segment bbox
            score = SpatialMapper._calculate_containment_score(extended_bbox, node_bounds)

            if score > best_iou and score >= threshold:
                best_iou = score
                best_node_id = node['id']

        return best_node_id, best_iou

    @staticmethod
    def _calculate_containment_score(container_bbox: Dict[str, float], node_bbox: Dict[str, float]) -> float:
        """
        Calculate a containment-based score that checks if node is within container.

        This is more appropriate than IoU when comparing page-wide segments
        to small structural elements like headings.

        Returns:
            1.0 if node is fully contained within container
            0.0 to 1.0 based on percentage of node area that overlaps
        """
        # Get container bounds
        c_x_min = container_bbox['x']
        c_y_min = container_bbox['y']
        c_x_max = c_x_min + container_bbox['width']
        c_y_max = c_y_min + container_bbox['height']

        # Get node bounds
        n_x_min = node_bbox['x']
        n_y_min = node_bbox['y']
        n_x_max = n_x_min + node_bbox['width']
        n_y_max = n_y_min + node_bbox['height']

        # Check if node is fully contained
        if (n_x_min >= c_x_min and n_x_max <= c_x_max and
            n_y_min >= c_y_min and n_y_max <= c_y_max):
            return 1.0

        # Calculate intersection
        inter_x_min = max(c_x_min, n_x_min)
        inter_y_min = max(c_y_min, n_y_min)
        inter_x_max = min(c_x_max, n_x_max)
        inter_y_max = min(c_y_max, n_y_max)

        # No overlap
        if inter_x_max <= inter_x_min or inter_y_max <= inter_y_min:
            return 0.0

        # Calculate what percentage of node is within container
        inter_area = (inter_x_max - inter_x_min) * (inter_y_max - inter_y_min)
        node_area = node_bbox['width'] * node_bbox['height']

        if node_area == 0:
            return 0.0

        return inter_area / node_area

    @staticmethod
    def _calculate_iou(bbox1: Dict[str, float], bbox2: Dict[str, float]) -> float:
        """
        Calculate Intersection over Union (IoU) between two bounding boxes.

        Args:
            bbox1: {x, y, width, height}
            bbox2: {x, y, width, height}

        Returns:
            IoU score between 0.0 (no overlap) and 1.0 (perfect overlap)
        """
        # Extract coordinates
        x1_min = bbox1['x']
        y1_min = bbox1['y']
        x1_max = x1_min + bbox1['width']
        y1_max = y1_min + bbox1['height']

        x2_min = bbox2['x']
        y2_min = bbox2['y']
        x2_max = x2_min + bbox2['width']
        y2_max = y2_min + bbox2['height']

        # Calculate intersection
        inter_x_min = max(x1_min, x2_min)
        inter_y_min = max(y1_min, y2_min)
        inter_x_max = min(x1_max, x2_max)
        inter_y_max = min(y1_max, y2_max)

        # Check if boxes overlap
        if inter_x_max < inter_x_min or inter_y_max < inter_y_min:
            return 0.0

        # Calculate areas
        inter_area = (inter_x_max - inter_x_min) * (inter_y_max - inter_y_min)
        bbox1_area = bbox1['width'] * bbox1['height']
        bbox2_area = bbox2['width'] * bbox2['height']

        # Calculate union
        union_area = bbox1_area + bbox2_area - inter_area

        if union_area == 0:
            return 0.0

        # Calculate IoU
        iou = inter_area / union_area
        return iou


# Standalone test function
def test_spatial_mapper():
    """Test spatial mapper with mock data."""
    logging.basicConfig(level=logging.INFO)

    # Mock segments (from segment detector with bboxes)
    segments = [
        {'page_index': 0, 'mcid': 0, 'bbox': {'x': 100, 'y': 700, 'width': 200, 'height': 20}},  # Should match node 0
        {'page_index': 0, 'mcid': 1, 'bbox': {'x': 100, 'y': 650, 'width': 200, 'height': 20}},  # Should match node 1
        {'page_index': 0, 'mcid': 2, 'bbox': {'x': 500, 'y': 700, 'width': 100, 'height': 50}},  # No match - different area
    ]

    # Mock nodes (from structure tree with target_bounds)
    nodes = [
        {
            'id': '/0',
            'role': 'Document',
            'page_index': 0,
            'target_bounds': None,  # Container, no bounds
            'children': [
                {
                    'id': '/0/0',
                    'role': 'H1',
                    'page_index': 0,
                    'target_bounds': {'x': 100, 'y': 690, 'width': 200, 'height': 25},
                    'children': []
                },
                {
                    'id': '/0/1',
                    'role': 'P',
                    'page_index': 0,
                    'target_bounds': {'x': 100, 'y': 640, 'width': 200, 'height': 25},
                    'children': []
                }
            ]
        }
    ]

    result = SpatialMapper.map_segments_to_nodes(segments, nodes, iou_threshold=0.3)

    logger.info(f"[TEST] Result: {result}")

    # Verify results
    assert '/0/0' in result, "H1 node should have matched segment"
    assert len(result['/0/0']) == 1, "H1 should have 1 segment"
    assert result['/0/0'][0]['mcid'] == 0, "H1 should match mcid 0"

    assert '/0/1' in result, "P node should have matched segment"
    assert len(result['/0/1']) == 1, "P should have 1 segment"
    assert result['/0/1'][0]['mcid'] == 1, "P should match mcid 1"

    assert '/0' in result, "Root should have fallback segment"
    assert result['/0'][0]['mcid'] == 2, "Root should have mcid 2"

    logger.info("[TEST] ✅ Spatial mapper test passed")


if __name__ == '__main__':
    test_spatial_mapper()
