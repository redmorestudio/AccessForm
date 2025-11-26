#!/usr/bin/env python3
import json

# Load test structure
with open('test_structure.json', 'r') as f:
    structure = json.load(f)

# Flatten nodes - LEAF ONLY (same as new orchestrator.py logic)
all_nodes = []
total_nodes = 0

def flatten(node_list):
    global total_nodes
    for node in node_list:
        total_nodes += 1
        children = node.get('children', [])
        
        if len(children) == 0:
            # Leaf node
            all_nodes.append(node)
            print(f"  ✓ Leaf: {node.get('role'):10s} id={node.get('id')}")
        else:
            # Container node - skip
            print(f"  ⊘ Skip: {node.get('role'):10s} id={node.get('id')} ({len(children)} children)")
            flatten(children)

flatten(structure.get('nodes', []))

print(f"\nTotal nodes in tree: {total_nodes}")
print(f"Leaf nodes for mapping: {len(all_nodes)}")
print(f"Container nodes skipped: {total_nodes - len(all_nodes)}")

# Simulate segment mapping
segments = ["Text 1", "Text 2", "Text 3", "Text 4"]
print(f"\nMapping {len(segments)} segments to {len(all_nodes)} leaf nodes:")
for i, seg in enumerate(segments):
    if i < len(all_nodes):
        node = all_nodes[i]
        print(f"  Segment {i} '{seg}' → {node.get('role')} (id={node.get('id')})")
    else:
        print(f"  Segment {i} '{seg}' → /0 (fallback)")
