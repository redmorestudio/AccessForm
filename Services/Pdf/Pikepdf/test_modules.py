#!/usr/bin/env python3
"""
Test that all pikepdf modules load correctly.
"""

import sys
import logging

logging.basicConfig(level=logging.INFO, format='%(message)s')
logger = logging.getLogger(__name__)


def test_module_imports():
    """Test that all modules can be imported."""

    logger.info("="*60)
    logger.info("TESTING PIKEPDF MODULE IMPORTS")
    logger.info("="*60)

    success_count = 0
    fail_count = 0

    # Core modules
    modules_to_test = [
        ('Core.pdf_utils', 'PdfUtils'),
        ('Core.content_parser', 'ContentParser'),
        ('Structure.tree_cleaner', 'TreeCleaner'),
        ('Structure.element_builder', 'ElementBuilder'),
        ('Structure.hierarchy_builder', 'HierarchyBuilder'),
        ('Mcid.segment_detector', 'SegmentDetector'),
        ('Mcid.allocator', 'McidAllocator'),
        ('Mcid.marker_inserter', 'MarkerInserter'),
        ('Mcid.mcr_builder', 'McrBuilder'),
        ('orchestrator', 'PikepdfOrchestrator'),
    ]

    for module_name, class_name in modules_to_test:
        try:
            module = __import__(module_name, fromlist=[class_name])
            cls = getattr(module, class_name)
            logger.info(f"✅ {module_name}.{class_name}")
            success_count += 1
        except Exception as e:
            logger.error(f"❌ {module_name}.{class_name}: {e}")
            fail_count += 1

    logger.info("="*60)
    logger.info(f"RESULTS: {success_count} passed, {fail_count} failed")
    logger.info("="*60)

    return fail_count == 0


def test_pikepdf_available():
    """Test that pikepdf is installed."""
    logger.info("\nTesting pikepdf installation...")
    try:
        import pikepdf
        version = pikepdf.__version__
        logger.info(f"✅ pikepdf version {version} installed")
        return True
    except ImportError as e:
        logger.error(f"❌ pikepdf not installed: {e}")
        logger.error("   Install with: pip3 install pikepdf")
        return False


if __name__ == '__main__':
    # Test pikepdf first
    if not test_pikepdf_available():
        sys.exit(1)

    # Test module imports
    if test_module_imports():
        logger.info("\n🎉 All modules loaded successfully!")
        sys.exit(0)
    else:
        logger.error("\n❌ Some modules failed to load")
        sys.exit(1)
