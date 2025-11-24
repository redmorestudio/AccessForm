import pikepdf

pdf = pikepdf.open('zoning_with_semantic_structure.pdf')
root = pdf.Root.StructTreeRoot

def count_tags(elem, counts=None, depth=0):
    if counts is None:
        counts = {}
    
    if hasattr(elem, 'S'):
        tag = str(elem.S)
        counts[tag] = counts.get(tag, 0) + 1
    
    if hasattr(elem, 'K'):
        kids = elem.K
        if not isinstance(kids, list):
            kids = [kids]
        for kid in kids:
            if hasattr(kid, 'Type') and kid.Type == '/StructElem':
                count_tags(kid, counts, depth + 1)
    
    return counts

tag_counts = count_tags(root)
print("\n📊 Semantic Structure Tag Counts:")
print("=" * 50)
for tag, count in sorted(tag_counts.items()):
    print(f"  {tag}: {count}")

print("\n✅ SEMANTIC STRUCTURE PRESERVED!" if any(tag in ['H1', 'H2', 'H3', 'P'] for tag in tag_counts) else "\n❌ NO SEMANTIC STRUCTURE - All artifacts!")
