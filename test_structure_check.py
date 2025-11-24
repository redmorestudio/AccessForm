#!/usr/bin/env python3
"""
Quick test to check if semantic structure exists in a PDF
"""
import sys
import pikepdf

if len(sys.argv) < 2:
    print("Usage: python3 test_structure_check.py <pdf_file>")
    sys.exit(1)

pdf_path = sys.argv[1]
print(f"\n🔍 Checking structure in: {pdf_path}")
print("=" * 70)

try:
    pdf = pikepdf.open(pdf_path)

    # Check if PDF has structure tree root
    if not hasattr(pdf.Root, 'StructTreeRoot'):
        print("❌ NO STRUCTURE TREE ROOT FOUND")
        sys.exit(1)

    root = pdf.Root.StructTreeRoot
    print("✅ StructTreeRoot exists")

    # Count structure elements
    def count_tags(elem, counts=None, depth=0):
        if counts is None:
            counts = {}

        if hasattr(elem, 'S'):
            tag = str(elem.S).replace('/', '')
            counts[tag] = counts.get(tag, 0) + 1

        if hasattr(elem, 'K'):
            kids = elem.K
            if not isinstance(kids, list):
                kids = [kids]
            for kid in kids:
                if hasattr(kid, 'Type') and str(kid.Type) == '/StructElem':
                    count_tags(kid, counts, depth + 1)

        return counts

    tag_counts = count_tags(root)

    if not tag_counts:
        print("❌ STRUCTURE TREE IS EMPTY (no structure elements found)")
        sys.exit(1)

    print(f"\n📊 Found {sum(tag_counts.values())} structure elements:")
    print("-" * 70)
    for tag, count in sorted(tag_counts.items()):
        print(f"  {tag:15} : {count:4d}")

    # Check for semantic tags
    semantic_tags = ['H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'P', 'Table', 'TR', 'TD', 'TH']
    found_semantic = [tag for tag in semantic_tags if tag in tag_counts]

    if found_semantic:
        print(f"\n✅ SEMANTIC STRUCTURE PRESERVED!")
        print(f"   Found semantic tags: {', '.join(found_semantic)}")
    else:
        print(f"\n⚠️  NO SEMANTIC TAGS FOUND (only structural containers)")
        print(f"   This means H1, H2, P, Table, etc. are missing!")

except Exception as e:
    print(f"❌ ERROR: {e}")
    import traceback
    traceback.print_exc()
    sys.exit(1)
