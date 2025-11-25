#!/usr/bin/env python3
"""
Content stream parser for PDF operators.
Single responsibility: Parse content streams into structured operator list.
"""

import pikepdf
import logging
from typing import List, Dict, Any, Optional

logger = logging.getLogger(__name__)


class ContentParser:
    """Parse PDF content streams into operators with operands."""

    @staticmethod
    def parse_page_content(page: pikepdf.Page) -> List[Dict[str, Any]]:
        """
        Parse page content stream into list of operators.

        Args:
            page: pikepdf.Page object

        Returns:
            List of operator dictionaries:
            [
                {
                    'operator': str,
                    'operands': list,
                    'index': int
                },
                ...
            ]
        """
        logger.info(f"[CONTENT-PARSER] Parsing content stream")

        try:
            # Parse using pikepdf's content stream parser
            operators = []
            index = 0

            # Parse operators from content stream
            for operands, operator in pikepdf.parse_content_stream(page):
                operators.append({
                    'operator': str(operator),
                    'operands': [ContentParser._convert_operand(op) for op in operands],
                    'index': index
                })
                index += 1

            logger.info(f"[CONTENT-PARSER] Parsed {len(operators)} operators")
            return operators

        except Exception as e:
            logger.error(f"[CONTENT-PARSER] Failed to parse content: {e}")
            raise

    @staticmethod
    def _convert_operand(operand: Any) -> Any:
        """
        Convert pikepdf operand to serializable format.

        Args:
            operand: Pikepdf operand object

        Returns:
            Serializable value (str, int, float, list)
        """
        if isinstance(operand, pikepdf.Name):
            return str(operand)
        elif isinstance(operand, pikepdf.String):
            return str(operand)
        elif isinstance(operand, (int, float)):
            return operand
        elif isinstance(operand, pikepdf.Array):
            return [ContentParser._convert_operand(item) for item in operand]
        elif isinstance(operand, pikepdf.Dictionary):
            return {str(k): ContentParser._convert_operand(v) for k, v in operand.items()}
        else:
            return str(operand)

    @staticmethod
    def find_operator_indices(operators: List[Dict[str, Any]], operator_name: str) -> List[int]:
        """
        Find all indices of a specific operator.

        Args:
            operators: List of operator dicts from parse_page_content
            operator_name: Operator name to search for (e.g., 'BT', 'ET', 'Do')

        Returns:
            List of indices where operator appears
        """
        indices = [
            op['index']
            for op in operators
            if op['operator'] == operator_name
        ]

        logger.debug(f"[CONTENT-PARSER] Found {len(indices)} occurrences of '{operator_name}'")
        return indices

    @staticmethod
    def get_operator_range(operators: List[Dict[str, Any]], start_index: int, end_index: int) -> List[Dict[str, Any]]:
        """
        Get operators in a specific index range.

        Args:
            operators: List of operator dicts
            start_index: Start index (inclusive)
            end_index: End index (inclusive)

        Returns:
            List of operators in range
        """
        return [
            op for op in operators
            if start_index <= op['index'] <= end_index
        ]


# Standalone test function
def test_content_parser():
    """Test content parser with a sample PDF."""
    logging.basicConfig(level=logging.INFO)
    logger.info("[CONTENT-PARSER-TEST] Module loaded successfully")


if __name__ == '__main__':
    test_content_parser()
