#!/usr/bin/env python3
"""
Segment bounding box extractor - estimates geometric location of content segments.
Based on Phase 6I specification for content stream geometry estimation.
"""

import logging
from typing import List, Dict, Any, Optional, Tuple

logger = logging.getLogger(__name__)


class TextState:
    """
    Track text rendering state between BT/ET operators.
    Approximates text position based on Tm, Td, TD, Tf operators.
    """
    def __init__(self):
        self.in_text = False  # Between BT and ET
        self.x = 0.0          # Current text x position
        self.y = 0.0          # Current text y position
        self.font_size = 12.0 # Current font size (default 12pt)
        self.line_height = 12.0  # Estimated line height


class SegmentBboxExtractor:
    """
    Extract bounding boxes for content segments by tracking geometry in content streams.
    """

    @staticmethod
    def extract_segment_bboxes(
        operators: List[Dict[str, Any]],
        segments: List[Dict[str, Any]]
    ) -> List[Dict[str, Any]]:
        """
        Estimate bounding boxes for content segments.

        Args:
            operators: List of operators from ContentParser (with 'index', 'operator', 'operands')
            segments: List of segments from SegmentDetector (with 'type', 'start_index', 'end_index')

        Returns:
            Segments with added 'bbox' field: {'x': float, 'y': float, 'width': float, 'height': float}
        """
        logger.info(f"[BBOX-EXTRACTOR] Extracting bboxes for {len(segments)} segments from {len(operators)} operators")

        # Build operator index map for fast lookup
        op_by_index = {op['index']: op for op in operators}

        # Assign geometry to each operator
        op_geometries = SegmentBboxExtractor._assign_operator_geometries(operators)

        # Build bounding box for each segment
        segments_with_bbox = []
        for segment in segments:
            bbox = SegmentBboxExtractor._compute_segment_bbox(
                segment,
                op_by_index,
                op_geometries
            )

            segment_copy = segment.copy()
            segment_copy['bbox'] = bbox
            segments_with_bbox.append(segment_copy)

        # Log statistics
        with_bbox = sum(1 for s in segments_with_bbox if s['bbox'] is not None)
        logger.info(f"[BBOX-EXTRACTOR] {with_bbox}/{len(segments_with_bbox)} segments have bounding boxes")

        return segments_with_bbox

    @staticmethod
    def _assign_operator_geometries(operators: List[Dict[str, Any]]) -> Dict[int, Dict[str, float]]:
        """
        Walk through operators and assign estimated geometry to each one.

        Returns:
            Map of operator_index -> {'x': float, 'y': float, 'width': float, 'height': float}
        """
        text_state = TextState()
        geometries = {}

        for op in operators:
            op_idx = op['index']
            op_name = op['operator']
            operands = op.get('operands', [])

            # Track text state
            if op_name == 'BT':
                text_state.in_text = True

            elif op_name == 'ET':
                text_state.in_text = False

            elif op_name == 'Tm' and len(operands) >= 6:
                # Text matrix: a b c d e f
                # e, f are translation (x, y position)
                text_state.x = float(operands[4])
                text_state.y = float(operands[5])

            elif op_name in ('Td', 'TD') and len(operands) >= 2:
                # Text displacement: tx ty
                text_state.x += float(operands[0])
                text_state.y += float(operands[1])

            elif op_name == 'Tf' and len(operands) >= 2:
                # Font: /FontName fontSize
                text_state.font_size = float(operands[1])
                text_state.line_height = text_state.font_size * 1.2  # Approximate line height

            elif op_name in ('Tj', 'TJ', "'", '"') and text_state.in_text:
                # Text showing operators - assign current text state
                geometries[op_idx] = {
                    'x': text_state.x,
                    'y': text_state.y,
                    'width': text_state.font_size * 10,  # Rough estimate (10 chars average)
                    'height': text_state.font_size
                }

                # Advance x position (rough approximation)
                text_state.x += text_state.font_size * 10

            elif op_name == 'Do' and len(operands) > 0:
                # XObject (image) - use current position or estimate
                # Images don't have explicit position in Do, need to track cm/q transforms
                # For v1, use a default placeholder or skip
                geometries[op_idx] = {
                    'x': text_state.x if text_state.x != 0 else 100,
                    'y': text_state.y if text_state.y != 0 else 100,
                    'width': 100,  # Default image size
                    'height': 100
                }

            # Note: Graphics operators (paths, fills) would need cm matrix tracking
            # For v1, we focus on text and images only

        return geometries

    @staticmethod
    def _compute_segment_bbox(
        segment: Dict[str, Any],
        op_by_index: Dict[int, Dict[str, Any]],
        op_geometries: Dict[int, Dict[str, float]]
    ) -> Optional[Dict[str, float]]:
        """
        Compute bounding box for a segment by unioning operator geometries.

        Args:
            segment: Segment with 'start_index', 'end_index'
            op_by_index: Map of index -> operator
            op_geometries: Map of index -> geometry

        Returns:
            Bounding box dict or None if no geometry found
        """
        start_idx = segment['start_index']
        end_idx = segment['end_index']

        # Collect all geometries in this segment's index range
        segment_geometries = []
        for idx in range(start_idx, end_idx + 1):
            if idx in op_geometries:
                segment_geometries.append(op_geometries[idx])

        if not segment_geometries:
            # No geometry found for this segment
            return None

        # Union all geometries to get segment bounding box
        min_x = min(g['x'] for g in segment_geometries)
        min_y = min(g['y'] for g in segment_geometries)
        max_x = max(g['x'] + g['width'] for g in segment_geometries)
        max_y = max(g['y'] + g['height'] for g in segment_geometries)

        return {
            'x': min_x,
            'y': min_y,
            'width': max_x - min_x,
            'height': max_y - min_y
        }


# Standalone test function
def test_segment_bbox_extractor():
    """Test segment bbox extractor."""
    logging.basicConfig(level=logging.INFO)

    # Mock operators
    operators = [
        {'index': 0, 'operator': 'BT', 'operands': []},
        {'index': 1, 'operator': 'Tf', 'operands': ['/F1', 12]},
        {'index': 2, 'operator': 'Tm', 'operands': [1, 0, 0, 1, 100, 700]},
        {'index': 3, 'operator': 'Tj', 'operands': ['Hello']},
        {'index': 4, 'operator': 'ET', 'operands': []},
    ]

    segments = [
        {'type': 'text', 'start_index': 0, 'end_index': 4, 'mcid': None}
    ]

    result = SegmentBboxExtractor.extract_segment_bboxes(operators, segments)

    logger.info(f"[TEST] Result: {result}")
    assert len(result) == 1
    assert result[0]['bbox'] is not None
    assert result[0]['bbox']['x'] == 100
    logger.info("[TEST] ✅ Segment bbox extractor test passed")


if __name__ == '__main__':
    test_segment_bbox_extractor()
