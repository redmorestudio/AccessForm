#!/usr/bin/env python3
"""Direct test of pikepdf content stream rewriting"""

import pikepdf
from pikepdf import Pdf, Dictionary, Name, Operator
import sys

# Open a test PDF
pdf_path = "./alexandria_FINAL_WORKING.pdf"
print(f"Opening PDF: {pdf_path}")

pdf = Pdf.open(pdf_path)
page = pdf.pages[0]

print(f"PDF has {len(pdf.pages)} pages")

# Parse content stream
print("Parsing content stream...")
instructions = pikepdf.parse_content_stream(page)
print(f"Parsed {len(instructions)} instructions")

# Show format of first few instructions to understand the structure
print("\nFirst 5 instructions:")
for i, instr in enumerate(instructions[:5]):
    print(f"  {i}: {type(instr)} = {instr}")

# Build new instructions with BDC/EMC
print("\nBuilding new instruction stream...")
new_instructions = []

# Add BDC marker
# Match existing format: [tag, property_name, property_value, operator]
# Existing instruction 2: [Name("/P"), Name("/MCID"), 738, Operator("BDC")]
bdc_instruction = [Name.Span, Name.MCID, 0, Operator("BDC")]
new_instructions.append(bdc_instruction)
print(f"Added BDC instruction: {bdc_instruction}")

# Add original content
new_instructions.extend(instructions)
print(f"Added {len(instructions)} original instructions")

# Add EMC marker - just the operator in a list
new_instructions.append([Operator("EMC")])
print(f"Added EMC instruction: {new_instructions[-1]}")

print(f"Total new instructions: {len(new_instructions)}")

# Unparse back to stream
print("Unparsing to stream...")
try:
    new_stream = pikepdf.unparse_content_stream(new_instructions)
    print(f"Unparsed successfully: {len(new_stream)} bytes")

    # Check if BDC/EMC appear in the stream
    bdc_count = new_stream.count(b'BDC')
    emc_count = new_stream.count(b'EMC')
    print(f"Stream contains {bdc_count} BDC and {emc_count} EMC")

    # Show first 500 bytes
    print(f"\nFirst 500 bytes of new stream:")
    print(new_stream[:500])

except Exception as e:
    print(f"ERROR during unparse: {e}")
    import traceback
    traceback.print_exc()
    sys.exit(1)

# Replace page contents
print("\nReplacing page contents...")
try:
    page.contents_replace(new_stream)
    print("Contents replaced successfully")

    # Verify
    verify_stream = bytes(page.contents_coalesce())
    verify_bdc = verify_stream.count(b'BDC')
    verify_emc = verify_stream.count(b'EMC')
    print(f"After replace: {verify_bdc} BDC and {verify_emc} EMC")

except Exception as e:
    print(f"ERROR during replace: {e}")
    import traceback
    traceback.print_exc()
    sys.exit(1)

# Save PDF
output_path = "./test_pikepdf_output.pdf"
print(f"\nSaving to {output_path}...")
try:
    pdf.save(output_path)
    print("Saved successfully")

    # Re-open and verify
    pdf2 = Pdf.open(output_path)
    verify2_stream = bytes(pdf2.pages[0].contents_coalesce())
    verify2_bdc = verify2_stream.count(b'BDC')
    verify2_emc = verify2_stream.count(b'EMC')
    print(f"After save and reopen: {verify2_bdc} BDC and {verify2_emc} EMC")
    pdf2.close()

except Exception as e:
    print(f"ERROR during save: {e}")
    import traceback
    traceback.print_exc()
    sys.exit(1)

pdf.close()
print("\nTest complete!")
