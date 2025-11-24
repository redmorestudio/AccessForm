#!/usr/bin/env python3
"""
Quick test to verify that pikepdf can persist BDC/EMC markers.
This isolates the save/reload issue.
"""

import pikepdf
from io import BytesIO

# Test 1: Create minimal PDF with BDC/EMC
def test_bdc_persistence():
    print("=" * 80)
    print("TEST 1: Create minimal PDF with BDC/EMC markers")
    print("=" * 80)

    # Create minimal PDF
    pdf = pikepdf.Pdf.new()
    page = pdf.add_blank_page(page_size=(612, 792))

    # Add simple content stream with BDC/EMC
    content_stream = b"""
BT
/F1 12 Tf
100 700 Td
/P <</MCID 0>> BDC
(Hello World) Tj
EMC
ET
"""

    # Set content stream
    page.Contents = pikepdf.Stream(pdf, content_stream)

    # Save to bytes
    output = BytesIO()
    pdf.save(output, normalize_content=False, compress_streams=False)
    pdf_bytes = output.getvalue()

    # Check markers in bytes
    bdc_count = pdf_bytes.count(b'BDC')
    emc_count = pdf_bytes.count(b'EMC')

    print(f"After save: {bdc_count} BDC, {emc_count} EMC")

    # Reload and check again
    pdf2 = pikepdf.open(BytesIO(pdf_bytes))
    page2 = pdf2.pages[0]
    content_bytes2 = bytes(page2.Contents.read_bytes())

    bdc_count2 = content_bytes2.count(b'BDC')
    emc_count2 = content_bytes2.count(b'EMC')

    print(f"After reload: {bdc_count2} BDC, {emc_count2} EMC")

    if bdc_count2 > 0 and emc_count2 > 0:
        print("✅ TEST 1 PASSED: Markers persist through save/reload")
    else:
        print("❌ TEST 1 FAILED: Markers disappeared")

    return bdc_count2 > 0 and emc_count2 > 0


# Test 2: Modify existing PDF content stream
def test_modify_existing():
    print("\n" + "=" * 80)
    print("TEST 2: Modify existing PDF and add BDC/EMC")
    print("=" * 80)

    # Load Erie PDF
    erie_path = "StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0051_Route 5 August 2025.pdf"

    try:
        pdf = pikepdf.open(erie_path)
    except FileNotFoundError:
        print(f"❌ Erie PDF not found at {erie_path}")
        return False

    page = pdf.pages[0]

    # Parse existing content
    try:
        instructions = pikepdf.parse_content_stream(page)
        print(f"Parsed {len(instructions)} instructions from page 1")
    except Exception as e:
        print(f"❌ Failed to parse: {e}")
        return False

    # Insert BDC/EMC around first 10 instructions
    from pikepdf import Operator, Array, Dictionary, Name

    wrapped = []

    # Add BDC marker
    mcid_dict = Dictionary(MCID=0)
    wrapped.append((Array([Name.P, mcid_dict]), Operator("BDC")))

    # Add first 10 original instructions
    wrapped.extend(instructions[:min(10, len(instructions))])

    # Add EMC marker
    wrapped.append((Array(), Operator("EMC")))

    # Add remaining instructions
    wrapped.extend(instructions[10:])

    print(f"Built wrapped instruction list with {len(wrapped)} instructions")

    # Unparse back to stream
    try:
        new_stream = pikepdf.unparse_content_stream(wrapped)
        print(f"Unparsed to {len(new_stream)} bytes")
    except Exception as e:
        print(f"❌ Failed to unparse: {e}")
        return False

    # Replace content
    page.Contents = pikepdf.Stream(pdf, new_stream)

    # Save
    output = BytesIO()
    try:
        pdf.save(output, normalize_content=False, compress_streams=False)
        pdf_bytes = output.getvalue()
    except Exception as e:
        print(f"❌ Failed to save: {e}")
        return False

    # Check markers
    bdc_count = pdf_bytes.count(b'BDC')
    emc_count = pdf_bytes.count(b'EMC')

    print(f"After save: {bdc_count} BDC, {emc_count} EMC")

    # Reload and verify
    try:
        pdf2 = pikepdf.open(BytesIO(pdf_bytes))
        page2 = pdf2.pages[0]
        content_bytes2 = bytes(page2.Contents.read_bytes())

        bdc_count2 = content_bytes2.count(b'BDC')
        emc_count2 = content_bytes2.count(b'EMC')

        print(f"After reload: {bdc_count2} BDC, {emc_count2} EMC")

        if bdc_count2 > 0 and emc_count2 > 0:
            print("✅ TEST 2 PASSED: Markers persist in modified PDF")

            # Save test output
            with open("test_pikepdf_erie_output.pdf", "wb") as f:
                f.write(pdf_bytes)
            print("Saved test output to test_pikepdf_erie_output.pdf")

            return True
        else:
            print("❌ TEST 2 FAILED: Markers disappeared after reload")
            return False

    except Exception as e:
        print(f"❌ Failed to reload: {e}")
        return False


if __name__ == "__main__":
    test1_passed = test_bdc_persistence()
    test2_passed = test_modify_existing()

    print("\n" + "=" * 80)
    print("SUMMARY")
    print("=" * 80)
    print(f"Test 1 (minimal PDF): {'✅ PASS' if test1_passed else '❌ FAIL'}")
    print(f"Test 2 (Erie PDF):    {'✅ PASS' if test2_passed else '❌ FAIL'}")

    if test1_passed and test2_passed:
        print("\n✅✅✅ pikepdf CAN persist BDC/EMC markers!")
        print("Issue must be elsewhere in the pipeline.")
    else:
        print("\n❌ pikepdf has marker persistence issues")
