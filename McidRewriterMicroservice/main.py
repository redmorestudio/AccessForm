"""
Phase 6J: External MCID Rewriter Microservice with Improved Geometry & Image Support

This FastAPI microservice receives a PDF and McidRewritePlan from the .NET application,
rewrites PDF content streams with BDC/EMC markers using pikepdf, with:
- Accurate text geometry using font metrics
- Image support via Do operator with CTM tracking
- Proper content segmentation by bounding box

This approach bypasses iText7's internal tagging model limitations.
"""

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field
from typing import List, Optional, Dict, Tuple, Any
import base64
import logging
from io import BytesIO
import pikepdf
from pikepdf import Pdf, Array, Dictionary, Name, Operator, Stream
try:
    from pikepdf import ContentStreamInstruction
except ImportError:
    ContentStreamInstruction = pikepdf._core.ContentStreamInstruction
import numpy as np

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
    description="Phase 6J: Rewrites PDF content streams with BDC/EMC markers using geometry",
    version="6J-1"
)

# Add CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
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
    role: Optional[str] = Field(None, description="Structure role (P, H1, Figure, etc.)")
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
    version: str = Field("6J-1", description="Contract version")
    segments: List[McidSegment] = Field(default_factory=list)
    debug: bool = Field(False, description="Enable debug mode")

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
# Phase 6J: Font Metrics & Geometry Classes
# ============================================================================

class FontInfo:
    """Font information for width calculation."""
    def __init__(self, name: str, avg_width_glyph_units: float = 500.0, units_per_em: float = 1000.0):
        self.name = name
        self.avg_width = avg_width_glyph_units
        self.units_per_em = units_per_em


class FontInfoCache:
    """Caches font metrics per page."""
    def __init__(self):
        self.fonts: Dict[str, FontInfo] = {}

    def load_from_page(self, page: pikepdf.Page):
        """Extract font information from page resources."""
        try:
            if '/Resources' not in page:
                return

            resources = page.Resources
            if '/Font' not in resources:
                return

            font_dict = resources['/Font']
            for font_name, font_obj in font_dict.items():
                try:
                    font_name_str = str(font_name)

                    # Get font metrics
                    avg_width = 500.0  # default

                    # Try to read Widths array
                    if '/Widths' in font_obj:
                        widths_array = font_obj['/Widths']
                        if isinstance(widths_array, Array) and len(widths_array) > 0:
                            # Compute average
                            total = sum(float(w) for w in widths_array if isinstance(w, (int, float)))
                            if len(widths_array) > 0:
                                avg_width = total / len(widths_array)

                    # Store font info
                    self.fonts[font_name_str] = FontInfo(font_name_str, avg_width)
                    logger.debug(f"[FONT] Loaded {font_name_str}: avg_width={avg_width:.1f}")

                except Exception as e:
                    logger.debug(f"[FONT] Could not parse font {font_name}: {e}")
                    continue

        except Exception as e:
            logger.debug(f"[FONT] Error loading fonts from page: {e}")

    def get_font(self, font_name: str) -> Optional[FontInfo]:
        """Get font info by name."""
        return self.fonts.get(font_name)


class TextState:
    """Tracks text state (position, font) between BT/ET."""
    def __init__(self):
        self.in_text = False
        self.x = 0.0
        self.y = 0.0
        self.font_size = 12.0
        self.font_name: Optional[str] = None


class GraphicsState:
    """Graphics state including CTM."""
    def __init__(self):
        # 3x3 transformation matrix (identity initially)
        self.ctm = np.identity(3, dtype=float)


class GraphicsStateStack:
    """Stack for q/Q graphics state save/restore."""
    def __init__(self):
        self.stack = [GraphicsState()]

    @property
    def current(self) -> GraphicsState:
        return self.stack[-1]

    def push(self):
        """Save graphics state (q operator)."""
        new_state = GraphicsState()
        new_state.ctm = self.current.ctm.copy()
        self.stack.append(new_state)

    def pop(self):
        """Restore graphics state (Q operator)."""
        if len(self.stack) > 1:
            self.stack.pop()


class EnhancedInstruction:
    """Enhanced instruction with geometry information."""
    def __init__(self, operands: List, operator: str):
        self.operands = operands
        self.operator = str(operator)

        # Geometry fields
        self.x: Optional[float] = None
        self.y: Optional[float] = None
        self.bbox: Optional[Tuple[float, float, float, float]] = None  # (x, y, w, h)
        self.ctm: Optional[np.ndarray] = None

        # Image fields
        self.is_image: bool = False
        self.xobject_name: Optional[str] = None


# ============================================================================
# Phase 6J: Geometry & Segmentation Functions
# ============================================================================

def bbox_overlaps(seg_bbox: Tuple[float, float, float, float],
                  inst_bbox: Tuple[float, float, float, float],
                  y_tolerance: float = 2.0) -> bool:
    """Check if instruction bbox overlaps with segment bbox."""
    sx, sy, sw, sh = seg_bbox
    ix, iy, iw, ih = inst_bbox

    seg_x1, seg_x2 = sx, sx + sw
    seg_y1, seg_y2 = sy, sy + sh

    inst_x1, inst_x2 = ix, ix + iw
    inst_y1, inst_y2 = iy, iy + ih

    horiz = (inst_x2 >= seg_x1) and (inst_x1 <= seg_x2)
    vert = (inst_y2 + y_tolerance >= seg_y1) and (inst_y1 - y_tolerance <= seg_y2)

    return horiz and vert


def compute_text_width(text_data: Any, font_info: Optional[FontInfo], font_size: float) -> float:
    """Compute approximate text width."""
    # Handle both string and array (TJ) operands
    if isinstance(text_data, (str, bytes)):
        text_str = str(text_data) if isinstance(text_data, str) else text_data.decode('latin-1', errors='ignore')
        char_count = len(text_str)
    elif isinstance(text_data, Array):
        # TJ array - sum character counts from strings
        char_count = 0
        for item in text_data:
            if isinstance(item, (str, bytes)):
                text_str = str(item) if isinstance(item, str) else item.decode('latin-1', errors='ignore')
                char_count += len(text_str)
    else:
        char_count = 0

    if char_count == 0:
        return 0.0

    # Use font metrics if available
    if font_info:
        glyph_width = font_info.avg_width
        units_per_em = font_info.units_per_em
        text_width = (char_count * glyph_width / units_per_em) * font_size
    else:
        # Fallback estimate
        text_width = max(50.0, char_count * font_size * 0.5)

    return text_width


def parse_instructions_with_geometry(
    page: pikepdf.Page,
    font_cache: FontInfoCache
) -> List[EnhancedInstruction]:
    """
    Parse content stream and populate geometry for text and images.
    Returns list of EnhancedInstruction objects.
    """
    # Parse content stream
    raw_instructions = pikepdf.parse_content_stream(page)

    enhanced_instructions = []
    text_state = TextState()
    gfx_stack = GraphicsStateStack()

    for raw_instr in raw_instructions:
        operands = raw_instr.operands if hasattr(raw_instr, 'operands') else []
        operator = str(raw_instr.operator) if hasattr(raw_instr, 'operator') else str(raw_instr)

        enhanced = EnhancedInstruction(operands, operator)

        # Track graphics state (q/Q/cm)
        if operator == 'q':
            gfx_stack.push()
        elif operator == 'Q':
            gfx_stack.pop()
        elif operator == 'cm' and len(operands) == 6:
            # Concatenate matrix
            a, b, c, d, e, f = [float(op) for op in operands]
            cm_matrix = np.array([[a, b, 0],
                                  [c, d, 0],
                                  [e, f, 1]], dtype=float)
            gfx_stack.current.ctm = gfx_stack.current.ctm @ cm_matrix

        # Track text state
        if operator == 'BT':
            text_state.in_text = True
        elif operator == 'ET':
            text_state.in_text = False
        elif operator == 'Tm' and len(operands) == 6:
            # Text matrix
            e, f = float(operands[4]), float(operands[5])
            text_state.x = e
            text_state.y = f
        elif operator in ('Td', 'TD') and len(operands) == 2:
            # Text delta
            tx, ty = float(operands[0]), float(operands[1])
            text_state.x += tx
            text_state.y += ty
        elif operator == 'Tf' and len(operands) == 2:
            # Font selection
            text_state.font_name = str(operands[0])
            text_state.font_size = float(operands[1])

        # Compute geometry for text instructions
        elif operator in ('Tj', 'TJ') and text_state.in_text:
            font_info = font_cache.get_font(text_state.font_name) if text_state.font_name else None
            text_data = operands[0] if len(operands) > 0 else ""

            text_width = compute_text_width(text_data, font_info, text_state.font_size)

            enhanced.x = text_state.x
            enhanced.y = text_state.y
            enhanced.bbox = (enhanced.x, enhanced.y, text_width, text_state.font_size)

        # Detect and compute geometry for images (Do operator)
        elif operator == 'Do' and len(operands) == 1:
            xobject_name = str(operands[0])

            try:
                # Get XObject from page resources
                if '/Resources' in page and '/XObject' in page.Resources:
                    xobjects = page.Resources['/XObject']
                    if xobject_name in xobjects:
                        xobj = xobjects[xobject_name]

                        # Check if it's an image
                        if '/Subtype' in xobj and xobj['/Subtype'] == '/Image':
                            # Get image dimensions
                            img_width = float(xobj.get('/Width', 1))
                            img_height = float(xobj.get('/Height', 1))

                            # Transform image corners using CTM
                            corners = [
                                [0, 0, 1],
                                [img_width, 0, 1],
                                [img_width, img_height, 1],
                                [0, img_height, 1]
                            ]

                            transformed_corners = []
                            for corner in corners:
                                v = np.array(corner, dtype=float)
                                transformed = gfx_stack.current.ctm @ v
                                transformed_corners.append((transformed[0], transformed[1]))

                            # Compute bounding box
                            xs = [c[0] for c in transformed_corners]
                            ys = [c[1] for c in transformed_corners]

                            ix = min(xs)
                            iy = min(ys)
                            iw = max(xs) - ix
                            ih = max(ys) - iy

                            enhanced.x = ix
                            enhanced.y = iy
                            enhanced.bbox = (ix, iy, iw, ih)
                            enhanced.is_image = True
                            enhanced.xobject_name = xobject_name
                            enhanced.ctm = gfx_stack.current.ctm.copy()

                            logger.debug(f"[IMAGE] Detected {xobject_name}: bbox=({ix:.1f}, {iy:.1f}, {iw:.1f}, {ih:.1f})")

            except Exception as e:
                logger.debug(f"[IMAGE] Could not process XObject {xobject_name}: {e}")

        enhanced_instructions.append(enhanced)

    return enhanced_instructions


def assign_instructions_to_segments(
    instructions: List[EnhancedInstruction],
    segments: List[McidSegment]
) -> Dict[int, List[int]]:
    """
    Map instruction indices to segment MCIDs based on bbox overlap.
    Returns: {mcid: [instruction_indices]}
    """
    segment_instruction_indices: Dict[int, List[int]] = {}

    # Sort segments by sequence index
    sorted_segments = sorted(segments, key=lambda s: s.sequence_index if s.sequence_index is not None else 0)

    for idx, instr in enumerate(instructions):
        if instr.bbox is None:
            continue

        # Check overlap with each segment
        for seg in sorted_segments:
            # Skip zero-rect segments (nodes without bounds like table headers)
            if seg.width == 0 and seg.height == 0:
                continue

            seg_bbox = (seg.x, seg.y, seg.width, seg.height)

            # For images, only assign to Figure segments
            if instr.is_image and seg.role != "Figure":
                continue

            if bbox_overlaps(seg_bbox, instr.bbox):
                # Assign to this segment
                if seg.mcid not in segment_instruction_indices:
                    segment_instruction_indices[seg.mcid] = []
                segment_instruction_indices[seg.mcid].append(idx)

                # Assign to first matching segment only
                break

    return segment_instruction_indices


def wrap_segments_with_mcid(
    instructions: List[EnhancedInstruction],
    segment_instruction_indices: Dict[int, List[int]]
) -> List:
    """
    Insert BDC/EMC markers around instruction groups for each segment.
    Groups consecutive bbox-overlapping instructions to avoid nested contentItems.
    Also wraps ALL unmarked content as artifacts to satisfy PDF/UA 7.1-3.
    Returns list of pikepdf ContentStreamInstruction objects.
    """
    # Helper function to group consecutive indices
    def group_consecutive(indices: List[int]) -> List[tuple]:
        """Groups consecutive indices into (start, end, mcid) tuples."""
        if not indices:
            return []

        sorted_indices = sorted(indices)
        groups = []
        group_start = sorted_indices[0]
        prev_idx = sorted_indices[0]

        for idx in sorted_indices[1:]:
            if idx != prev_idx + 1:
                # End current group
                groups.append((group_start, prev_idx))
                group_start = idx
            prev_idx = idx

        # Add final group
        groups.append((group_start, prev_idx))
        return groups

    # Build wrapped segments by grouping consecutive instructions for each MCID
    # This avoids wrapping entire min-to-max ranges that may contain non-overlapping instructions
    wrapped_segments = []
    for mcid, indices in segment_instruction_indices.items():
        if not indices:
            continue

        # Group consecutive indices for this MCID
        groups = group_consecutive(indices)
        for start, end in groups:
            wrapped_segments.append((start, end, mcid))

    # Build marked instruction indices set (only bbox-overlapping instructions)
    marked_indices = set()
    for indices in segment_instruction_indices.values():
        marked_indices.update(indices)

    # Sort segments by start index
    wrapped_segments.sort(key=lambda t: (t[0], t[1]))

    result = []
    current_segment_idx = 0
    current_segment = wrapped_segments[current_segment_idx] if wrapped_segments else None
    in_artifact = False

    for i, instr in enumerate(instructions):
        is_marked = i in marked_indices

        # Close artifact block if we're entering a marked instruction
        if in_artifact and is_marked:
            emc_instr = ContentStreamInstruction([], pikepdf.Operator("EMC"))
            result.append(emc_instr)
            in_artifact = False

        # Open artifact block if this instruction is unmarked
        if not is_marked and not in_artifact:
            # Don't wrap structure operators - only actual content
            if instr.operator not in ('q', 'Q', 'BT', 'ET', 'cm', 'gs', 'CS', 'cs', 'G', 'g', 'RG', 'rg', 'K', 'k', 'w', 'J', 'j', 'M', 'd', 'ri', 'i', 'gs'):
                bdc_instr = ContentStreamInstruction(
                    [pikepdf.Name.Artifact],
                    pikepdf.Operator("BMC")
                )
                result.append(bdc_instr)
                in_artifact = True

        # Insert BDC if this is the start of a consecutive group
        while current_segment is not None and i == current_segment[0]:
            _, _, mcid = current_segment
            bdc_instr = ContentStreamInstruction(
                [pikepdf.Name.Span, pikepdf.Name.MCID, mcid],
                pikepdf.Operator("BDC")
            )
            result.append(bdc_instr)
            break

        # Add original instruction
        original_instr = ContentStreamInstruction(instr.operands, pikepdf.Operator(instr.operator))
        result.append(original_instr)

        # Insert EMC if this is the end of the current consecutive group
        while current_segment is not None and i == current_segment[1]:
            emc_instr = ContentStreamInstruction([], pikepdf.Operator("EMC"))
            result.append(emc_instr)

            current_segment_idx += 1
            if current_segment_idx < len(wrapped_segments):
                current_segment = wrapped_segments[current_segment_idx]
            else:
                current_segment = None
                break

        # Close artifact block if needed (after adding instruction)
        if in_artifact and (i == len(instructions) - 1 or (i + 1) in marked_indices):
            emc_instr = ContentStreamInstruction([], pikepdf.Operator("EMC"))
            result.append(emc_instr)
            in_artifact = False

    # Close any remaining artifact block
    if in_artifact:
        emc_instr = ContentStreamInstruction([], pikepdf.Operator("EMC"))
        result.append(emc_instr)

    return result


# ============================================================================
# PDF Content Stream Rewriting Logic
# ============================================================================

def rewrite_content_stream_with_mcids(
    pdf: Pdf,
    page: pikepdf.Page,
    page_index: int,
    segments: List[McidSegment],
    debug: bool = False
) -> tuple[int, int]:
    """
    Rewrites a page's content stream to insert BDC/EMC markers for each segment.
    Uses Phase 6J geometry-based segmentation.

    Returns: (bdc_count, emc_count) tuple
    """
    # Filter segments for this page (convert to 0-based)
    page_segments = [s for s in segments if s.page_index == page_index + 1]

    if not page_segments:
        logger.info(f"[PHASE-6J] Page {page_index + 1}: No segments to process")
        return (0, 0)

    logger.info(f"[PHASE-6J] Page {page_index + 1}: Processing {len(page_segments)} segments")

    # Load font metrics
    font_cache = FontInfoCache()
    font_cache.load_from_page(page)
    logger.info(f"[PHASE-6J] Page {page_index + 1}: Loaded {len(font_cache.fonts)} fonts")

    # Parse content stream with geometry
    try:
        instructions = parse_instructions_with_geometry(page, font_cache)
        logger.info(f"[PHASE-6J] Page {page_index + 1}: Parsed {len(instructions)} instructions")

        # Count instructions with geometry
        text_count = sum(1 for i in instructions if i.bbox and not i.is_image)
        image_count = sum(1 for i in instructions if i.is_image)
        logger.info(f"[PHASE-6J] Page {page_index + 1}: {text_count} text, {image_count} images with geometry")

    except Exception as e:
        logger.error(f"[PHASE-6J] Failed to parse content stream for page {page_index + 1}: {e}")
        raise

    # Assign instructions to segments
    segment_instruction_indices = assign_instructions_to_segments(instructions, page_segments)

    # Log assignment results
    for mcid, indices in segment_instruction_indices.items():
        seg = next((s for s in page_segments if s.mcid == mcid), None)
        if seg:
            logger.info(f"[PHASE-6J]   MCID={mcid}, Role={seg.role}: {len(indices)} instructions assigned")

    # Wrap with BDC/EMC
    wrapped_instructions = wrap_segments_with_mcid(instructions, segment_instruction_indices)

    # Count markers
    bdc_count = sum(1 for i in wrapped_instructions if str(i.operator) == "BDC")
    emc_count = sum(1 for i in wrapped_instructions if str(i.operator) == "EMC")

    logger.info(f"[PHASE-6J] Page {page_index + 1}: Built instruction stream with {bdc_count} BDC, {emc_count} EMC")

    # Rebuild content stream
    try:
        new_stream = pikepdf.unparse_content_stream(wrapped_instructions)
        logger.info(f"[PHASE-6J] Page {page_index + 1}: Unparsed to stream of {len(new_stream)} bytes")

        # Replace page contents (direct assignment)
        page.Contents = Stream(pdf, new_stream)
        logger.info(f"[PHASE-6J] Page {page_index + 1}: Replaced page contents")

        # Verify
        verify_stream = bytes(page.Contents.read_bytes())
        verify_bdc = verify_stream.count(b'BDC')
        verify_emc = verify_stream.count(b'EMC')
        logger.info(f"[PHASE-6J] Page {page_index + 1}: Verification - {verify_bdc} BDC, {verify_emc} EMC in content")

    except Exception as e:
        logger.error(f"[PHASE-6J] Failed to rebuild content stream for page {page_index + 1}: {e}", exc_info=True)
        raise

    return (bdc_count, emc_count)


def rewrite_pdf_with_mcids(pdf_bytes: bytes, plan: McidRewritePlan) -> tuple[bytes, int, int, int]:
    """
    Main rewriting function: opens PDF, rewrites content streams, returns modified PDF.
    Returns: (output_bytes, pages_processed, total_bdc, total_emc)
    """
    logger.info(f"[PHASE-6J] Starting MCID rewrite for document: {plan.document_id or 'unknown'}")
    logger.info(f"[PHASE-6J] Plan version: {plan.version}, Segments: {len(plan.segments)}, Debug: {plan.debug}")

    # Open PDF with pikepdf
    try:
        pdf = pikepdf.open(BytesIO(pdf_bytes))
    except Exception as e:
        logger.error(f"[PHASE-6J] Failed to open PDF: {e}")
        raise ValueError(f"Failed to open PDF: {e}")

    total_bdc = 0
    total_emc = 0
    pages_processed = 0

    # Process each page
    page_count = len(pdf.pages)
    logger.info(f"[PHASE-6J] PDF has {page_count} pages")

    for page_index, page in enumerate(pdf.pages):
        try:
            bdc, emc = rewrite_content_stream_with_mcids(pdf, page, page_index, plan.segments, plan.debug)
            total_bdc += bdc
            total_emc += emc
            if bdc > 0 or emc > 0:
                pages_processed += 1
        except Exception as e:
            logger.error(f"[PHASE-6J] Failed to process page {page_index + 1}: {e}")
            continue

    # Save modified PDF to bytes
    # PHASE 6K DEBUG: Try different save options to preserve BDC/EMC markers
    try:
        output_buffer = BytesIO()
        # Disable content normalization to preserve BDC/EMC markers
        pdf.save(output_buffer, normalize_content=False, compress_streams=False)
        output_bytes = output_buffer.getvalue()

        # DEBUG: Verify markers in saved bytes
        saved_bdc = output_bytes.count(b'BDC')
        saved_emc = output_bytes.count(b'EMC')
        logger.info(f"[PHASE-6J-DEBUG] After pdf.save(): {saved_bdc} BDC, {saved_emc} EMC in output bytes")

    except Exception as e:
        logger.error(f"[PHASE-6J] Failed to save PDF: {e}")
        raise ValueError(f"Failed to save modified PDF: {e}")
    finally:
        pdf.close()

    logger.info(f"[PHASE-6J] ✅ Rewrite complete: {pages_processed} pages, {len(plan.segments)} segments, {total_bdc} BDC, {total_emc} EMC")

    return (output_bytes, pages_processed, total_bdc, total_emc)


# ============================================================================
# API Endpoints
# ============================================================================

@app.get("/health")
async def health_check():
    """Health check endpoint."""
    return {"status": "healthy", "service": "mcid-rewriter", "version": "6J-1"}


@app.post("/api/mcid-rewrite", response_model=McidRewriteResponse)
async def mcid_rewrite(request: McidRewriteRequest):
    """
    Main endpoint: receives PDF + plan, returns rewritten PDF.
    """
    try:
        logger.info(f"[PHASE-6J] Received MCID rewrite request: {len(request.plan.segments)} segments")

        # Decode PDF from Base64
        try:
            pdf_bytes = base64.b64decode(request.pdf_base64)
        except Exception as e:
            logger.error(f"[PHASE-6J] Failed to decode Base64 PDF: {e}")
            raise HTTPException(status_code=400, detail=f"Invalid Base64 PDF data: {e}")

        logger.info(f"[PHASE-6J] Input PDF size: {len(pdf_bytes) / 1024:.1f} KB")

        # Rewrite PDF
        try:
            output_bytes, pages_processed, bdc_count, emc_count = rewrite_pdf_with_mcids(pdf_bytes, request.plan)
        except ValueError as e:
            logger.error(f"[PHASE-6J] PDF processing error: {e}")
            raise HTTPException(status_code=400, detail=str(e))
        except Exception as e:
            logger.error(f"[PHASE-6J] Unexpected error during rewrite: {e}", exc_info=True)
            raise HTTPException(status_code=500, detail=f"Internal error: {e}")

        # Encode result as Base64
        output_base64 = base64.b64encode(output_bytes).decode('utf-8')

        logger.info(f"[PHASE-6J] Output PDF size: {len(output_bytes) / 1024:.1f} KB")

        # Build stats
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
        logger.error(f"[PHASE-6J] Unexpected error in endpoint: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Internal server error: {e}")


# ============================================================================
# Main Entry Point
# ============================================================================

if __name__ == "__main__":
    import uvicorn

    logger.info("=" * 80)
    logger.info("PHASE 6J: MCID Rewriter Microservice (Geometry + Images)")
    logger.info("=" * 80)

    uvicorn.run(
        app,
        host="0.0.0.0",
        port=8000,
        log_level="info"
    )
