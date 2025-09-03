#!/usr/bin/env python3
"""
Test the rebuild with actual field update data from the log.
"""
import json
import sys
import os
sys.path.insert(0, os.path.dirname(__file__))

from pdf_complete_rebuild import PDFCompleteRebuilder

# Actual field updates from the log
field_updates = [
    {
        "originalName": "Last Name",
        "newName": "Last Name",
        "fieldType": "text",
        "X": 100,
        "Y": 100,
        "Width": 200,
        "Height": 20
    },
    {
        "originalName": "In person, hand-delivered",
        "newName": "delivery date",
        "fieldType": "text",
        "X": 100,
        "Y": 300,
        "Width": 150,
        "Height": 20
    }
]

# Test with the actual test-upload.pdf
pdf_path = "test-upload.pdf"
if not os.path.exists(pdf_path):
    print(f"PDF not found: {pdf_path}")
    sys.exit(1)

print("Testing rebuild with actual field updates...")
rebuilder = PDFCompleteRebuilder()

try:
    result = rebuilder.rebuild_pdf(pdf_path, field_updates)
    print(json.dumps(result, indent=2))
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()