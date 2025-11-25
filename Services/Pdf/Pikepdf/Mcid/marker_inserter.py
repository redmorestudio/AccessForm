#!/usr/bin/env python3
"""
BDC/EMC marker inserter - inserts marked content operators into content streams.
Single responsibility: Insert BDC/EMC markers around content segments.
"""

import pikepdf
import logging
from typing import List, Dict, Any

logger = logging.getLogger(__name__)


class MarkerInserter:
    """Insert BDC/EMC markers into PDF content streams."""

    @staticmethod
    def insert_markers(
        pdf: pikepdf.Pdf,
        page: pikepdf.Page,
        operators: List[Dict[str, Any]],
        segments: List[Dict[str, Any]]
    ) -> None:
        """
        Insert BDC/EMC markers into page content stream.

        Args:
            pdf: pikepdf.Pdf object (needed to create streams)
            page: pikepdf.Page to modify
            operators: List of operators from ContentParser
            segments: List of segments with MCIDs from McidAllocator

        Modifies page content stream in place.
        """
        logger.info(f"[MARKER-INSERTER] Inserting markers for {len(segments)} segments")

        # Build new content stream with markers
        new_content = MarkerInserter._build_marked_content(operators, segments)

        # Replace page contents with new stream
        # Create a new stream object from the content bytes
        new_stream = pdf.make_stream(new_content)
        page.Contents = new_stream

        logger.info(f"[MARKER-INSERTER] Successfully inserted {len(segments)} BDC/EMC pairs")

    @staticmethod
    def _build_marked_content(
        operators: List[Dict[str, Any]],
        segments: List[Dict[str, Any]]
    ) -> pikepdf.Stream:
        """
        Build new content stream with BDC/EMC markers.

        Args:
            operators: Original operators
            segments: Segments with MCIDs

        Returns:
            New content stream with markers inserted
        """
        # Create lookups for fast segment access
        segments_by_start = {seg['start_index']: seg for seg in segments}
        segments_by_end = {seg['end_index']: seg for seg in segments}

        # Build new content as list of instructions
        instructions = []

        for op in operators:
            # Insert BDC before segment start
            if op['index'] in segments_by_start:
                segment = segments_by_start[op['index']]
                bdc_instruction = MarkerInserter._create_bdc_instruction(segment['mcid'])
                instructions.append(bdc_instruction)

            # Add original operator
            instructions.append((op['operands'], pikepdf.Operator(op['operator'])))

            # Insert EMC after segment end
            if op['index'] in segments_by_end:
                emc_instruction = ([], pikepdf.Operator('EMC'))
                instructions.append(emc_instruction)

        # Unparse instructions back to content stream
        content_stream = pikepdf.unparse_content_stream(instructions)

        return content_stream

    @staticmethod
    def _create_bdc_instruction(mcid: int) -> tuple:
        """
        Create BDC instruction with MCID.

        Format: /Span << /MCID n >> BDC

        Args:
            mcid: MCID number

        Returns:
            Tuple of (operands, operator) for pikepdf
        """
        # Create property dictionary: << /MCID n >>
        properties = pikepdf.Dictionary({
            '/MCID': mcid
        })

        # BDC operands: [/Span, properties_dict]
        operands = [
            pikepdf.Name('/Span'),
            properties
        ]

        return (operands, pikepdf.Operator('BDC'))

    @staticmethod
    def verify_markers(page: pikepdf.Page) -> Dict[str, int]:
        """
        Verify BDC/EMC markers in page content.

        Args:
            page: pikepdf.Page to verify

        Returns:
            Dictionary with marker counts:
            {
                'bdc_count': int,
                'emc_count': int,
                'matched': bool
            }
        """
        # Read page contents stream
        content_bytes = page.Contents.read_bytes()
        content_str = content_bytes.decode('latin-1', errors='ignore')

        bdc_count = content_str.count('BDC')
        emc_count = content_str.count('EMC')

        logger.info(f"[MARKER-INSERTER] Verification: {bdc_count} BDC, {emc_count} EMC")

        return {
            'bdc_count': bdc_count,
            'emc_count': emc_count,
            'matched': bdc_count == emc_count
        }


# Standalone test function
def test_marker_inserter():
    """Test marker inserter."""
    logging.basicConfig(level=logging.INFO)
    logger.info("[MARKER-INSERTER-TEST] Module loaded successfully")


if __name__ == '__main__':
    test_marker_inserter()
