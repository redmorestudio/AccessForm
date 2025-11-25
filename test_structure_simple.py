#!/usr/bin/env python3
import sys
import pikepdf

pdf_path = sys.argv[1] if len(sys.argv) > 1 else "test.pdf"

pdf = pikepdf.open(pdf_path)

if not hasattr(pdf.Root, 'StructTreeRoot'):
    print("❌ NO STRUCTURE TREE ROOT")
    sys.exit(1)

root = pdf.Root.StructTreeRoot
print(f"✅ StructTreeRoot exists")
print(f"   Type: {root.get('/Type', 'N/A')}")
print(f"   Keys: {list(root.keys())}")

if '/K' in root:
    k = root['/K']
    print(f"\n📊 /K (kids): {type(k)}")
    if isinstance(k, list):
        print(f"   Count: {len(k)} kids")
        for i, kid in enumerate(k[:5]):  # First 5 only
            if hasattr(kid, 'get'):
                print(f"   Kid {i}: S={kid.get('/S', 'N/A')}, Type={kid.get('/Type', 'N/A')}")
    else:
        if hasattr(k, 'get'):
            print(f"   Single kid: S={k.get('/S', 'N/A')}, Type={k.get('/Type', 'N/A')}")
else:
    print("\n❌ NO /K (kids) - structure tree is EMPTY!")
