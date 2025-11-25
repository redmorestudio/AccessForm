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

        # Sort by start index
        segments.sort(key=lambda s: s['start_index'])

        logger.info(f"[SEGMENT-DETECTOR] Detected {len(segments)} segments: "
                   f"{len(text_segments)} text, {len(image_segments)} images")

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
