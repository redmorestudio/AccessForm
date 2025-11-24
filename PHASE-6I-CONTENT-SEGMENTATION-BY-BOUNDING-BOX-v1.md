# Phase 6I – Content Stream Segmentation by Bounding Box (v1)

**Goal**  
Extend the external MCID rewriter (Phase 6H) so that BDC/EMC + MCID markers wrap the **correct portions of content** instead of the entire page.

Right now, the microservice does something like:

BDC (MCID 0)
BDC (MCID 1)
<ALL PAGE CONTENT>
EMC
EMC

This corrupts layout and is semantically wrong. Phase 6I defines how to:

1. Parse content streams into a sequence of instructions.
2. Estimate the geometric position of each text-drawing instruction.
3. Assign each instruction to zero/one/many `McidSegment`s based on bounding boxes.
4. Insert BDC/EMC pairs **around the instruction ranges per segment**, preserving order.

Give this file to Claude and say:

> “Implement everything in `PHASE-6I-CONTENT-SEGMENTATION-BY-BOUNDING-BOX-v1.md`.”

---

## 0. Assumptions & Existing Pieces

From Phases 6H and earlier, we already have:

- A Python microservice using `pikepdf` that:
  - Accepts: `pdfBase64` + `McidRewritePlan` (`segments` with MCID + bounding boxes).
  - Loads the PDF using `pikepdf`.
  - Decodes content streams into a list of **ContentStreamInstruction**-like objects.
  - Can insert BDC/EMC operators and save the PDF back out.

- A `McidSegment` struct/record (JSON) with:
  - `PageIndex`
  - `Mcid`
  - `Role`
  - `X`, `Y`, `Width`, `Height`
  - `SequenceIndex`

- A working proof that:
  - When we insert BDC/EMC using structured instructions (e.g. `ContentStreamInstruction([...], Operator("BDC"))`), markers **persist** in saved PDFs.

Phase 6I assumes all that is in place and focuses only on **correct segmentation + wrapping**.

---

## 1. Core Concept

We want to achieve this pattern per segment:

<other content>
BDC /Span <</MCID n>> BDC
<content assigned to segment n>
EMC
<more content>

Instead of:

BDC /Span <</MCID 0>> BDC
BDC /Span <</MCID 1>> BDC
<all content>
EMC
EMC

That requires knowing which instructions in the stream correspond to which segment’s bounding box.

---

## 2. ContentStreamInstruction Model

### 2.1 Instruction abstraction

Ensure there is a Python class like:

class ContentStreamInstruction:
    def __init__(self, operands, operator):
        self.operands = operands  # list of pikepdf.Name, numbers, strings, etc.
        self.operator = operator  # pikepdf.Operator or string like "Tj"

        # New: approximate geometry
        self.x = None      # float | None
        self.y = None      # float | None
        self.bbox = None   # (x, y, w, h) | None

Where:

- `operands`: decoded from stream tokens.
- `operator`: e.g., "Tj", "TJ", "cm", "Tm", "Td", "BT", "ET", "Do", "q", "Q".

Phase 6I requires extending this class with **geometry fields** that reflect where the instruction draws.

---

## 3. Geometry Estimation

### 3.1 Text positioning basics (approximation is OK)

For v1, we only need **good-enough** geometry to associate text with bounding boxes. We do not need full PDF text extraction accuracy.

We approximate using:

- Text object state between BT and ET.
- Text matrix (Tm).
- Text movement (Td, TD).
- Font size (Tf) if available.

Minimal model:

class TextState:
    def __init__(self):
        self.in_text = False  # between BT/ET
        self.x = 0.0
        self.y = 0.0
        self.font_size = 12.0  # default; update on Tf

### 3.2 Geometry assignment rules

During stream parsing:

1. Initialize `TextState`.
2. Walk the instructions in order.
3. For each instruction:

   - If operator is `BT`:
     - `text_state.in_text = True`

   - If operator is `ET`:
     - `text_state.in_text = False`

   - If operator is `Tm`:
     - Operands: `a b c d e f`
     - Set `text_state.x = e`, `text_state.y = f`.

   - If operator is `Td` or `TD`:
     - Operands: `tx ty`
     - Update `text_state.x += tx`, `text_state.y += ty`.

   - If operator is `Tf`:
     - Operands: `/FontName fontSize`
     - Update `text_state.font_size = fontSize`.

   - If operator is `Tj` or `TJ` and `text_state.in_text`:
     - Use `text_state.x`, `text_state.y` as the anchor.
     - Approximate bounding box:

       inst.x = text_state.x
       inst.y = text_state.y
       estimated_width = max(50.0, len(text_string) * text_state.font_size * 0.5)
       inst.bbox = (inst.x, inst.y, estimated_width, text_state.font_size)

For now, we mostly care about Y to distinguish lines; X/width can be rough.

### 3.3 Images (optional in v1)

If/when you MCID-wrap images:

- Track preceding `cm` before `Do` to infer approximate position.
- For Phase 6I v1, it’s acceptable to focus on text (`Tj`, `TJ`) only.

---

## 4. Assigning Instructions to Segments

### 4.1 Group instructions per page

Per page:

- `page_instructions: List[ContentStreamInstruction]`
- `segments_for_page: List[McidSegment]` (filtered by `PageIndex`)

### 4.2 Bounding box overlap function

Utility:

def bbox_overlaps(seg_bbox, inst_bbox, y_tolerance=2.0):
    sx, sy, sw, sh = seg_bbox
    ix, iy, iw, ih = inst_bbox

    seg_x1, seg_x2 = sx, sx + sw
    seg_y1, seg_y2 = sy, sy + sh

    inst_x1, inst_x2 = ix, ix + iw
    inst_y1, inst_y2 = iy, iy + ih

    horiz = (inst_x2 >= seg_x1) and (inst_x1 <= seg_x2)
    vert = (inst_y2 + y_tolerance >= seg_y1) and (inst_y1 - y_tolerance <= seg_y2)

    return horiz and vert

### 4.3 Mapping instructions to segments

For each instruction with `inst.bbox`:

- For each segment `seg` on that page:
  - If `bbox_overlaps(seg_bbox, inst.bbox)`:
    - Mark that this instruction index belongs to `seg.Mcid`.

Use something like:

segment_instruction_indices = {
    mcid_value: sorted(list_of_instruction_indices)
}

If an instruction overlaps multiple segments, for v1:

- Assign it to the first overlapping segment in ascending `SequenceIndex` order on that page.

---

## 5. Inserting BDC/EMC Around Assigned Content

### 5.1 Building segment ranges

For each MCID:

- `indices = segment_instruction_indices[mcid]`  
- If empty → skip.
- `start = indices[0]`, `end = indices[-1]`.

Build a list of `(start, end, mcid)` for all segments on that page.

### 5.2 Wrapping while preserving order

Recommended approach: build a new instruction list.

Pseudocode:

def wrap_segments_with_mcid(instructions, segment_instruction_indices):
    wrapped_segments = []
    for mcid, idxs in segment_instruction_indices.items():
        if not idxs:
            continue
        start, end = idxs[0], idxs[-1]
        wrapped_segments.append((start, end, mcid))

    wrapped_segments.sort(key=lambda t: (t[0], t[1]))

    result = []
    current_segment_idx = 0
    current_segment = wrapped_segments[current_segment_idx] if wrapped_segments else None

    for i, instr in enumerate(instructions):
        # If this instruction is the start of a segment, insert BDC first
        while current_segment is not None and i == current_segment[0]:
            _, _, mcid = current_segment
            bdc_instr = ContentStreamInstruction(
                operands=[pikepdf.Name("/Span"), pikepdf.Name("/MCID"), mcid],
                operator=pikepdf.Operator("BDC")
            )
            result.append(bdc_instr)
            break

        result.append(instr)

        # If this instruction is the end of the current segment, insert EMC
        while current_segment is not None and i == current_segment[1]:
            emc_instr = ContentStreamInstruction([], pikepdf.Operator("EMC"))
            result.append(emc_instr)

            current_segment_idx += 1
            if current_segment_idx < len(wrapped_segments):
                current_segment = wrapped_segments[current_segment_idx]
            else:
                current_segment = None
                break

    return result

Claude should adapt the exact types (`pikepdf.Name`, `pikepdf.Operator`) to whatever is already used.

### 5.3 Edge cases

- **Segments with no text instructions**: skip them; do not emit empty BDC/EMC pairs.
- **Overlapping segments**: v1 can tolerate simple nesting; if needed, later phases can merge/normalize.

---

## 6. Integrating Back Into Phase 6H

The main flow becomes (per page):

1. Parse original content into `instructions`.
2. Compute geometry (populate `inst.bbox`).
3. Build `segment_instruction_indices` using bounding box overlaps.
4. Call `wrap_segments_with_mcid(...)` to get `wrapped_instructions`.
5. Serialize `wrapped_instructions` back to bytes and replace `/Contents`.

All existing Phase 6H logic for:

- Loading/saving PDFs.
- Handling multiple pages.
- Returning base64.

…stays the same.

---

## 7. Testing

### 7.1 Synthetic 2-line PDF

Create a tiny 1-page PDF:

- Line 1 at Y ≈ 700.
- Line 2 at Y ≈ 650.

Plan:

- Segment 0: bbox covering line 1.
- Segment 1: bbox covering line 2.

Expected:

- Instructions for line 1 are wrapped by BDC/EMC MCID=0.
- Instructions for line 2 are wrapped by BDC/EMC MCID=1.
- Visual output looks unchanged.
- `strings output.pdf | grep -c "BDC"` > 0, `grep -c "EMC"` > 0.

### 7.2 Real-world (Alexandria) PDF

Re-run:

- Use the same `McidRewritePlan` as Phase 6H tests.
- Verify:
  - Layout is intact (no “everything moved into a box” effect).
  - Tags highlight correctly in Acrobat/PDFix.
  - Screen reader navigation respects headings/paragraphs.

---

## 8. Summary for Claude

> - Extend the current external MCID rewriter to:
>   - Estimate approximate geometry (x, y, bbox) for text instructions using BT/ET + Tm/Td/TD/Tf.
>   - Map instructions to `McidSegment`s based on bbox overlaps.
>   - Insert BDC/EMC pairs around the instruction ranges for each MCID, preserving stream order.
> - Do not wrap the entire content in one big BDC/EMC block.
> - Keep all existing Phase 6H architecture; only change how the content is segmented and wrapped.
> - Test with a synthetic 2-line PDF and then the Alexandria PDF.

---

**Filename:**  
`PHASE-6I-CONTENT-SEGMENTATION-BY-BOUNDING-BOX-v1.md`
