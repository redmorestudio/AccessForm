#!/usr/bin/env python3
"""
MCID allocator - assigns MCID numbers to content segments.
Single responsibility: Allocate sequential MCID numbers.
"""

import logging
from typing import List, Dict, Any

logger = logging.getLogger(__name__)


class McidAllocator:
    """Allocate MCID numbers to content segments."""

    @staticmethod
    def allocate_mcids(segments: List[Dict[str, Any]], start_mcid: int = 0) -> List[Dict[str, Any]]:
        """
        Allocate sequential MCID numbers to segments.

        Args:
            segments: List of segment dicts (from SegmentDetector)
            start_mcid: Starting MCID number (default 0)

        Returns:
            Same segment list with 'mcid' field populated
        """
        logger.info(f"[MCID-ALLOCATOR] Allocating MCIDs for {len(segments)} segments, "
                   f"starting from {start_mcid}")

        # Sort segments by start_index to ensure reading order
        sorted_segments = sorted(segments, key=lambda s: s['start_index'])

        # Allocate sequential MCIDs
        current_mcid = start_mcid
        for segment in sorted_segments:
            segment['mcid'] = current_mcid
            current_mcid += 1

        logger.info(f"[MCID-ALLOCATOR] Allocated MCIDs {start_mcid} to {current_mcid - 1}")

        return sorted_segments

    @staticmethod
    def get_mcid_mapping(segments: List[Dict[str, Any]]) -> Dict[int, Dict[str, Any]]:
        """
        Create MCID-to-segment mapping for fast lookup.

        Args:
            segments: List of segments with MCIDs allocated

        Returns:
            Dictionary mapping MCID to segment dict
        """
        mapping = {
            seg['mcid']: seg
            for seg in segments
            if seg['mcid'] is not None
        }

        logger.debug(f"[MCID-ALLOCATOR] Created mapping for {len(mapping)} MCIDs")
        return mapping


# Standalone test function
def test_mcid_allocator():
    """Test MCID allocator."""
    logging.basicConfig(level=logging.INFO)

    # Test data
    segments = [
        {'start_index': 0, 'end_index': 10, 'type': 'text', 'mcid': None},
        {'start_index': 15, 'end_index': 20, 'type': 'image', 'mcid': None},
        {'start_index': 25, 'end_index': 35, 'type': 'text', 'mcid': None},
    ]

    result = McidAllocator.allocate_mcids(segments)

    logger.info("[MCID-ALLOCATOR-TEST] Allocated segments:")
    for seg in result:
        logger.info(f"  MCID {seg['mcid']}: {seg['type']} [{seg['start_index']}-{seg['end_index']}]")


if __name__ == '__main__':
    test_mcid_allocator()
