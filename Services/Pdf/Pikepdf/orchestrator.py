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
                'error': str (if failed),
                'warnings': list (if any)
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
            # Validate inputs
            if not input_pdf_path or not isinstance(input_pdf_path, str):
                raise ValueError("input_pdf_path must be a non-empty string")

            if not output_pdf_path or not isinstance(output_pdf_path, str):
                raise ValueError("output_pdf_path must be a non-empty string")

            if not structure_tree_json or not isinstance(structure_tree_json, str):
                raise ValueError("structure_tree_json must be a non-empty string")

            from pathlib import Path
            if not Path(input_pdf_path).exists():
                raise FileNotFoundError(f"Input PDF not found: {input_pdf_path}")

            # Parse structure tree model
            try:
                structure_tree = json.loads(structure_tree_json)
            except json.JSONDecodeError as e:
                raise ValueError(f"Invalid JSON in structure_tree_json: {e}")
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

                # Propagate errors/warnings from MCID marking
                if 'errors' in mcid_result:
                    result['warnings'] = mcid_result['errors']
                    logger.warning(f"[ORCHESTRATOR] MCID marking had {len(mcid_result['errors'])} errors")

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
        errors = []

        # Process each page
        for page_index, page in enumerate(pdf.pages):
            try:
                logger.info(f"[ORCHESTRATOR-MCID] Processing page {page_index + 1}")

                # Parse content stream
                try:
                    operators = ContentParser.parse_page_content(page)
                except Exception as e:
                    logger.error(f"[ORCHESTRATOR-MCID] Failed to parse page {page_index + 1}: {e}")
                    errors.append(f"Page {page_index + 1} parse error: {e}")
                    continue

                # Detect segments
                try:
                    segments = SegmentDetector.detect_segments(operators)
                except Exception as e:
                    logger.error(f"[ORCHESTRATOR-MCID] Failed to detect segments on page {page_index + 1}: {e}")
                    errors.append(f"Page {page_index + 1} segment detection error: {e}")
                    continue

                if len(segments) == 0:
                    logger.info(f"[ORCHESTRATOR-MCID] No segments on page {page_index + 1}")
                    continue

                # Allocate MCIDs
                try:
                    segments = McidAllocator.allocate_mcids(segments, start_mcid=total_marker_count)
                except Exception as e:
                    logger.error(f"[ORCHESTRATOR-MCID] Failed to allocate MCIDs on page {page_index + 1}: {e}")
                    errors.append(f"Page {page_index + 1} MCID allocation error: {e}")
                    continue

                # Insert BDC/EMC markers
                try:
                    MarkerInserter.insert_markers(pdf, page, operators, segments)
                except Exception as e:
                    logger.error(f"[ORCHESTRATOR-MCID] Failed to insert markers on page {page_index + 1}: {e}")
                    errors.append(f"Page {page_index + 1} marker insertion error: {e}")
                    continue

                # Verify markers
                try:
                    verification = MarkerInserter.verify_markers(page)
                    total_marker_count += verification['bdc_count']
                except Exception as e:
                    logger.warning(f"[ORCHESTRATOR-MCID] Failed to verify markers on page {page_index + 1}: {e}")
                    # Non-fatal, continue

                # Build element-to-MCID mapping
                try:
                    element_mcid_map = self._build_element_mcid_map(
                        structure_tree, page_index, segments
                    )
                except Exception as e:
                    logger.error(f"[ORCHESTRATOR-MCID] Failed to build MCID map on page {page_index + 1}: {e}")
                    errors.append(f"Page {page_index + 1} MCID mapping error: {e}")
                    continue

                # Create MCR kids
                try:
                    mcr_count = McrBuilder.add_mcr_kids(pdf, element_mcid_map)
                    total_mcr_count += mcr_count
                except Exception as e:
                    logger.error(f"[ORCHESTRATOR-MCID] Failed to add MCR kids on page {page_index + 1}: {e}")
                    errors.append(f"Page {page_index + 1} MCR creation error: {e}")
                    continue

            except Exception as e:
                logger.error(f"[ORCHESTRATOR-MCID] Unexpected error on page {page_index + 1}: {e}", exc_info=True)
                errors.append(f"Page {page_index + 1} unexpected error: {e}")
                continue

        logger.info(f"[ORCHESTRATOR-MCID] MCID marking complete: "
                   f"{total_mcr_count} MCRs, {total_marker_count} markers, {len(errors)} errors")

        result = {
            'mcr_count': total_mcr_count,
            'marker_count': total_marker_count
        }

        if errors:
            result['errors'] = errors
            logger.warning(f"[ORCHESTRATOR-MCID] Encountered {len(errors)} errors during MCID marking")

        return result

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

        # Flatten node tree - ONLY INCLUDE LEAF NODES (no children)
        # This prevents mapping content to structural containers
        all_nodes = []
        total_nodes = 0

        def flatten(node_list):
            nonlocal total_nodes
            for node in node_list:
                total_nodes += 1
                children = node.get('children', [])

                if len(children) == 0:
                    # Leaf node - can receive MCID
                    all_nodes.append(node)
                    logger.debug(f"[ORCHESTRATOR] Leaf node: {node.get('role')} (id={node.get('id')})")
                else:
                    # Container node - skip but recurse into children
                    logger.debug(f"[ORCHESTRATOR] Skipping container: {node.get('role')} (id={node.get('id')}) - has {len(children)} children")
                    flatten(children)

        flatten(nodes)

        # Map segments to nodes sequentially
        # All nodes now have hierarchical IDs generated by C# (e.g., /0/0, /0/1/2, etc.)
        logger.info(f"[ORCHESTRATOR] Structure tree: {total_nodes} total nodes, {len(all_nodes)} leaf nodes")
        logger.info(f"[ORCHESTRATOR] Mapping {len(segments)} segments to {len(all_nodes)} leaf nodes")

        for i, segment in enumerate(segments):
            if i < len(all_nodes):
                node = all_nodes[i]
                # Node IDs are now always present (auto-generated in C# if not explicitly set)
                node_id = node.get('id')

                if not node_id:
                    logger.error(f"[ORCHESTRATOR] Node {i} has no ID! This should not happen.")
                    node_id = '/0'  # Emergency fallback
            else:
                # More segments than leaf nodes - map excess to root "/0"
                # This handles graphics/extra content not covered by AI structure analysis
                node_id = '/0'
                logger.debug(f"[ORCHESTRATOR] Segment {i} ({segment.get('type')}) mapped to fallback '/0' (no corresponding leaf node)")

            if node_id not in element_mcid_map:
                element_mcid_map[node_id] = []

            element_mcid_map[node_id].append({
                'page_index': page_index,
                'mcid': segment['mcid']
            })

        if len(segments) > len(all_nodes):
            logger.warning(f"[ORCHESTRATOR] {len(segments) - len(all_nodes)} segments mapped to fallback '/0' "
                          f"(structure tree has {len(all_nodes)} leaf nodes, but {len(segments)} content segments)")

        return element_mcid_map


def main():
    """CLI entry point."""
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
        stream=sys.stderr  # Send all logs to stderr, keep stdout for JSON only
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
