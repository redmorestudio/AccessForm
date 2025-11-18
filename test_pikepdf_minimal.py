#!/usr/bin/env python3
"""Minimal test to understand pikepdf unparsing"""

import pikepdf
from pikepdf import Pdf

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

# Show first instruction with BDC (instruction 2)
print(f"\nInstruction 2 (BDC): {instructions[2]}")
print(f"  Type: {type(instructions[2])}")
print(f"  Operands: {instructions[2].operands}")
print(f"  Operator: {instructions[2].operator}")

# Test 1: Try to unparse JUST the existing instructions unchanged
print("\n=== TEST 1: Unparse existing instructions unchanged ===")
try:
    test_stream = pikepdf.unparse_content_stream(instructions)
    print(f"✅ SUCCESS: Unparsed {len(test_stream)} bytes")
except Exception as e:
    print(f"❌ FAILED: {e}")

# Test 2: Try to unparse with one new simple instruction (just 'q')
print("\n=== TEST 2: Add simple 'q' instruction ===")
try:
    new_instructions = []
    new_instructions.append([pikepdf.Operator("q")])  # Save graphics state
    new_instructions.extend(instructions)
    new_instructions.append([pikepdf.Operator("Q")])  # Restore graphics state
    test_stream = pikepdf.unparse_content_stream(new_instructions)
    print(f"✅ SUCCESS: Unparsed {len(test_stream)} bytes")
except Exception as e:
    print(f"❌ FAILED: {e}")

# Test 3: Create BDC using exact same operands as instruction 2
print("\n=== TEST 3: Create BDC with same operands as instruction 2 ===")
try:
    new_instructions = []
    # Copy operands from instruction 2
    bdc_operands = list(instructions[2].operands)
    bdc_instruction = bdc_operands + [instructions[2].operator]
    new_instructions.append(bdc_instruction)
    new_instructions.extend(instructions)
    test_stream = pikepdf.unparse_content_stream(new_instructions)
    print(f"✅ SUCCESS: Unparsed {len(test_stream)} bytes")
except Exception as e:
    print(f"❌ FAILED: {e}")

# Test 4: Try to create ContentStreamInstruction object
print("\n=== TEST 4: Try ContentStreamInstruction constructor ===")
try:
    from pikepdf import ContentStreamInstruction
    new_instr = ContentStreamInstruction([pikepdf.Name("/Span"), pikepdf.Name("/MCID"), 0], pikepdf.Operator("BDC"))
    print(f"Created: {new_instr}")
    new_instructions = [new_instr] + list(instructions)
    test_stream = pikepdf.unparse_content_stream(new_instructions)
    print(f"✅ SUCCESS: Unparsed {len(test_stream)} bytes")
except ImportError as e:
    print(f"❌ ContentStreamInstruction not importable: {e}")
except Exception as e:
    print(f"❌ FAILED: {e}")

# Test 5: Use pikepdf's higher-level API - parse_content_stream with modification
print("\n=== TEST 5: Modify at PDF level instead of content stream level ===")
try:
    # Instead of modifying content stream, try directly modifying the PDF stream object
    original_stream = page.Contents.read_bytes()
    print(f"Original content stream: {len(original_stream)} bytes")

    # Insert BDC/EMC markers as raw PDF bytes
    bdc_marker = b"/Span << /MCID 0 >> BDC\n"
    emc_marker = b"EMC\n"

    new_stream_bytes = bdc_marker + original_stream + emc_marker
    print(f"New content stream: {len(new_stream_bytes)} bytes")

    # Try replacing the content stream
    page.Contents = pikepdf.Stream(pdf, new_stream_bytes)

    # Save and verify
    output_path = "./test_raw_stream_output.pdf"
    pdf.save(output_path)
    print(f"✅ SUCCESS: Saved to {output_path}")

    # Re-open and check
    pdf2 = Pdf.open(output_path)
    verify_stream = bytes(pdf2.pages[0].Contents.read_bytes())
    verify_bdc = verify_stream.count(b'BDC')
    verify_emc = verify_stream.count(b'EMC')
    print(f"Verification: {verify_bdc} BDC, {verify_emc} EMC in stream")
    pdf2.close()

except Exception as e:
    print(f"❌ FAILED: {e}")
    import traceback
    traceback.print_exc()

pdf.close()
print("\nTest complete!")
