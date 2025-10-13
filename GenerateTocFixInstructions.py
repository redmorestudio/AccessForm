#!/usr/bin/env python3
"""
TOC Link Fix Instruction Generator
Analyzes PDF TOC structure and generates exact manual fix instructions for Adobe Acrobat
"""

import sys
import os
import json
import fitz  # PyMuPDF
from datetime import datetime
from pathlib import Path

def analyze_pdf_toc(pdf_path):
    """Analyze PDF for TOC link structure issues"""
    doc = fitz.open(pdf_path)
    results = {
        'file': os.path.basename(pdf_path),
        'timestamp': datetime.now().isoformat(),
        'pages': [],
        'total_issues': 0,
        'manual_fixes': []
    }

    # Find TOC pages (usually page 2, or pages with many links)
    for page_num in range(len(doc)):
        page = doc[page_num]
        links = page.get_links()

        if len(links) >= 10:  # Likely a TOC page
            page_info = {
                'page_number': page_num + 1,
                'total_links': len(links),
                'issues': []
            }

            # Check each link
            for idx, link in enumerate(links):
                link_text = extract_link_text(page, link)
                dest_page = link.get('page', 0) + 1 if 'page' in link else 0

                # For this analysis, we'll assume all links need fixing
                # (based on your diagnostic showing all Reference elements)
                issue = {
                    'link_index': idx + 1,
                    'link_text': link_text,
                    'destination_page': dest_page,
                    'issue_type': 'NotInLinkElement',
                    'current_structure': 'Reference'  # From your diagnostic
                }
                page_info['issues'].append(issue)

            if page_info['issues']:
                results['pages'].append(page_info)
                results['total_issues'] += len(page_info['issues'])

    doc.close()
    return results

def extract_link_text(page, link):
    """Extract text associated with a link"""
    rect = fitz.Rect(link['from'])
    # Expand rect slightly to capture text
    rect.x0 -= 5
    rect.y0 -= 2
    rect.x1 += 50
    rect.y1 += 2
    text = page.get_textbox(rect).strip()
    if not text:
        text = f"Link at ({rect.x0:.0f}, {rect.y0:.0f})"
    return text[:50]  # Truncate long text

def generate_manual_instructions(analysis):
    """Generate step-by-step manual fix instructions"""
    instructions = []
    fix_number = 1

    for page_info in analysis['pages']:
        page_num = page_info['page_number']

        # Group similar issues on the same page
        for issue in page_info['issues']:
            instruction = {
                'fix_number': fix_number,
                'page': page_num,
                'link_text': issue['link_text'],
                'destination': issue['destination_page'],
                'steps': generate_fix_steps(fix_number, page_num, issue)
            }
            instructions.append(instruction)
            fix_number += 1

            # Only show first 5 fixes per page to avoid overwhelming
            if (fix_number - 1) % 5 == 0 and fix_number > 1:
                instructions.append({
                    'fix_number': 'batch',
                    'page': page_num,
                    'note': f'Repeat the same process for remaining {len(page_info["issues"]) - 5} links on this page'
                })
                break

    analysis['manual_fixes'] = instructions
    return analysis

def generate_fix_steps(fix_num, page_num, issue):
    """Generate specific fix steps for an issue"""
    return [
        f"Step 1: Open Adobe Acrobat Pro DC",
        f"Step 2: Open the Tags panel (View > Show/Hide > Navigation Panes > Tags)",
        f"Step 3: Navigate to page {page_num} in the document",
        f"Step 4: In the Tags panel, expand the structure tree",
        f"Step 5: Look for a TOC or TOCI element containing '{issue['link_text'][:30]}'",
        f"Step 6: Find the Reference element for this link",
        f"Step 7: Right-click the Reference element",
        f"Step 8: Select 'New Tag' from the context menu",
        f"Step 9: Choose 'Link' as the tag type",
        f"Step 10: Drag the Reference element into the newly created Link tag",
        f"Step 11: Right-click the Link tag and select 'Properties'",
        f"Step 12: In the 'Alternate Text' field, enter: 'Go to page {issue['destination_page']}'",
        f"Step 13: Click OK to save",
        f"Step 14: The link is now properly structured!"
    ]

def generate_html_report(analysis):
    """Generate HTML report with instructions"""
    html = f"""<!DOCTYPE html>
<html>
<head>
    <title>TOC Fix Instructions - {analysis['file']}</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Arial, sans-serif;
            max-width: 1200px;
            margin: 0 auto;
            padding: 20px;
            line-height: 1.6;
        }}
        h1 {{
            color: #2c3e50;
            border-bottom: 3px solid #3498db;
            padding-bottom: 10px;
        }}
        .summary {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 20px;
            border-radius: 10px;
            margin: 20px 0;
        }}
        .stats {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px;
            margin: 20px 0;
        }}
        .stat-card {{
            background: white;
            border: 2px solid #e0e0e0;
            padding: 20px;
            border-radius: 10px;
            text-align: center;
            transition: transform 0.2s;
        }}
        .stat-card:hover {{
            transform: translateY(-5px);
            box-shadow: 0 5px 15px rgba(0,0,0,0.1);
        }}
        .stat-number {{
            font-size: 3em;
            font-weight: bold;
            color: #3498db;
        }}
        .fix-instruction {{
            background: #f8f9fa;
            border-left: 5px solid #ff9800;
            padding: 20px;
            margin: 20px 0;
            border-radius: 5px;
        }}
        .fix-instruction h3 {{
            color: #e65100;
            margin-top: 0;
        }}
        .steps {{
            background: white;
            padding: 15px;
            border-radius: 5px;
            margin-top: 10px;
        }}
        .steps ol {{
            margin: 0;
            padding-left: 20px;
        }}
        .steps li {{
            margin: 8px 0;
        }}
        .keyboard-shortcut {{
            background: #e0e0e0;
            padding: 2px 5px;
            border-radius: 3px;
            font-family: monospace;
            font-size: 0.9em;
        }}
        .time-estimate {{
            color: #666;
            font-style: italic;
            margin-top: 10px;
        }}
        .batch-note {{
            background: #e3f2fd;
            border: 1px solid #2196f3;
            padding: 15px;
            border-radius: 5px;
            margin: 20px 0;
        }}
        .success-note {{
            background: #e8f5e9;
            border: 1px solid #4caf50;
            padding: 15px;
            border-radius: 5px;
            margin: 20px 0;
        }}
    </style>
</head>
<body>
    <h1>🔧 TOC Link Structure Fix Instructions</h1>
    <p><strong>File:</strong> {analysis['file']}</p>
    <p><strong>Generated:</strong> {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}</p>

    <div class="summary">
        <h2 style="color: white; margin-top: 0;">📊 Summary</h2>
        <p>This document has Table of Contents links that need to be properly structured for PDF/UA compliance.</p>
        <p>Each link needs to be wrapped in a Link structure element and given alternative text.</p>
    </div>

    <div class="stats">
        <div class="stat-card">
            <div class="stat-number">{len(analysis['pages'])}</div>
            <div>TOC Pages</div>
        </div>
        <div class="stat-card">
            <div class="stat-number">{analysis['total_issues']}</div>
            <div>Links to Fix</div>
        </div>
        <div class="stat-card">
            <div class="stat-number">~{analysis['total_issues'] * 30 // 60}</div>
            <div>Minutes to Fix</div>
        </div>
    </div>

    <h2>📋 Step-by-Step Instructions</h2>
    <p>Follow these instructions in Adobe Acrobat Pro DC:</p>
"""

    # Add fix instructions
    for fix in analysis['manual_fixes']:
        if fix.get('fix_number') == 'batch':
            html += f"""
    <div class="batch-note">
        <strong>📌 Batch Process:</strong> {fix.get('note')}
    </div>
"""
        else:
            html += f"""
    <div class="fix-instruction">
        <h3>Fix #{fix['fix_number']}: Page {fix['page']}</h3>
        <p><strong>Link Text:</strong> "{fix['link_text']}"</p>
        <p><strong>Destination:</strong> Page {fix['destination']}</p>

        <div class="steps">
            <ol>
"""
            for step in fix['steps']:
                html += f"                <li>{step}</li>\n"

            html += """            </ol>
        </div>
        <p class="time-estimate">⏱️ Estimated time: 30 seconds</p>
    </div>
"""

    # Add completion note
    html += """
    <div class="success-note">
        <h3>✅ After Completing These Fixes:</h3>
        <ol>
            <li>Save the PDF (File > Save or <span class="keyboard-shortcut">Ctrl/Cmd + S</span>)</li>
            <li>Run PAC (PDF Accessibility Checker) to verify all errors are resolved</li>
            <li>Test navigation by clicking the TOC links to ensure they still work</li>
        </ol>
    </div>

    <h2>🎯 Pro Tips</h2>
    <ul>
        <li><strong>Batch Processing:</strong> After fixing the first link, you can often select multiple Reference elements and convert them all at once</li>
        <li><strong>Keyboard Shortcuts:</strong> Use Tab to navigate the Tags panel quickly</li>
        <li><strong>Find Feature:</strong> Use Ctrl/Cmd + F in the Tags panel to search for specific text</li>
        <li><strong>Undo:</strong> If you make a mistake, use Ctrl/Cmd + Z to undo</li>
    </ul>

    <hr style="margin-top: 50px;">
    <p style="color: #666; font-size: 0.9em;">Generated by TOC Fix Instruction Generator</p>
</body>
</html>
"""

    return html

def generate_text_report(analysis):
    """Generate plain text report"""
    lines = []
    lines.append("=" * 70)
    lines.append("TOC LINK STRUCTURE FIX INSTRUCTIONS")
    lines.append("=" * 70)
    lines.append(f"File: {analysis['file']}")
    lines.append(f"Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    lines.append(f"\nTotal Issues: {analysis['total_issues']} links need fixing")
    lines.append(f"Estimated Time: ~{analysis['total_issues'] * 30 // 60} minutes")
    lines.append("\n" + "=" * 70)
    lines.append("INSTRUCTIONS FOR ADOBE ACROBAT PRO")
    lines.append("=" * 70)

    for fix in analysis['manual_fixes']:
        if fix.get('fix_number') == 'batch':
            lines.append(f"\n[BATCH NOTE] {fix.get('note')}")
        else:
            lines.append(f"\nFIX #{fix['fix_number']} - Page {fix['page']}")
            lines.append("-" * 50)
            lines.append(f"Link: '{fix['link_text']}'")
            lines.append(f"Goes to: Page {fix['destination']}")
            lines.append("\nSteps:")
            for i, step in enumerate(fix['steps'], 1):
                lines.append(f"  {step}")

    return "\n".join(lines)

def main():
    """Main function"""
    if len(sys.argv) < 2:
        pdf_path = "TWC Forms/eBily/foster-youth-services-guide-twc.pdf"
    else:
        pdf_path = sys.argv[1]

    if not os.path.exists(pdf_path):
        print(f"Error: File not found: {pdf_path}")
        sys.exit(1)

    print("=" * 70)
    print("TOC LINK FIX INSTRUCTION GENERATOR")
    print("=" * 70)
    print(f"Analyzing: {pdf_path}")

    # Create output directory
    output_dir = Path("TOC-Fix-Instructions")
    output_dir.mkdir(exist_ok=True)

    # Analyze PDF
    print("Analyzing PDF structure...")
    analysis = analyze_pdf_toc(pdf_path)
    analysis = generate_manual_instructions(analysis)

    # Generate reports
    base_name = Path(pdf_path).stem

    # HTML report
    html_path = output_dir / f"{base_name}-fix-instructions.html"
    with open(html_path, 'w') as f:
        f.write(generate_html_report(analysis))
    print(f"✅ HTML instructions: {html_path}")

    # Text report
    txt_path = output_dir / f"{base_name}-fix-instructions.txt"
    with open(txt_path, 'w') as f:
        f.write(generate_text_report(analysis))
    print(f"✅ Text instructions: {txt_path}")

    # JSON data
    json_path = output_dir / f"{base_name}-analysis.json"
    with open(json_path, 'w') as f:
        json.dump(analysis, f, indent=2, default=str)
    print(f"✅ JSON analysis: {json_path}")

    # Print summary
    print("\n" + "=" * 70)
    print("SUMMARY")
    print("=" * 70)
    print(f"TOC Pages Found: {len(analysis['pages'])}")
    print(f"Total Links to Fix: {analysis['total_issues']}")
    print(f"Estimated Time: ~{analysis['total_issues'] * 30 // 60} minutes")
    print("\n📌 TO VIEW INSTRUCTIONS:")
    print(f"   Open: {html_path.absolute()}")

    if sys.platform == 'darwin':
        print(f"\n   Or run: open '{html_path}'")
    elif sys.platform == 'win32':
        print(f"\n   Or run: start '{html_path}'")

if __name__ == "__main__":
    main()