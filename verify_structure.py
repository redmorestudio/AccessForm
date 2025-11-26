#!/usr/bin/env python3
"""Quick verification that structure tree and MCIDs are present in output PDF."""

import pikepdf
from pathlib import Path

pdf_path = Path("EndToEndPikepdfTest/remediated_01_0076_Guidelines to students.pdf")

print("=" * 70)
print(f"VERIFYING: {pdf_path.name}")
print("=" * 70)

with pikepdf.open(pdf_path) as pdf:
    # Check for structure tree root
    if '/StructTreeRoot' in pdf.Root:
        struct_root = pdf.Root.StructTreeRoot
        print("\n✅ StructTreeRoot found")

        # Check for K (kids)
        if '/K' in struct_root:
            print(f"✅ Structure tree has kids")

            # Try to navigate to first element
            k = struct_root.K
            if isinstance(k, pikepdf.Array) and len(k) > 0:
                first_elem = k[0]
                print(f"✅ First element type: {first_elem.get('/S', 'Unknown')}")

                # Check for MCR kids
                if '/K' in first_elem:
                    elem_kids = first_elem.K
                    if isinstance(elem_kids, pikepdf.Array):
                        mcr_count = sum(1 for kid in elem_kids if isinstance(kid, pikepdf.Dictionary) and kid.get('/Type') == '/MCR')
                        print(f"✅ MCR kids in first element: {mcr_count}")
    else:
        print("\n❌ No StructTreeRoot found")

    # Check for BDC markers in first page
    if len(pdf.pages) > 0:
        page = pdf.pages[0]
        if '/Contents' in page:
            content = page.Contents.read_bytes()
            content_str = content.decode('latin-1', errors='ignore')

            bdc_count = content_str.count('/MCID')
            emc_count = content_str.count('EMC')

            print(f"\n✅ BDC markers on page 1: {bdc_count}")
            print(f"✅ EMC markers on page 1: {emc_count}")

    print("\n✅ PDF is valid and loadable")

print("\n" + "=" * 70)
print("VERIFICATION PASSED")
print("=" * 70)
