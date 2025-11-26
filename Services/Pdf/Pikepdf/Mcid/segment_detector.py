#!/usr/bin/env python3
"""
Content segment detector - identifies BT/ET text blocks and Do images.
Single responsibility: Detect and classify content segments.
"""

import logging
from typing import List, Dict, Any

logger = logging.getLogger(__name__)


class SegmentDetector:
    """Detect content segments (text blocks, images) in operator list."""

    @staticmethod
    def detect_segments(operators: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """
        Detect all content segments in operator list.

        Args:
            operators: List of operators from ContentParser

        Returns:
            List of segment dictionaries:
            [
                {
                    'type': 'text' | 'image',
                    'start_index': int,
                    'end_index': int,
                    'mcid': None  # Will be assigned later
                },
                ...
            ]
        """
        logger.info(f"[SEGMENT-DETECTOR] Detecting segments in {len(operators)} operators")

        segments = []

        # Detect text segments (BT/ET pairs)
        text_segments = SegmentDetector._detect_text_segments(operators)
        segments.extend(text_segments)

        # Detect image segments (Do operators)
        image_segments = SegmentDetector._detect_image_segments(operators)
        segments.extend(image_segments)

        # Detect graphics segments (paths, fills, strokes)
        graphics_segments = SegmentDetector._detect_graphics_segments(operators, segments)
        segments.extend(graphics_segments)

        # Sort by start index
        segments.sort(key=lambda s: s['start_index'])

        logger.info(f"[SEGMENT-DETECTOR] Detected {len(segments)} segments: "
                   f"{len(text_segments)} text, {len(image_segments)} images, {len(graphics_segments)} graphics")

        return segments

    @staticmethod
    def _detect_text_segments(operators: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """
        Detect text segments (BT/ET pairs).

        Args:
            operators: List of operators

        Returns:
            List of text segment dicts
        """
        segments = []
        bt_index = None

        for op in operators:
            if op['operator'] == 'BT':
                if bt_index is not None:
                    # Nested BT - close previous segment
                    logger.warning(f"[SEGMENT-DETECTOR] Nested BT at index {op['index']}, "
                                 f"closing previous at {bt_index}")
                    segments.append({
                        'type': 'text',
                        'start_index': bt_index,
                        'end_index': op['index'] - 1,
                        'mcid': None
                    })
                bt_index = op['index']

            elif op['operator'] == 'ET':
                if bt_index is not None:
                    segments.append({
                        'type': 'text',
                        'start_index': bt_index,
                        'end_index': op['index'],
                        'mcid': None
                    })
                    bt_index = None
                else:
                    logger.warning(f"[SEGMENT-DETECTOR] ET without matching BT at index {op['index']}")

        if bt_index is not None:
            logger.warning(f"[SEGMENT-DETECTOR] Unclosed BT at index {bt_index}")

        return segments

    @staticmethod
    def _detect_image_segments(operators: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """
        Detect image segments (Do operators).

        Args:
            operators: List of operators

        Returns:
            List of image segment dicts
        """
        segments = []
        MAX_LOOKBACK = 5

        for i, op in enumerate(operators):
            if op['operator'] == 'Do' and len(op['operands']) > 0:
                # Look back for transform operators (cm, q)
                start_index = op['index']
                for j in range(max(0, i - MAX_LOOKBACK), i):
                    if operators[j]['operator'] in ['cm', 'q']:
                        start_index = operators[j]['index']
                        break

                segments.append({
                    'type': 'image',
                    'start_index': start_index,
                    'end_index': op['index'],
                    'mcid': None
                })

        return segments

    @staticmethod
    def _detect_graphics_segments(operators: List[Dict[str, Any]], existing_segments: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """
        Detect graphics segments (paths, fills, strokes) not already covered by text or images.

        Args:
            operators: List of operators
            existing_segments: Already detected segments (text, images)

        Returns:
            List of graphics segment dicts
        """
        segments = []

        # Graphics path construction operators
        PATH_OPS = {'m', 'l', 'c', 'v', 'y', 'h', 're'}
        # Graphics painting operators
        PAINT_OPS = {'S', 's', 'f', 'F', 'f*', 'B', 'B*', 'b', 'b*', 'W', 'W*', 'n'}
        # Graphics state operators
        STATE_OPS = {'q', 'Q', 'cm', 'w', 'J', 'j', 'M', 'd', 'ri', 'i', 'gs', 'CS', 'cs', 'SC', 'SCN', 'sc', 'scn', 'G', 'g', 'RG', 'rg', 'K', 'k'}

        # Create set of all indices covered by existing segments
        covered_indices = set()
        for seg in existing_segments:
            for idx in range(seg['start_index'], seg['end_index'] + 1):
                covered_indices.add(idx)

        # Find graphics runs
        in_graphics_run = False
        run_start = None

        for i, op in enumerate(operators):
            op_idx = op['index']
            op_name = op['operator']

            # Skip if already covered by text/image segment
            if op_idx in covered_indices:
                if in_graphics_run and run_start is not None:
                    # End graphics run
                    segments.append({
                        'type': 'graphics',
                        'start_index': run_start,
                        'end_index': operators[i-1]['index'],
                        'mcid': None
                    })
                    in_graphics_run = False
                    run_start = None
                continue

            # Check if this is a graphics operator
            is_graphics = (op_name in PATH_OPS or op_name in PAINT_OPS or op_name in STATE_OPS)

            if is_graphics:
                if not in_graphics_run:
                    # Start new graphics run
                    in_graphics_run = True
                    run_start = op_idx
            else:
                if in_graphics_run and run_start is not None:
                    # End graphics run
                    segments.append({
                        'type': 'graphics',
                        'start_index': run_start,
                        'end_index': operators[i-1]['index'],
                        'mcid': None
                    })
                    in_graphics_run = False
                    run_start = None

        # Close final graphics run if still open
        if in_graphics_run and run_start is not None:
            segments.append({
                'type': 'graphics',
                'start_index': run_start,
                'end_index': operators[-1]['index'],
                'mcid': None
            })

        return segments

    @staticmethod
    def filter_segments(segments: List[Dict[str, Any]], segment_type: str = None) -> List[Dict[str, Any]]:
        """
        Filter segments by type.

        Args:
            segments: List of segments
            segment_type: 'text', 'image', or None for all

        Returns:
            Filtered segment list
        """
        if segment_type is None:
            return segments

        return [s for s in segments if s['type'] == segment_type]


# Standalone test function
def test_segment_detector():
    """Test segment detector."""
    logging.basicConfig(level=logging.INFO)
    logger.info("[SEGMENT-DETECTOR-TEST] Module loaded successfully")


if __name__ == '__main__':
    test_segment_detector()
