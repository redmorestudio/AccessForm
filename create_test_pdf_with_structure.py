#!/usr/bin/env python3
"""
Create a test PDF with:
1. A structure tree with semantic elements (H1, P)
2. BDC/EMC markers in content streams with MCIDs
3. But NO MCR kids linking them together

This simulates the state after iText7 creates structure but before MCR kids are added.
"""
import pikepdf
from pikepdf import Array, Dictionary, Name, Stream

# Create a new PDF
pdf = pikepdf.new()

# Add a single page
page = pdf.add_blank_page(page_size=(612, 792))  # Letter size

# Create content stream with BDC/EMC markers
content = b"""
BT
/F1 24 Tf
50 700 Td
/P /MCID 0 BDC
(This is a heading) Tj
EMC
0 -50 Td
/P /MCID 1 BDC
(This is a paragraph of text.) Tj
EMC
ET
"""

# Set page content
page.Contents = Stream(pdf, content)

# Add a basic font to the page resources
if '/Resources' not in page:
    page.Resources = Dictionary()
if '/Font' not in page.Resources:
    page.Resources.Font = Dictionary()

page.Resources.Font.F1 = pdf.make_indirect(Dictionary(
    Type=Name('/Font'),
    Subtype=Name('/Type1'),
    BaseFont=Name('/Helvetica')
))

# Create a structure tree with elements but NO MCR kids
struct_root = Dictionary(
    Type=Name('/StructTreeRoot'),
    K=Array([]),
    ParentTree=Dictionary(Nums=Array([])),
    ParentTreeNextKey=0
)

# Create H1 element (no MCR kids yet)
h1_elem = Dictionary(
    Type=Name('/StructElem'),
    S=Name('/H1'),
    P=struct_root
    # No /K - this element has no kids yet
)

# Create P element (no MCR kids yet)
p_elem = Dictionary(
    Type=Name('/StructElem'),
    S=Name('/P'),
    P=struct_root
    # No /K - this element has no kids yet
)

# Make elements indirect objects
h1_elem = pdf.make_indirect(h1_elem)
p_elem = pdf.make_indirect(p_elem)

# Add elements to structure tree
struct_root.K = Array([h1_elem, p_elem])

# Make struct root indirect and add to catalog
pdf.Root.StructTreeRoot = pdf.make_indirect(struct_root)

# Save the PDF
output_path = "test_pdf_with_structure_no_mcr.pdf"
pdf.save(output_path)
print(f"✅ Created test PDF: {output_path}")
print("   - Has StructTreeRoot with 2 elements (H1, P)")
print("   - Has BDC/EMC markers with MCIDs 0, 1")
print("   - Elements have NO MCR kids (not linked to content)")
