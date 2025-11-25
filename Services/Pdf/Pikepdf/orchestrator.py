#!/usr/bin/env python3
"""
Pikepdf Structure Orchestrator - main coordinator for all modules.
Single responsibility: Coordinate structure tree building and MCID marking workflow.
"""

import sys
import json
import logging
from pathlib import Path

# Add parent directory to path for imports
sys.path.insert(0, str(Path(__file__).parent))

from Core.pdf_utils import PdfUtils
from Core.content_parser import ContentParser
from Structure.tree_cleaner import TreeCleaner
from Structure.element_builder import ElementBuilder
from Structure.hierarchy_builder import HierarchyBuilder
from Mcid.segment_detector import SegmentDetector
from Mcid.allocator import McidAllocator
from Mcid.marker_inserter import MarkerInserter
from Mcid.mcr_builder import McrBuilder

logger = logging.getLogger(__name__)


class PikepdfOrchestrator:
    """Main orchestrator for pikepdf structure operations."""

    def __init__(self, enable_mcid: bool = True):
        """
        Initialize orchestrator.

        Args:
            enable_mcid: Whether to enable MCID marking (default True)
        """
        self.enable_mcid = enable_mcid
        logger.info(f"[ORCHESTRATOR] Initialized (MCID marking: {enable_mcid})")

    def rebuild_structure(
        self,
        input_pdf_path: str,
        output_pdf_path: str,
        structure_tree_json: str
    ) -> dict:
        """
        Rebuild PDF structure tree with MCID marking.

        Args:
            input_pdf_path: Path to input PDF
            output_pdf_path: Path to save output PDF
            structure_tree_json: JSON string of StructureTree model

        Returns:
            Dictionary with results:
            {
                'success': bool,
                'elements_created': int,
                'mcr_kids_created': int,
                'bdc_emc_pairs': int,
                'error': str (if failed)
            }
        """
        logger.info("[ORCHESTRATOR] ========== STARTING STRUCTURE REBUILD ==========")
        logger.info(f"[ORCHESTRATOR] Input: {input_pdf_path}")
        logger.info(f"[ORCHESTRATOR] Output: {output_pdf_path}")

        result = {
            'success': False,
            'elements_created': 0,
            'mcr_kids_created': 0,
            'bdc_emc_pairs': 0
        }

        try:
            # Parse structure tree model
            structure_tree = json.loads(structure_tree_json)
            logger.info(f"[ORCHESTRATOR] Parsed structure tree: {len(structure_tree.get('nodes', []))} root nodes")

            # Open PDF
            pdf = PdfUtils.open_pdf(input_pdf_path)

            # Step 1: Remove old structure tree
            logger.info("[ORCHESTRATOR] Step 1: Removing old structure tree")
            TreeCleaner.remove_structure_tree(pdf)

            # Step 2: Build new structure tree
            logger.info("[ORCHESTRATOR] Step 2: Building structure tree")
            struct_root = ElementBuilder.build_struct_tree_root(pdf)
            element_map = HierarchyBuilder.build_hierarchy(
                pdf, struct_root, structure_tree.get('nodes', [])
            )
            result['elements_created'] = len(element_map)

            # Step 3: MCID marking (if enabled)
            if self.enable_mcid:
                logger.info("[ORCHESTRATOR] Step 3: Adding MCID markers")
                mcid_result = self._mark_content_with_mcids(pdf, structure_tree)
                result['mcr_kids_created'] = mcid_result['mcr_count']
                result['bdc_emc_pairs'] = mcid_result['marker_count']

            # Step 4: Validate
            logger.info("[ORCHESTRATOR] Step 4: Validating structure")
            validation = PdfUtils.validate_structure_tree(pdf)
            if not validation['valid']:
                raise Exception(f"Validation failed: {validation}")

            # Step 5: Save
            logger.info("[ORCHESTRATOR] Step 5: Saving PDF")
            PdfUtils.save_pdf(pdf, output_pdf_path)

            result['success'] = True
            logger.info("[ORCHESTRATOR] ========== STRUCTURE REBUILD COMPLETE ==========")
            logger.info(f"[ORCHESTRATOR] Created {result['elements_created']} elements, "
                       f"{result['mcr_kids_created']} MCR kids, "
                       f"{result['bdc_emc_pairs']} BDC/EMC pairs")

        except Exception as e:
            logger.error(f"[ORCHESTRATOR] Failed: {e}", exc_info=True)
            result['error'] = str(e)

        return result

    def _mark_content_with_mcids(self, pdf, structure_tree: dict) -> dict:
        """
        Mark content streams with MCID markers and create MCR kids.

        Args:
            pdf: pikepdf.Pdf object
            structure_tree: Parsed structure tree model

        Returns:
            Dictionary with MCID marking results
        """
        logger.info("[ORCHESTRATOR-MCID] Starting MCID marking")

        total_mcr_count = 0
        total_marker_count = 0

        # Process each page
        for page_index, page in enumerate(pdf.pages):
            logger.info(f"[ORCHESTRATOR-MCID] Processing page {page_index + 1}")

            # Parse content stream
            operators = ContentParser.parse_page_content(page)

            # Detect segments
            segments = SegmentDetector.detect_segments(operators)

            if len(segments) == 0:
                logger.info(f"[ORCHESTRATOR-MCID] No segments on page {page_index}")
                continue

            # Allocate MCIDs
            segments = McidAllocator.allocate_mcids(segments, start_mcid=total_marker_count)

            # Insert BDC/EMC markers
            MarkerInserter.insert_markers(pdf, page, operators, segments)

            # Verify markers
            verification = MarkerInserter.verify_markers(page)
            total_marker_count += verification['bdc_count']

            # Build element-to-MCID mapping
            # For now, simple sequential mapping to nodes
            element_mcid_map = self._build_element_mcid_map(
                structure_tree, page_index, segments
            )

            # Create MCR kids
            mcr_count = McrBuilder.add_mcr_kids(pdf, element_mcid_map)
            total_mcr_count += mcr_count

        logger.info(f"[ORCHESTRATOR-MCID] MCID marking complete: "
                   f"{total_mcr_count} MCRs, {total_marker_count} markers")

        return {
            'mcr_count': total_mcr_count,
            'marker_count': total_marker_count
        }

    def _build_element_mcid_map(
        self,
        structure_tree: dict,
        page_index: int,
        segments: list
    ) -> dict:
        """
        Build mapping from element IDs to MCID references.

        Args:
            structure_tree: Structure tree model
            page_index: Current page index
            segments: Segments with MCIDs allocated

        Returns:
            Dictionary mapping element IDs to MCID refs
        """
        # Simple sequential mapping for now
        # TODO: Use layout plan for more sophisticated mapping

        element_mcid_map = {}
        nodes = structure_tree.get('nodes', [])

        # Flatten node tree
        all_nodes = []
        def flatten(node_list):
            for node in node_list:
                all_nodes.append(node)
                if 'children' in node:
                    flatten(node['children'])
        flatten(nodes)

        # Map segments to nodes sequentially
        for i, segment in enumerate(segments):
            if i < len(all_nodes):
                node = all_nodes[i]
                node_id = node.get('id', f'/0/{i}')

                if node_id not in element_mcid_map:
                    element_mcid_map[node_id] = []

                element_mcid_map[node_id].append({
                    'page_index': page_index,
                    'mcid': segment['mcid']
                })

        return element_mcid_map


def main():
    """CLI entry point."""
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
    )

    if len(sys.argv) < 4:
        print("Usage: python orchestrator.py <input_pdf> <output_pdf> <structure_json_file>")
        sys.exit(1)

    input_pdf = sys.argv[1]
    output_pdf = sys.argv[2]
    structure_json_file = sys.argv[3]

    # Read structure JSON
    with open(structure_json_file, 'r') as f:
        structure_json = f.read()

    # Run orchestrator
    orchestrator = PikepdfOrchestrator(enable_mcid=True)
    result = orchestrator.rebuild_structure(input_pdf, output_pdf, structure_json)

    # Print result
    print(json.dumps(result, indent=2))

    sys.exit(0 if result['success'] else 1)


if __name__ == '__main__':
    main()
