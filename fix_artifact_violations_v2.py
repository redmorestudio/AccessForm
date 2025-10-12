#!/usr/bin/env python3
"""
Enhanced version of fix_artifact_violations.py with improved depth tracking and patterns.
Fixes PDF/UA violations by properly handling untagged text content.

Usage: python3 fix_artifact_violations_v2.py <input_pdf> [output_pdf] [--verbose]
"""

import sys
import json
import tempfile
import os
import re

try:
    import fitz  # PyMuPDF
except ImportError:
    print(json.dumps({
        "success": False,
        "error": "PyMuPDF not installed. Run: pip install PyMuPDF"
    }))
    sys.exit(1)


def calculate_depth_at_position(text, position):
    """Calculate the marked content depth at a specific position using proper stack tracking."""
    depth = 0

    # Find all markers up to this position
    marker_pattern = r'(BDC|BMC|EMC)'
    for m in re.finditer(marker_pattern, text[:position]):
        marker_type = m.group(0)
        if marker_type in ['BDC', 'BMC']:
            depth += 1
        elif marker_type == 'EMC':
            depth = max(0, depth - 1)  # Never go negative

    return depth


def decode_pdf_string(pdf_string):
    """Decode PDF string with escape sequences."""
    result = pdf_string
    result = result.replace('\\n', '\n')
    result = result.replace('\\r', '\r')
    result = result.replace('\\t', '\t')
    result = result.replace('\\(', '(')
    result = result.replace('\\)', ')')
    result = result.replace('\\\\', '\\')

    # Handle octal sequences
    def octal_replace(match):
        octal = match.group(1)
        try:
            char_code = int(octal, 8)
            return chr(char_code)
        except:
            return match.group(0)

    result = re.sub(r'\\(\d{1,3})', octal_replace, result)
    return result


def is_whitespace_only(text):
    """Check if text contains only whitespace."""
    decoded = decode_pdf_string(text)
    return not decoded.strip()


def fix_artifact_violations(input_pdf_path, output_pdf_path=None, verbose=False):
    """
    Remove artifact markers from tagged content and handle untagged content.

    Five main passes:
    1. Unwrap tagged content from /Artifact BMC...EMC blocks
    2. Remove or mark untagged whitespace as artifacts
    3. Mark untagged BT...ET text blocks as artifacts
    4. Mark untagged path/graphics operations as artifacts (comprehensive)
    5. Final sweep: unwrap any remaining tagged content from artifacts
    """
    if output_pdf_path is None:
        output_pdf_path = tempfile.mktemp(suffix='.pdf')

    try:
        doc = fitz.open(input_pdf_path)

        total_fixed = 0
        violations_found = 0
        whitespace_removed = 0
        bt_blocks_marked = 0
        path_blocks_marked_total = 0
        sweep_fixes_total = 0
        poor_contrast_removed_total = 0

        for page_num in range(len(doc)):
            page = doc[page_num]

            try:
                # Get content stream(s)
                xrefs = page.get_contents()
                if isinstance(xrefs, list):
                    xref_list = xrefs
                else:
                    xref_list = [xrefs]

                # Process each content stream
                for xref in xref_list:
                    stream = doc.xref_stream(xref)
                    if not stream:
                        continue

                    content_str = stream.decode('latin-1', errors='ignore')
                    original_content = content_str
                    modified = False

                    # PASS 1: Find and fix all /Artifact BMC blocks with tagged content
                    while True:
                        artifact_pos = content_str.find('/Artifact BMC')
                        if artifact_pos == -1:
                            break

                        # Find matching EMC
                        bmc_end = artifact_pos + len('/Artifact BMC')
                        nesting_level = 1
                        search_pos = bmc_end
                        matching_emc = -1

                        while search_pos < len(content_str):
                            next_bmc = content_str.find('BMC', search_pos)
                            next_bdc = content_str.find('BDC', search_pos)
                            next_emc = content_str.find('EMC', search_pos)

                            earliest_pos = len(content_str)
                            earliest = None

                            if next_bmc != -1 and next_bmc < earliest_pos:
                                earliest = 'BMC'
                                earliest_pos = next_bmc
                            if next_bdc != -1 and next_bdc < earliest_pos:
                                earliest = 'BDC'
                                earliest_pos = next_bdc
                            if next_emc != -1 and next_emc < earliest_pos:
                                earliest = 'EMC'
                                earliest_pos = next_emc

                            if earliest is None:
                                break

                            if earliest in ['BMC', 'BDC']:
                                nesting_level += 1
                                search_pos = earliest_pos + 3
                            elif earliest == 'EMC':
                                nesting_level -= 1
                                if nesting_level == 0:
                                    matching_emc = earliest_pos
                                    break
                                search_pos = earliest_pos + 3

                        if matching_emc == -1:
                            content_str = content_str[:artifact_pos] + '###PROCESSED###' + content_str[artifact_pos + 13:]
                            continue

                        between = content_str[bmc_end:matching_emc]

                        # Check if this artifact contains tagged content
                        if '/MCID' in between:
                            violations_found += 1

                            # Check if it's whitespace-only
                            text_strings = re.findall(r'\(([^)]*)\)', between)
                            is_whitespace = all(is_whitespace_only(text) for text in text_strings) if text_strings else False

                            # Always preserve tagged content, just remove artifact wrapper
                            fixed_content = content_str[:artifact_pos] + between + content_str[matching_emc + 3:]
                            content_str = fixed_content
                            modified = True
                            total_fixed += 1
                        else:
                            content_str = content_str[:artifact_pos] + '###PROCESSED###' + content_str[artifact_pos + 13:]

                    # Restore markers
                    content_str = content_str.replace('###PROCESSED###', '/Artifact BMC')

                    # PASS 2: Remove untagged whitespace with improved patterns
                    patterns = [
                        # Simple whitespace with Tj
                        r'\((\\(?:40|11|12|15|n|r|t)|\s)*\)\s*Tj',
                        # Whitespace in TJ arrays
                        r'\[\s*\((\\(?:40|11|12|15|n|r|t)|\s)*\)\s*(?:-?\d+\s*)?\]\s*TJ',
                        # Multiple whitespace in TJ arrays
                        r'\[(?:\s*\((\\(?:40|11|12|15|n|r|t)|\s)*\)\s*-?\d+\s*)*\]\s*TJ',
                        # Hex-encoded spaces (space, nbsp, tab, lf, cr)
                        r'<(?:0020|00A0|0009|000A|000D|2000|2001|2002|2003|2004|2005|2006|2007|2008|2009|200A|200B|202F|205F|3000)>\s*Tj',
                        # Whitespace with ' or " operators
                        r'\((\\(?:40|11|12|15|n|r|t)|\s)*\)\s*[\'\"]',
                        # Empty text
                        r'\(\)\s*(?:Tj|TJ|[\'\"])',
                        # Single space variations
                        r'\(\s\)\s*Tj',
                        r'\(\\40\)\s*Tj',
                    ]

                    # Track removed items for logging
                    removed_items = []

                    for pattern in patterns:
                        matches = list(re.finditer(pattern, content_str))

                        for match in reversed(matches):
                            match_pos = match.start()
                            depth = calculate_depth_at_position(content_str, match_pos)

                            if depth == 0:  # Untagged
                                removed_text = match.group(0)[:50]
                                removed_items.append(removed_text)
                                content_str = content_str[:match.start()] + content_str[match.end():]
                                modified = True
                                whitespace_removed += 1

                    # PASS 3: Handle untagged BT...ET blocks
                    bt_pattern = r'BT(.*?)ET'
                    bt_matches = list(re.finditer(bt_pattern, content_str, re.DOTALL))

                    for bt_match in reversed(bt_matches):
                        bt_start = bt_match.start()

                        # Check if BT is inside marked content
                        bt_depth = calculate_depth_at_position(content_str, bt_start)
                        if bt_depth > 0:
                            continue  # This BT is already inside marked content

                        block_content = bt_match.group(1)

                        # Check if block contains marked content
                        has_marked = 'BDC' in block_content or 'BMC' in block_content

                        if not has_marked:
                            # Entire block is untagged
                            text_ops = re.findall(r'\([^)]*\)\s*(?:Tj|TJ|[\'\"])', block_content)

                            # Check if it's whitespace-only
                            is_whitespace = True
                            for op in text_ops:
                                text_match = re.search(r'\(([^)]*)\)', op)
                                if text_match and not is_whitespace_only(text_match.group(1)):
                                    is_whitespace = False
                                    break

                            if is_whitespace and text_ops:
                                # Option 1: Remove completely if whitespace-only
                                content_str = content_str[:bt_match.start()] + content_str[bt_match.end():]
                                modified = True
                                bt_blocks_marked += 1
                            elif not is_whitespace and text_ops:
                                # Option 2: Mark as artifact if it has content
                                # This preserves reading order but marks as non-semantic
                                new_block = f'/Artifact BMC\n{bt_match.group(0)}\nEMC'
                                content_str = content_str[:bt_match.start()] + new_block + content_str[bt_match.end():]
                                modified = True
                                bt_blocks_marked += 1

                    # PASS 4: Wrap untagged path/graphics operations in artifacts
                    # Path construction: m (move), l (line), c (curve), re (rectangle), h (closepath), v, y
                    # Paint operations: S (stroke), s (close+stroke), f/F (fill), f* (even-odd fill), B/B*/b/b* (fill+stroke)
                    # Clipping: W/W* (clip), n (no-op path end)
                    # Graphics state: q (save), Q (restore), cm (matrix), w (linewidth), J/j (linecap/join), M (miterlimit)
                    # Color: RG/rg (RGB), K/k (CMYK), SC/sc/SCN/scn (color), G/g (gray)

                    # Pattern: Match PDF operators (word boundaries or after whitespace/numbers)
                    # Single letters must be standalone, multi-letter can use word boundaries
                    # Match path/paint operators, including f*, B*, b* with optional * modifier
                    path_paint_ops = r'(?:^|\s)(?:re|cm|RG|rg|SC|sc|SCN|scn|[fBb]\*?|[mlchvyqQwJjMGgKkSsFWn])(?:\s|$)'

                    path_blocks_marked = 0

                    # Iteratively find and wrap untagged graphics blocks
                    max_iterations = 50  # Safety limit
                    for iteration in range(max_iterations):
                        found_untagged = False

                        # Find all path/paint operations
                        for match in re.finditer(path_paint_ops, content_str):
                            match_pos = match.start()
                            depth = calculate_depth_at_position(content_str, match_pos)

                            if depth == 0:  # Untagged - gather consecutive graphics lines
                                # First, scan BACKWARD to find any preceding untagged graphics
                                block_start = match_pos
                                scan_pos_back = match_pos

                                while True:
                                    # Find start of current line
                                    prev_newline = content_str.rfind('\n', 0, scan_pos_back - 1)
                                    if prev_newline == -1:
                                        block_start = 0
                                        break

                                    prev_line_start = prev_newline + 1

                                    # Look for graphics operator on previous line
                                    prev_line = content_str[prev_line_start:scan_pos_back]
                                    prev_match = re.search(path_paint_ops, prev_line)
                                    if not prev_match:
                                        # No graphics on previous line, stop
                                        block_start = prev_line_start if scan_pos_back != match_pos else content_str.rfind('\n', 0, match_pos) + 1
                                        if block_start < 0:
                                            block_start = 0
                                        break

                                    # Check depth of previous graphics op
                                    prev_op_pos = prev_line_start + prev_match.start()
                                    prev_depth = calculate_depth_at_position(content_str, prev_op_pos)

                                    if prev_depth > 0:
                                        # Hit tagged content, stop
                                        block_start = prev_line_start if scan_pos_back != match_pos else content_str.rfind('\n', 0, match_pos) + 1
                                        if block_start < 0:
                                            block_start = 0
                                        break

                                    # Continue gathering backward
                                    block_start = prev_line_start
                                    scan_pos_back = prev_line_start

                                # Now scan forward from the match
                                scan_pos = match_pos
                                block_end = scan_pos

                                while True:
                                    # Find end of current line
                                    next_newline = content_str.find('\n', scan_pos)
                                    if next_newline == -1:
                                        block_end = len(content_str)
                                        break

                                    block_end = next_newline

                                    # Check if next line has graphics ops
                                    next_line_start = next_newline + 1
                                    if next_line_start >= len(content_str):
                                        break

                                    # Look for graphics operator on next line
                                    next_match = re.search(path_paint_ops, content_str[next_line_start:next_line_start+200])
                                    if not next_match:
                                        break  # No more graphics

                                    # Check depth of next graphics op
                                    next_op_pos = next_line_start + next_match.start()
                                    next_depth = calculate_depth_at_position(content_str, next_op_pos)

                                    if next_depth > 0:
                                        break  # Hit tagged content, stop here

                                    # Continue gathering
                                    scan_pos = next_op_pos

                                # Get the block
                                block_content = content_str[block_start:block_end]

                                # Skip if already wrapped
                                if '/Artifact BMC' not in block_content and 'BDC' not in block_content:
                                    # Wrap the consecutive graphics block
                                    content_str = (content_str[:block_start] +
                                                 '/Artifact BMC\n' +
                                                 block_content +
                                                 '\nEMC\n' +
                                                 content_str[block_end:])
                                    modified = True
                                    path_blocks_marked += 1
                                    found_untagged = True
                                    break  # Re-scan after modification

                        if not found_untagged:
                            break  # No more untagged graphics blocks found

                    # PASS 5: Final sweep - remove any tagged content from artifacts
                    # This catches any /MCID content that slipped through
                    sweep_fixes = 0
                    while True:
                        # Find /Artifact BMC blocks
                        artifact_match = re.search(r'/Artifact\s+BMC', content_str)
                        if not artifact_match:
                            break

                        artifact_pos = artifact_match.start()
                        bmc_end = artifact_match.end()

                        # Find matching EMC
                        nesting = 1
                        search_pos = bmc_end
                        matching_emc = -1

                        while search_pos < len(content_str):
                            next_marker = re.search(r'(BMC|BDC|EMC)', content_str[search_pos:])
                            if not next_marker:
                                break

                            marker_pos = search_pos + next_marker.start()
                            marker = next_marker.group(0)

                            if marker in ['BMC', 'BDC']:
                                nesting += 1
                            elif marker == 'EMC':
                                nesting -= 1
                                if nesting == 0:
                                    matching_emc = marker_pos
                                    break

                            search_pos = marker_pos + len(marker)

                        if matching_emc == -1:
                            # No matching EMC, skip this artifact
                            content_str = content_str[:artifact_pos] + '###SKIP_ARTIFACT###' + content_str[bmc_end:]
                            continue

                        # Check content between BMC and EMC
                        between = content_str[bmc_end:matching_emc]

                        # If it contains /MCID (tagged content), unwrap it
                        if '/MCID' in between:
                            content_str = content_str[:artifact_pos] + between + content_str[matching_emc + 3:]
                            modified = True
                            sweep_fixes += 1
                        else:
                            # Mark as processed
                            content_str = content_str[:artifact_pos] + '###SKIP_ARTIFACT###' + content_str[bmc_end:]

                    # Restore skipped artifacts
                    content_str = content_str.replace('###SKIP_ARTIFACT###', '/Artifact BMC')

                    if sweep_fixes > 0:
                        print(f"Pass 5: Unwrapped {sweep_fixes} tagged objects from artifacts", file=sys.stderr)

                    # PASS 6: Remove text with poor contrast (text color same as background)
                    # This catches text that has the same color as a recent fill operation
                    poor_contrast_removed = 0

                    # Find all color operations followed by text operations
                    # Look for patterns like: "0.851 0.882 0.949 rg" followed by "BT ... (text) ... Tm [(...)] TJ"
                    # where the text has similar color values in a 'rg' or 'g' command near the text

                    # Strategy: Find BT...ET blocks, check for color commands inside, and compare
                    # with recent fill colors (from 'rg' or 'g' commands in artifact blocks)

                    bt_pattern = r'BT\s+(.*?)\s+ET'
                    for bt_match in re.finditer(bt_pattern, content_str, re.DOTALL):
                        bt_content = bt_match.group(1)
                        bt_start = bt_match.start()
                        bt_end = bt_match.end()

                        # Check if this BT block contains a color command
                        # Look for 'rg' (RGB), 'g' (gray), or 'k' (CMYK) commands
                        text_color_match = re.search(r'([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+rg', bt_content)
                        text_gray_match = re.search(r'([\d.]+)\s+g\s', bt_content)

                        if text_color_match or text_gray_match:
                            # Look backward from BT to find recent fill color in artifact block
                            lookback = content_str[max(0, bt_start - 500):bt_start]

                            # Find most recent 'rg' (fill color) command, preferably in an artifact block
                            fill_color_matches = list(re.finditer(r'([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+rg', lookback))

                            if fill_color_matches:
                                # Get the most recent fill color
                                last_fill = fill_color_matches[-1]
                                fill_r = float(last_fill.group(1))
                                fill_g = float(last_fill.group(2))
                                fill_b = float(last_fill.group(3))

                                # Compare with text color
                                if text_color_match:
                                    text_r = float(text_color_match.group(1))
                                    text_g = float(text_color_match.group(2))
                                    text_b = float(text_color_match.group(3))

                                    # Check if colors are very similar (poor contrast)
                                    # Allow small tolerance for rounding
                                    color_diff = abs(fill_r - text_r) + abs(fill_g - text_g) + abs(fill_b - text_b)

                                    if color_diff < 0.15:  # Very similar colors
                                        # Check if text is just whitespace
                                        text_content = re.search(r'\[(.*?)\]\s*TJ', bt_content)
                                        if text_content:
                                            text = text_content.group(1)
                                            # If it's just space or empty, remove the whole BT block
                                            if not text.strip() or text.strip() in ['()', '( )', '(  )']:
                                                content_str = content_str[:bt_start] + content_str[bt_end:]
                                                modified = True
                                                poor_contrast_removed += 1
                                                break  # Re-scan after modification

                    if poor_contrast_removed > 0:
                        print(f"Pass 6: Removed {poor_contrast_removed} poor contrast text blocks", file=sys.stderr)

                    # Update stream if modified
                    if modified:
                        if verbose:
                            print(f"Page {page_num + 1}: Fixed {violations_found} artifacts, "
                                  f"removed {whitespace_removed} whitespace, "
                                  f"marked {bt_blocks_marked} BT blocks, "
                                  f"marked {path_blocks_marked} path blocks, "
                                  f"unwrapped {sweep_fixes} tagged from artifacts, "
                                  f"removed {poor_contrast_removed} poor contrast text", file=sys.stderr)
                        new_stream = content_str.encode('latin-1', errors='ignore')
                        doc.update_stream(xref, new_stream)
                        total_fixed += violations_found + whitespace_removed + bt_blocks_marked + path_blocks_marked + sweep_fixes + poor_contrast_removed
                        path_blocks_marked_total += path_blocks_marked
                        sweep_fixes_total += sweep_fixes
                        poor_contrast_removed_total += poor_contrast_removed

            except Exception as page_error:
                print(f"Warning: Error processing page {page_num + 1}: {page_error}", file=sys.stderr)
                if verbose:
                    import traceback
                    traceback.print_exc(file=sys.stderr)

        # Save the fixed PDF
        doc.save(output_pdf_path, garbage=4, deflate=True, clean=True)
        doc.close()

        return {
            "success": True,
            "output_path": output_pdf_path,
            "violations_found": violations_found,
            "violations_fixed": total_fixed,
            "whitespace_removed": whitespace_removed,
            "bt_blocks_handled": bt_blocks_marked,
            "path_blocks_marked": path_blocks_marked_total,
            "tagged_unwrapped_from_artifacts": sweep_fixes_total,
            "poor_contrast_removed": poor_contrast_removed_total,
            "message": f"Fixed {total_fixed} total violations"
        }

    except Exception as e:
        import traceback
        if verbose:
            traceback.print_exc(file=sys.stderr)
        return {
            "success": False,
            "error": str(e)
        }


def main():
    if len(sys.argv) < 2:
        print(json.dumps({
            "success": False,
            "error": "Usage: fix_artifact_violations_v2.py <input_pdf> [output_pdf] [--verbose]"
        }))
        sys.exit(1)

    input_pdf = sys.argv[1]
    output_pdf = None
    verbose = False

    for arg in sys.argv[2:]:
        if arg == '--verbose':
            verbose = True
        elif not output_pdf:
            output_pdf = arg

    if not os.path.exists(input_pdf):
        print(json.dumps({
            "success": False,
            "error": f"Input PDF not found: {input_pdf}"
        }))
        sys.exit(1)

    result = fix_artifact_violations(input_pdf, output_pdf, verbose)
    print(json.dumps(result, indent=2))
    sys.exit(0 if result["success"] else 1)


if __name__ == "__main__":
    main()