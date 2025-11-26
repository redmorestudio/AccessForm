#!/usr/bin/env python3
import json

# Load test structure
with open('test_structure.json', 'r') as f:
    structure = json.load(f)

# Flatten nodes (same as orchestrator.py)
all_nodes = []
def flatten(node_list):
    for node in node_list:
        all_nodes.append(node)
        if 'children' in node:
            flatten(node['children'])

flatten(structure.get('nodes', []))

# Check each node for ID
print(f"Total flattened nodes: {len(all_nodes)}")
for i, node in enumerate(all_nodes):
    node_id = node.get('id')
    node_role = node.get('role', 'unknown')
    has_id = node_id is not None
    print(f"Node {i}: role={node_role}, id={node_id}, has_id={has_id}")
    
    if not has_id:
        print(f"  ⚠️  Node {i} missing ID!")

# Count nodes with/without IDs
with_ids = sum(1 for n in all_nodes if n.get('id') is not None)
without_ids = len(all_nodes) - with_ids
print(f"\nSummary: {with_ids} with IDs, {without_ids} without IDs")
