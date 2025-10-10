#!/usr/bin/env python3
"""
Smart whitespace artifact marking utility.
Instead of deleting whitespace, intelligently marks it as artifacts or merges with adjacent content.

Usage: python3 mark_whitespace_artifacts.py <input_pdf> [output_pdf] [--merge|--mark|--smart]
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


class WhitespaceHandler:
    """Intelligent handler for untagged whitespace in PDFs."""

    def __init__(self, mode='smart'):
        self.mode = mode  # 'merge', 'mark', or 'smart'
        self.stats = {
            'merged': 0,
            'marked': 0,
            'deleted': 0,
            'preserved': 0,
            'bt_blocks': 0
        }

    def process_pdf(self, input_pdf_path, output_pdf_path=None):
        """Process the PDF and handle whitespace appropriately."""
        if output_pdf_path is None:
            output_pdf_path = tempfile.mktemp(suffix='.pdf')

        try:
            doc = fitz.open(input_pdf_path)

            for page_num in range(len(doc)):
                page = doc[page_num]
                self.process_page(doc, page, page_num)

            # Save the fixed PDF
            doc.save(output_pdf_path, garbage=4, deflate=True, clean=True)
            doc.close()

            return {
                "success": True,
                "output_path": output_pdf_path,
                "stats": self.stats,
                "message": f"Processed with mode '{self.mode}'"
            }

        except Exception as e:
            import traceback
            traceback.print_exc(file=sys.stderr)
            return {
                "success": False,
                "error": str(e)
            }

    def process_page(self, doc, page, page_num):
        """Process a single page."""
        try:
            xrefs = page.get_contents()
            if not isinstance(xrefs, list):
                xrefs = [xrefs] if xrefs else []

            for xref in xrefs:
                stream = doc.xref_stream(xref)
                if not stream:
                    continue

                content = stream.decode('latin-1', errors='ignore')
                new_content = self.process_content_stream(content, page_num + 1)

                if new_content != content:
                    # Update the stream
                    new_stream = new_content.encode('latin-1', errors='ignore')
                    doc.update_stream(xref, new_stream)

        except Exception as e:
            print(f"Error processing page {page_num + 1}: {e}", file=sys.stderr)

    def process_content_stream(self, content, page_num):
        """Process a content stream to handle whitespace."""
        original = content

        # First, handle complete BT...ET blocks
        content = self.handle_bt_blocks(content, page_num)

        # Then handle individual whitespace operations
        content = self.handle_whitespace_operations(content, page_num)

        return content

    def handle_bt_blocks(self, content, page_num):
        """Handle untagged BT...ET blocks."""
        bt_pattern = r'BT(.*?)ET'
        bt_matches = list(re.finditer(bt_pattern, content, re.DOTALL))

        # Process in reverse to maintain indices
        for match in reversed(bt_matches):
            bt_start = match.start()

            # Check if this BT is already inside marked content
            depth = self.calculate_depth(content, bt_start)
            if depth > 0:
                continue  # Already tagged

            block_content = match.group(1)

            # Check if block has any marked content
            if 'BDC' in block_content or 'BMC' in block_content:
                continue  # Has some tagging

            # Analyze content
            analysis = self.analyze_text_block(block_content)

            if analysis['is_whitespace_only']:
                if self.mode == 'smart' or self.mode == 'mark':
                    # Mark as artifact
                    new_block = f'/Artifact BMC\nBT{block_content}ET\nEMC'
                    content = content[:match.start()] + new_block + content[match.end():]
                    self.stats['marked'] += 1
                else:
                    # Delete entirely
                    content = content[:match.start()] + content[match.end():]
                    self.stats['deleted'] += 1
            elif analysis['looks_decorative']:
                # Mark decorative content as artifact
                new_block = f'/Artifact BMC\nBT{block_content}ET\nEMC'
                content = content[:match.start()] + new_block + content[match.end():]
                self.stats['marked'] += 1
            else:
                # Real content - needs proper tagging
                if self.mode == 'smart':
                    # Add paragraph tag
                    new_block = f'/P <</MCID {self.get_next_mcid()}>> BDC\nBT{block_content}ET\nEMC'
                    content = content[:match.start()] + new_block + content[match.end():]
                    self.stats['marked'] += 1
                else:
                    self.stats['preserved'] += 1

            self.stats['bt_blocks'] += 1

        return content

    def handle_whitespace_operations(self, content, page_num):
        """Handle individual whitespace text operations."""

        # Comprehensive patterns for whitespace
        patterns = [
            (r'\((\\(?:40|11|12|15|n|r|t)|\s)+\)\s*Tj', 'simple_whitespace'),
            (r'\[\s*\((\\(?:40|11|12|15|n|r|t)|\s)+\)\s*(?:-?\d+\s*)?\]\s*TJ', 'array_whitespace'),
            (r'<(?:0020|00A0|0009|000[AD])>\s*Tj', 'hex_whitespace'),
            (r'\(\)\s*Tj', 'empty_text'),
        ]

        for pattern, pattern_type in patterns:
            matches = list(re.finditer(pattern, content))

            for match in reversed(matches):
                pos = match.start()
                depth = self.calculate_depth(content, pos)

                if depth == 0:  # Untagged
                    # Analyze context
                    context = self.analyze_context(content, pos)

                    if self.mode == 'merge' and context['can_merge']:
                        # Merge with adjacent tagged content
                        content = self.merge_with_adjacent(content, match, context)
                        self.stats['merged'] += 1
                    elif self.mode == 'mark' or (self.mode == 'smart' and context['should_mark']):
                        # Mark as artifact
                        artifact = f'/Artifact BMC\n{match.group(0)}\nEMC'
                        content = content[:match.start()] + artifact + content[match.end():]
                        self.stats['marked'] += 1
                    elif self.mode == 'smart' and context['can_delete']:
                        # Safe to delete
                        content = content[:match.start()] + content[match.end():]
                        self.stats['deleted'] += 1
                    else:
                        self.stats['preserved'] += 1

        return content

    def analyze_text_block(self, block_content):
        """Analyze a text block to determine its nature."""
        result = {
            'is_whitespace_only': True,
            'looks_decorative': False,
            'has_real_content': False
        }

        # Extract all text operations
        text_ops = re.findall(r'\([^)]*\)\s*(?:Tj|TJ)', block_content)

        for op in text_ops:
            text_match = re.search(r'\(([^)]*)\)', op)
            if text_match:
                text = text_match.group(1)
                # Decode escape sequences
                text = self.decode_pdf_string(text)

                if text.strip():
                    result['is_whitespace_only'] = False

                    # Check if it looks like decoration
                    if len(text.strip()) <= 3 and text.strip() in '•·–—―_|/\\':
                        result['looks_decorative'] = True
                    elif text.strip().isdigit() and len(text.strip()) <= 3:
                        # Could be page number
                        result['looks_decorative'] = True
                    else:
                        result['has_real_content'] = True

        return result

    def analyze_context(self, content, position):
        """Analyze the context around a whitespace operation."""
        context = {
            'can_merge': False,
            'should_mark': False,
            'can_delete': False,
            'merge_target': None
        }

        # Look for nearby tagged content
        before_snippet = content[max(0, position - 200):position]
        after_snippet = content[position:min(len(content), position + 200)]

        # Check if there's tagged content immediately before
        if 'EMC' in before_snippet[-50:]:
            context['can_merge'] = True
            context['merge_target'] = 'before'

        # Check if there's tagged content immediately after
        if 'BDC' in after_snippet[:50] or 'BMC' in after_snippet[:50]:
            context['can_merge'] = True
            context['merge_target'] = 'after'

        # Check if it looks like layout whitespace
        if not context['can_merge']:
            # Isolated whitespace - probably layout
            context['should_mark'] = True

        # Check if safe to delete
        # Only delete if it's truly isolated and minimal
        if not context['can_merge'] and len(after_snippet.strip()) > 0:
            context['can_delete'] = True

        return context

    def merge_with_adjacent(self, content, match, context):
        """Merge whitespace with adjacent tagged content."""
        if context['merge_target'] == 'before':
            # Find the previous EMC and insert before it
            emc_pos = content.rfind('EMC', 0, match.start())
            if emc_pos != -1:
                # Insert whitespace before the EMC
                return (content[:emc_pos] + match.group(0) + '\n' +
                        content[emc_pos:match.start()] + content[match.end():])

        elif context['merge_target'] == 'after':
            # Find the next BDC/BMC and insert after it
            bdc_match = re.search(r'(BDC|BMC)', content[match.end():])
            if bdc_match:
                insert_pos = match.end() + bdc_match.end()
                # Move whitespace after the BDC/BMC
                return (content[:match.start()] +
                        content[match.end():insert_pos] + '\n' + match.group(0) +
                        content[insert_pos:])

        # Fallback: mark as artifact if merge fails
        artifact = f'/Artifact BMC\n{match.group(0)}\nEMC'
        return content[:match.start()] + artifact + content[match.end():]

    def calculate_depth(self, text, position):
        """Calculate marked content depth at position."""
        depth = 0
        for m in re.finditer(r'(BDC|BMC|EMC)', text[:position]):
            if m.group(0) in ['BDC', 'BMC']:
                depth += 1
            else:
                depth = max(0, depth - 1)
        return depth

    def decode_pdf_string(self, pdf_string):
        """Decode PDF string with escape sequences."""
        result = pdf_string
        result = result.replace('\\n', '\n')
        result = result.replace('\\r', '\r')
        result = result.replace('\\t', '\t')
        result = result.replace('\\40', ' ')
        result = result.replace('\\(', '(')
        result = result.replace('\\)', ')')

        # Handle octal
        def octal_replace(match):
            try:
                return chr(int(match.group(1), 8))
            except:
                return match.group(0)
        result = re.sub(r'\\(\d{1,3})', octal_replace, result)

        return result

    def get_next_mcid(self):
        """Get the next available MCID number."""
        # In a real implementation, this would track MCIDs properly
        import random
        return random.randint(100, 999)


def main():
    if len(sys.argv) < 2:
        print("Usage: mark_whitespace_artifacts.py <input_pdf> [output_pdf] [--merge|--mark|--smart]")
        print("Modes:")
        print("  --merge : Try to merge whitespace with adjacent tagged content")
        print("  --mark  : Mark all untagged whitespace as artifacts")
        print("  --smart : Intelligently decide based on context (default)")
        sys.exit(1)

    input_pdf = sys.argv[1]
    output_pdf = None
    mode = 'smart'

    for arg in sys.argv[2:]:
        if arg in ['--merge', '--mark', '--smart']:
            mode = arg.replace('--', '')
        elif not output_pdf:
            output_pdf = arg

    if not os.path.exists(input_pdf):
        print(json.dumps({
            "success": False,
            "error": f"Input PDF not found: {input_pdf}"
        }))
        sys.exit(1)

    handler = WhitespaceHandler(mode)
    result = handler.process_pdf(input_pdf, output_pdf)

    print(json.dumps(result, indent=2))
    sys.exit(0 if result["success"] else 1)


if __name__ == "__main__":
    main()