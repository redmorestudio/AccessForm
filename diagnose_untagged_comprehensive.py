#!/usr/bin/env python3
"""
Comprehensive diagnostic tool for untagged text objects in PDFs.
Detects ALL variations of untagged text to match PAC's findings.
"""

import sys
import fitz
import re
import json
from collections import defaultdict

class UntaggedTextDiagnostic:
    def __init__(self, pdf_path, verbose=False):
        self.pdf_path = pdf_path
        self.verbose = verbose
        self.doc = fitz.open(pdf_path)
        self.results = {
            'total_untagged': 0,
            'by_page': defaultdict(list),
            'by_type': defaultdict(int),
            'details': []
        }

    def analyze(self):
        """Perform comprehensive analysis of untagged text."""
        print(f"Analyzing: {self.pdf_path}")
        print("=" * 80)

        for page_num in range(len(self.doc)):
            self.analyze_page(page_num)

        self.doc.close()
        return self.results

    def analyze_page(self, page_num):
        """Analyze a single page for untagged text."""
        page = self.doc[page_num]

        try:
            # Get all content streams for this page
            xrefs = page.get_contents()
            if not isinstance(xrefs, list):
                xrefs = [xrefs] if xrefs else []

            for xref in xrefs:
                stream = self.doc.xref_stream(xref)
                if not stream:
                    continue

                content = stream.decode('latin-1', errors='ignore')
                self.analyze_content_stream(content, page_num + 1)

        except Exception as e:
            print(f"Error analyzing page {page_num + 1}: {e}")

    def analyze_content_stream(self, content, page_num):
        """Analyze a content stream for untagged text."""

        # Method 1: Line-by-line with proper nesting stack
        lines = content.split('\n')
        nesting_stack = []

        for line_num, line in enumerate(lines):
            # Track nesting with a stack
            if 'BDC' in line:
                nesting_stack.append('BDC')
            elif 'BMC' in line:
                nesting_stack.append('BMC')
            elif 'EMC' in line:
                if nesting_stack:
                    nesting_stack.pop()

            # Check for text operators
            if self.has_text_operator(line):
                is_tagged = len(nesting_stack) > 0

                if not is_tagged:
                    text_content = self.extract_text_from_line(line)
                    self.record_untagged(page_num, line_num, line, text_content, 'line_analysis')

        # Method 2: Find all BT...ET blocks
        self.analyze_bt_blocks(content, page_num)

        # Method 3: Regex pattern matching for all text operators
        self.analyze_with_patterns(content, page_num)

    def has_text_operator(self, line):
        """Check if line contains text showing operators."""
        # Match Tj, TJ, ', " (text operators)
        return bool(re.search(r'\bTj\b|\bTJ\b|\'(?!\w)|"(?!\w)', line))

    def extract_text_from_line(self, line):
        """Extract actual text content from a line."""
        results = []

        # Extract from parentheses (Tj operator)
        paren_matches = re.findall(r'\(([^)]*)\)', line)
        for match in paren_matches:
            # Decode escape sequences
            decoded = self.decode_pdf_string(match)
            results.append(decoded)

        # Extract from hex strings <...>
        hex_matches = re.findall(r'<([0-9A-Fa-f]+)>', line)
        for match in hex_matches:
            try:
                # Convert hex to string
                if len(match) % 2 == 0:
                    decoded = bytes.fromhex(match).decode('utf-16-be', errors='ignore')
                    results.append(decoded)
            except:
                results.append(f"<{match}>")

        # Extract from TJ arrays
        tj_arrays = re.findall(r'\[(.*?)\]\s*TJ', line)
        for array_content in tj_arrays:
            # Parse array elements
            elements = re.findall(r'\(([^)]*)\)', array_content)
            for elem in elements:
                decoded = self.decode_pdf_string(elem)
                results.append(decoded)

        return ' '.join(results) if results else line.strip()[:50]

    def decode_pdf_string(self, pdf_string):
        """Decode PDF string with escape sequences."""
        # Handle common escape sequences
        result = pdf_string
        result = result.replace('\\n', '\n')
        result = result.replace('\\r', '\r')
        result = result.replace('\\t', '\t')
        result = result.replace('\\(', '(')
        result = result.replace('\\)', ')')
        result = result.replace('\\\\', '\\')

        # Handle octal sequences
        import re
        def octal_replace(match):
            octal = match.group(1)
            try:
                char_code = int(octal, 8)
                return chr(char_code)
            except:
                return match.group(0)

        result = re.sub(r'\\(\d{1,3})', octal_replace, result)

        # Visual representation for whitespace
        if result.strip() == '':
            if '\n' in result:
                return '[NEWLINE]'
            elif '\r' in result:
                return '[CR]'
            elif '\t' in result:
                return '[TAB]'
            elif result == ' ':
                return '[SPACE]'
            elif result:
                return f'[WHITESPACE:{repr(result)}]'

        return result

    def analyze_bt_blocks(self, content, page_num):
        """Find and analyze all BT...ET blocks."""
        # Pattern to find BT...ET blocks
        bt_pattern = r'BT(.*?)ET'
        bt_blocks = re.findall(bt_pattern, content, re.DOTALL)

        for block_num, block in enumerate(bt_blocks):
            # Check if this block has any marked content
            has_bdc = 'BDC' in block
            has_bmc = 'BMC' in block

            if not has_bdc and not has_bmc:
                # This entire BT block is untagged
                text_ops = re.findall(r'(\([^)]*\)|\<[^>]*\>)\s*(?:Tj|TJ|\'|")', block)
                if text_ops:
                    for op in text_ops:
                        self.record_untagged(page_num, block_num, op,
                                           self.decode_pdf_string(op.strip('()<>')),
                                           'bt_block_analysis')

    def analyze_with_patterns(self, content, page_num):
        """Use comprehensive regex patterns to find untagged text."""

        # Pattern 1: Whitespace-only Tj operations
        whitespace_pattern = r'(\[)?\((\\(?:40|11|12|15|n|r|t)|\s)*\)(\])?\s*T[jJ]'

        # Pattern 2: Any Tj operation
        tj_pattern = r'\([^)]*\)\s*Tj'

        # Pattern 3: TJ arrays
        tj_array_pattern = r'\[.*?\]\s*TJ'

        # Pattern 4: Hex strings
        hex_pattern = r'<[0-9A-Fa-f]+>\s*Tj'

        patterns = [
            ('whitespace', whitespace_pattern),
            ('tj_simple', tj_pattern),
            ('tj_array', tj_array_pattern),
            ('hex_string', hex_pattern)
        ]

        for pattern_name, pattern in patterns:
            matches = list(re.finditer(pattern, content))

            for match in matches:
                # Check if this match is inside marked content
                before_text = content[:match.start()]

                # Count markers with proper nesting
                depth = self.calculate_depth_at_position(before_text)

                if depth == 0:
                    # This is untagged
                    self.record_untagged(page_num, match.start(), match.group(0),
                                       self.extract_text_from_line(match.group(0)),
                                       f'pattern_{pattern_name}')

    def calculate_depth_at_position(self, text_before):
        """Calculate marked content depth at a position."""
        depth = 0

        # Find all markers in order
        markers = []

        for match in re.finditer(r'(BDC|BMC|EMC)', text_before):
            markers.append((match.start(), match.group(0)))

        # Process markers in order
        for _, marker in markers:
            if marker in ['BDC', 'BMC']:
                depth += 1
            elif marker == 'EMC':
                depth = max(0, depth - 1)

        return depth

    def record_untagged(self, page_num, position, raw_content, decoded_content, detection_method):
        """Record an untagged text finding."""

        # Deduplicate based on position and content
        key = f"{page_num}:{position}:{decoded_content[:20]}"

        for existing in self.results['details']:
            if existing.get('key') == key:
                return  # Already recorded

        finding = {
            'key': key,
            'page': page_num,
            'position': position,
            'raw': raw_content[:100],
            'decoded': decoded_content,
            'method': detection_method
        }

        self.results['details'].append(finding)
        self.results['by_page'][page_num].append(finding)
        self.results['by_type'][detection_method] += 1
        self.results['total_untagged'] += 1

        if self.verbose:
            print(f"Page {page_num}, Pos {position}: {decoded_content} [{detection_method}]")

    def generate_report(self):
        """Generate a detailed report of findings."""
        print("\n" + "=" * 80)
        print("UNTAGGED TEXT DIAGNOSTIC REPORT")
        print("=" * 80)

        print(f"\nTotal Untagged Text Objects: {self.results['total_untagged']}")

        print("\nBy Page:")
        for page_num in sorted(self.results['by_page'].keys()):
            count = len(self.results['by_page'][page_num])
            print(f"  Page {page_num}: {count} untagged objects")

        print("\nBy Detection Method:")
        for method, count in self.results['by_type'].items():
            print(f"  {method}: {count}")

        print("\nDetailed Findings:")
        print("-" * 80)

        # Group by content type
        whitespace_only = []
        real_content = []

        for finding in self.results['details']:
            decoded = finding['decoded']
            if decoded.startswith('[') and decoded.endswith(']'):
                whitespace_only.append(finding)
            else:
                real_content.append(finding)

        if whitespace_only:
            print("\nWhitespace-Only Untagged Content:")
            for finding in whitespace_only[:10]:  # Show first 10
                print(f"  Page {finding['page']}: {finding['decoded']}")
                if self.verbose:
                    print(f"    Raw: {finding['raw'][:50]}")

        if real_content:
            print("\nReal Untagged Content:")
            for finding in real_content[:10]:  # Show first 10
                print(f"  Page {finding['page']}: {finding['decoded'][:50]}")
                if self.verbose:
                    print(f"    Raw: {finding['raw'][:50]}")

        print("\n" + "=" * 80)
        print(f"Summary: {self.results['total_untagged']} total untagged text objects found")
        print(f"         {len(whitespace_only)} whitespace-only")
        print(f"         {len(real_content)} with real content")

        # Save detailed JSON report
        report_file = self.pdf_path.replace('.pdf', '_diagnostic.json')
        with open(report_file, 'w') as f:
            json.dump(self.results, f, indent=2)
        print(f"\nDetailed report saved to: {report_file}")

        return self.results


def main():
    if len(sys.argv) < 2:
        print("Usage: python diagnose_untagged_comprehensive.py <pdf_file> [--verbose]")
        sys.exit(1)

    pdf_path = sys.argv[1]
    verbose = '--verbose' in sys.argv

    diagnostic = UntaggedTextDiagnostic(pdf_path, verbose)
    results = diagnostic.analyze()
    diagnostic.generate_report()

    # Exit with error code if untagged content found
    sys.exit(0 if results['total_untagged'] == 0 else 1)


if __name__ == "__main__":
    main()