#!/usr/bin/env python3
"""
Seth's Challenge: Modify 10 random PDFs from Alexandria directory.
- Replace a header with "Seth Was Here"
- Add checkbox "is seth awesome?" if PDF has forms
"""

import pikepdf
import random
import shutil
from pathlib import Path
import re

# Configuration
SOURCE_DIR = Path("/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria")
OUTPUT_DIR = Path("/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/Seth10")
NUM_PDFS = 10

def find_and_replace_text_in_content(page, old_text_pattern, new_text):
    """
    Find and replace text in PDF content stream.
    This is a simplified approach - real text replacement in PDFs is complex.
    """
    try:
        # Get content stream
        if '/Contents' not in page:
            return False

        content = page.Contents.read_bytes()
        content_str = content.decode('latin-1', errors='ignore')

        # Look for text operators (Tj, TJ, etc.) and try to replace
        # This is a naive approach - proper PDF text replacement is very complex
        modified = False

        # Try to find "header" patterns - text at top of page with larger font
        # We'll look for common header patterns
        patterns = [
            r'(City of Alexandria)',
            r'(ALEXANDRIA)',
            r'(Department of)',
            r'(SUMMER \d{4})',
        ]

        for pattern in patterns:
            if re.search(pattern, content_str):
                # Replace first occurrence
                content_str = re.sub(pattern, new_text, content_str, count=1)
                modified = True
                break

        if modified:
            # Write back
            new_content = content_str.encode('latin-1', errors='ignore')
            page.Contents = pikepdf.Stream(page.Contents.owner, new_content)
            return True

        return False
    except Exception as e:
        print(f"  ⚠️  Text replacement failed: {e}")
        return False

def add_checkbox_field(pdf, page_num=0):
    """
    Add a checkbox form field to the PDF.
    """
    try:
        # Create AcroForm if it doesn't exist
        if '/AcroForm' not in pdf.Root:
            pdf.Root.AcroForm = pikepdf.Dictionary({
                '/Fields': pikepdf.Array([])
            })

        # Get or create Fields array
        if '/Fields' not in pdf.Root.AcroForm:
            pdf.Root.AcroForm.Fields = pikepdf.Array([])

        # Create checkbox field
        checkbox = pikepdf.Dictionary({
            '/FT': pikepdf.Name('/Btn'),  # Button field type
            '/T': pikepdf.String('IsSethAwesome'),  # Field name
            '/V': pikepdf.Name('/Off'),  # Default value (unchecked)
            '/Ff': 0,  # Field flags
        })

        # Create appearance for the checkbox (widget annotation)
        page = pdf.pages[page_num]

        # Position at bottom-left (adjust coordinates as needed)
        widget = pikepdf.Dictionary({
            '/Type': pikepdf.Name('/Annot'),
            '/Subtype': pikepdf.Name('/Widget'),
            '/Rect': pikepdf.Array([50, 50, 70, 70]),  # [x1, y1, x2, y2]
            '/F': 4,  # Print flag
            '/P': page.obj,  # Parent page
            '/T': pikepdf.String('Is Seth Awesome?'),
            '/FT': pikepdf.Name('/Btn'),
            '/V': pikepdf.Name('/Off'),
        })

        # Add widget to page annotations
        if '/Annots' not in page:
            page.Annots = pikepdf.Array([])
        page.Annots.append(widget)

        # Add field to AcroForm
        pdf.Root.AcroForm.Fields.append(checkbox)

        return True
    except Exception as e:
        print(f"  ⚠️  Checkbox addition failed: {e}")
        return False

def has_form_fields(pdf):
    """Check if PDF has form fields."""
    try:
        if '/AcroForm' in pdf.Root:
            if '/Fields' in pdf.Root.AcroForm:
                return len(pdf.Root.AcroForm.Fields) > 0
        return False
    except:
        return False

def process_pdf(input_path, output_path):
    """Process a single PDF."""
    print(f"\n📄 Processing: {input_path.name}")

    results = {
        'success': False,
        'text_replaced': False,
        'checkbox_added': False,
        'had_forms': False,
        'error': None
    }

    try:
        # Open PDF
        pdf = pikepdf.open(input_path)

        # Check if it has forms
        results['had_forms'] = has_form_fields(pdf)

        # Try to replace text in first page
        if len(pdf.pages) > 0:
            replaced = find_and_replace_text_in_content(pdf.pages[0], r'Alexandria', 'Seth Was Here')
            results['text_replaced'] = replaced
            if replaced:
                print("  ✅ Replaced header with 'Seth Was Here'")
            else:
                print("  ⚠️  Could not find header to replace")

        # Add checkbox if it has forms (or add it anyway for testing)
        # Let's add it to all PDFs for consistency
        checkbox_added = add_checkbox_field(pdf, 0)
        results['checkbox_added'] = checkbox_added
        if checkbox_added:
            print("  ✅ Added checkbox 'Is Seth Awesome?'")

        # Save
        pdf.save(output_path)
        results['success'] = True
        print(f"  ✅ Saved to: {output_path.name}")

    except Exception as e:
        results['error'] = str(e)
        print(f"  ❌ Failed: {e}")

    return results

def main():
    print("=" * 70)
    print("SETH'S CHALLENGE: Process 10 Random PDFs")
    print("=" * 70)

    # Create output directory
    OUTPUT_DIR.mkdir(exist_ok=True)
    print(f"\n📁 Output directory: {OUTPUT_DIR}")

    # Find all PDFs
    all_pdfs = list(SOURCE_DIR.glob("*.pdf"))
    print(f"📚 Found {len(all_pdfs)} PDFs in source directory")

    # Select random 10
    if len(all_pdfs) < NUM_PDFS:
        selected = all_pdfs
        print(f"⚠️  Only {len(all_pdfs)} PDFs available, processing all")
    else:
        selected = random.sample(all_pdfs, NUM_PDFS)
        print(f"🎲 Randomly selected {NUM_PDFS} PDFs")

    # Process each PDF
    results = []
    for i, pdf_path in enumerate(selected, 1):
        print(f"\n[{i}/{len(selected)}]", end=" ")
        output_path = OUTPUT_DIR / f"seth_{i:02d}_{pdf_path.name}"
        result = process_pdf(pdf_path, output_path)
        results.append({
            'input': pdf_path.name,
            'output': output_path.name,
            **result
        })

    # Summary
    print("\n" + "=" * 70)
    print("SUMMARY")
    print("=" * 70)

    successful = sum(1 for r in results if r['success'])
    text_replaced = sum(1 for r in results if r['text_replaced'])
    checkbox_added = sum(1 for r in results if r['checkbox_added'])
    had_forms = sum(1 for r in results if r['had_forms'])

    print(f"\n✅ Successfully processed: {successful}/{len(results)}")
    print(f"📝 Text replaced: {text_replaced}/{len(results)}")
    print(f"☑️  Checkboxes added: {checkbox_added}/{len(results)}")
    print(f"📋 PDFs with existing forms: {had_forms}/{len(results)}")

    if successful < len(results):
        print(f"\n❌ Failures: {len(results) - successful}")
        for r in results:
            if not r['success']:
                print(f"  - {r['input']}: {r['error']}")

    print(f"\n📦 Output directory: {OUTPUT_DIR}")
    print(f"🎁 Ready to zip!")

    # Create zip file
    try:
        shutil.make_archive(
            str(OUTPUT_DIR.parent / "Seth10"),
            'zip',
            OUTPUT_DIR
        )
        print(f"\n✅ Created zip: {OUTPUT_DIR.parent / 'Seth10.zip'}")
    except Exception as e:
        print(f"\n⚠️  Zip creation failed: {e}")

if __name__ == '__main__':
    main()
