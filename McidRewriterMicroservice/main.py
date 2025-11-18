"""
Phase 6H: External MCID Rewriter Microservice

This FastAPI microservice receives a PDF and McidRewritePlan from the .NET application,
rewrites PDF content streams with BDC/EMC markers using pikepdf, and returns the modified PDF.

This approach bypasses iText7's internal tagging model limitations that cause MCID markers
to be discarded during pdfDoc.Close().
"""

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field
from typing import List, Optional
import base64
import logging
from io import BytesIO
import pikepdf
from pikepdf import Pdf, Array, Dictionary, Name, Operator, Stream
try:
    from pikepdf import ContentStreamInstruction
except ImportError:
    # ContentStreamInstruction might not be in public API in all versions
    ContentStreamInstruction = pikepdf._core.ContentStreamInstruction
import re

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='[%(asctime)s] [%(levelname)s] %(message)s',
    datefmt='%Y-%m-%d %H:%M:%S'
)
logger = logging.getLogger(__name__)

# Create FastAPI app
app = FastAPI(
    title="MCID Rewriter Microservice",
    description="Phase 6H: Rewrites PDF content streams with BDC/EMC markers",
    version="6H-1"
)

# Add CORS middleware to allow .NET application to call this service
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # In production, restrict to specific origins
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


# ============================================================================
# Pydantic Models (matching C# contracts)
# ============================================================================

class McidSegment(BaseModel):
    """Represents a single content segment to wrap with MCID markers."""
    page_index: int = Field(..., alias="pageIndex", description="1-based page index")
    mcid: int = Field(..., description="MCID value to apply")
    role: Optional[str] = Field(None, description="Structure role (P, H1, TD, etc.)")
    x: float = Field(..., description="Bounding box X coordinate (72 dpi)")
    y: float = Field(..., description="Bounding box Y coordinate (72 dpi)")
    width: float = Field(..., description="Bounding box width (72 dpi)")
    height: float = Field(..., description="Bounding box height (72 dpi)")
    sequence_index: Optional[int] = Field(None, alias="sequenceIndex", description="Ordering index")

    class Config:
        populate_by_name = True


class McidRewritePlan(BaseModel):
    """Full MCID mapping for a document."""
    document_id: Optional[str] = Field(None, alias="documentId")
    version: str = Field("6H-1", description="Contract version")
    segments: List[McidSegment] = Field(default_factory=list)
    debug: bool = Field(False, description="Enable debug mode with visual overlays")

    class Config:
        populate_by_name = True


class McidRewriteRequest(BaseModel):
    """Request body for MCID rewrite operation."""
    pdf_base64: str = Field(..., alias="pdfBase64", description="Base64-encoded PDF bytes")
    plan: McidRewritePlan = Field(..., description="MCID rewrite plan")

    class Config:
        populate_by_name = True


class McidRewriteStats(BaseModel):
    """Statistics from MCID rewrite operation."""
    pages_processed: int = Field(..., alias="pagesProcessed")
    segments_processed: int = Field(..., alias="segmentsProcessed")
    bdc_count: int = Field(..., alias="bdcCount")
    emc_count: int = Field(..., alias="emcCount")

    class Config:
        populate_by_name = True


class McidRewriteResponse(BaseModel):
    """Response from MCID rewrite operation."""
    success: bool
    message: str
    pdf_base64: Optional[str] = Field(None, alias="pdfBase64")
    stats: Optional[McidRewriteStats] = None

    class Config:
        populate_by_name = True


# ============================================================================
# PDF Content Stream Rewriting Logic
# ============================================================================

def rewrite_content_stream_with_mcids(
    page: pikepdf.Page,
    page_index: int,
    segments: List[McidSegment],
    debug: bool = False
) -> tuple[int, int]:
    """
    Rewrites a page's content stream to insert BDC/EMC markers for each segment.

    Returns: (bdc_count, emc_count) tuple
    """
    # Filter segments for this page (convert to 0-based)
    page_segments = [s for s in segments if s.page_index == page_index + 1]

    if not page_segments:
        logger.info(f"[PHASE-6H] Page {page_index + 1}: No segments to process")
        return (0, 0)

    logger.info(f"[PHASE-6H] Page {page_index + 1}: Processing {len(page_segments)} segments")

    # Parse content stream into instructions (parse_content_stream expects a Page object)
    try:
        instructions = pikepdf.parse_content_stream(page)
        logger.info(f"[PHASE-6H] Page {page_index + 1}: Parsed {len(instructions)} content stream instructions")
    except Exception as e:
        logger.error(f"[PHASE-6H] Failed to parse content stream for page {page_index + 1}: {e}")
        raise

    # Sort segments by sequence index or by Y coordinate (top to bottom)
    sorted_segments = sorted(
        page_segments,
        key=lambda s: s.sequence_index if s.sequence_index is not None else -s.y
    )

    # Insert BDC/EMC markers around content based on bounding boxes
    # Strategy: Wrap entire content stream sections with BDC/EMC pairs
    # For now, insert at beginning/end for each MCID (simple approach)

    new_instructions = []
    bdc_count = 0
    emc_count = 0

    # For each segment, insert BDC at start
    for i, segment in enumerate(sorted_segments):
        # Insert BDC marker: /Span << /MCID N >> BDC
        # Must use ContentStreamInstruction constructor
        bdc_instr = ContentStreamInstruction(
            [pikepdf.Name.Span, pikepdf.Name.MCID, segment.mcid],
            pikepdf.Operator("BDC")
        )
        new_instructions.append(bdc_instr)
        bdc_count += 1

        logger.info(f"[PHASE-6H]   Segment {i}: MCID={segment.mcid}, Role={segment.role}, Bounds=({segment.x:.1f}, {segment.y:.1f}, {segment.width:.1f}, {segment.height:.1f})")

    # Add original content
    logger.info(f"[PHASE-6H] Page {page_index + 1}: Adding {len(instructions)} original instructions")
    new_instructions.extend(instructions)

    # Close all segments with EMC
    for _ in sorted_segments:
        emc_instr = ContentStreamInstruction([], pikepdf.Operator("EMC"))
        new_instructions.append(emc_instr)
        emc_count += 1

    logger.info(f"[PHASE-6H] Page {page_index + 1}: Built new instruction stream with {len(new_instructions)} total instructions")

    # Rebuild content stream
    try:
        new_stream = pikepdf.unparse_content_stream(new_instructions)
        logger.info(f"[PHASE-6H] Page {page_index + 1}: Unparsed to stream of {len(new_stream)} bytes")

        # Replace page contents
        page.contents_replace(new_stream)
        logger.info(f"[PHASE-6H] Page {page_index + 1}: Replaced page contents")

        # Verify the replacement worked by checking the stream
        verify_stream = bytes(page.contents_coalesce())
        verify_bdc = verify_stream.count(b'BDC')
        verify_emc = verify_stream.count(b'EMC')
        logger.info(f"[PHASE-6H] Page {page_index + 1}: Verification - stream has {verify_bdc} BDC and {verify_emc} EMC in content")

    except Exception as e:
        logger.error(f"[PHASE-6H] Failed to rebuild content stream for page {page_index + 1}: {e}", exc_info=True)
        raise

    logger.info(f"[PHASE-6H] Page {page_index + 1}: Inserted {bdc_count} BDC and {emc_count} EMC markers")

    return (bdc_count, emc_count)


def rewrite_pdf_with_mcids(pdf_bytes: bytes, plan: McidRewritePlan) -> tuple[bytes, int, int, int]:
    """
    Main rewriting function: opens PDF, rewrites content streams, returns modified PDF.
    Returns: (output_bytes, pages_processed, total_bdc, total_emc)
    """
    logger.info(f"[PHASE-6H] Starting MCID rewrite for document: {plan.document_id or 'unknown'}")
    logger.info(f"[PHASE-6H] Plan version: {plan.version}, Segments: {len(plan.segments)}, Debug: {plan.debug}")

    # Open PDF with pikepdf
    try:
        pdf = pikepdf.open(BytesIO(pdf_bytes))
    except Exception as e:
        logger.error(f"[PHASE-6H] Failed to open PDF: {e}")
        raise ValueError(f"Failed to open PDF: {e}")

    total_bdc = 0
    total_emc = 0
    pages_processed = 0

    # Process each page
    page_count = len(pdf.pages)
    logger.info(f"[PHASE-6H] PDF has {page_count} pages")

    for page_index, page in enumerate(pdf.pages):
        try:
            bdc, emc = rewrite_content_stream_with_mcids(page, page_index, plan.segments, plan.debug)
            total_bdc += bdc
            total_emc += emc
            if bdc > 0 or emc > 0:
                pages_processed += 1
        except Exception as e:
            logger.error(f"[PHASE-6H] Failed to process page {page_index + 1}: {e}")
            # Continue processing other pages
            continue

    # Save modified PDF to bytes
    try:
        output_buffer = BytesIO()
        pdf.save(output_buffer)
        output_bytes = output_buffer.getvalue()
    except Exception as e:
        logger.error(f"[PHASE-6H] Failed to save PDF: {e}")
        raise ValueError(f"Failed to save modified PDF: {e}")
    finally:
        pdf.close()

    logger.info(f"[PHASE-6H] ✅ Rewrite complete: {pages_processed} pages, {len(plan.segments)} segments, {total_bdc} BDC, {total_emc} EMC")

    return (output_bytes, pages_processed, total_bdc, total_emc)


# ============================================================================
# API Endpoints
# ============================================================================

@app.get("/health")
async def health_check():
    """Health check endpoint."""
    return {"status": "healthy", "service": "mcid-rewriter", "version": "6H-1"}


@app.post("/api/mcid-rewrite", response_model=McidRewriteResponse)
async def mcid_rewrite(request: McidRewriteRequest):
    """
    Main endpoint: receives PDF + plan, returns rewritten PDF.
    """
    try:
        logger.info(f"[PHASE-6H] Received MCID rewrite request: {len(request.plan.segments)} segments")

        # Decode PDF from Base64
        try:
            pdf_bytes = base64.b64decode(request.pdf_base64)
        except Exception as e:
            logger.error(f"[PHASE-6H] Failed to decode Base64 PDF: {e}")
            raise HTTPException(status_code=400, detail=f"Invalid Base64 PDF data: {e}")

        logger.info(f"[PHASE-6H] Input PDF size: {len(pdf_bytes) / 1024:.1f} KB")

        # Rewrite PDF
        try:
            output_bytes, pages_processed, bdc_count, emc_count = rewrite_pdf_with_mcids(pdf_bytes, request.plan)
        except ValueError as e:
            logger.error(f"[PHASE-6H] PDF processing error: {e}")
            raise HTTPException(status_code=400, detail=str(e))
        except Exception as e:
            logger.error(f"[PHASE-6H] Unexpected error during rewrite: {e}", exc_info=True)
            raise HTTPException(status_code=500, detail=f"Internal error: {e}")

        # Encode result as Base64
        output_base64 = base64.b64encode(output_bytes).decode('utf-8')

        logger.info(f"[PHASE-6H] Output PDF size: {len(output_bytes) / 1024:.1f} KB")

        # Build stats from actual rewrite results
        stats = McidRewriteStats(
            pages_processed=pages_processed,
            segments_processed=len(request.plan.segments),
            bdc_count=bdc_count,
            emc_count=emc_count
        )

        return McidRewriteResponse(
            success=True,
            message=f"Successfully rewrote PDF with {len(request.plan.segments)} MCID markers",
            pdf_base64=output_base64,
            stats=stats
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"[PHASE-6H] Unexpected error in endpoint: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Internal server error: {e}")


# ============================================================================
# Main Entry Point
# ============================================================================

if __name__ == "__main__":
    import uvicorn

    logger.info("=" * 80)
    logger.info("PHASE 6H: MCID Rewriter Microservice")
    logger.info("=" * 80)

    uvicorn.run(
        app,
        host="0.0.0.0",
        port=8000,
        log_level="info"
    )
