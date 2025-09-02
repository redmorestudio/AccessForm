#!/usr/bin/env python3
from pypdf import PdfWriter, PdfReader
from pypdf.generic import NameObject, TextStringObject, DictionaryObject, ArrayObject, NumberObject
import tempfile

# Create a simple PDF with form fields that have type suffixes
writer = PdfWriter()

# Add a blank page
writer.add_blank_page(width=612, height=792)

# Create form fields with type suffixes (like Syncfusion does)
fields = [
    {"name": "Last Name[name]", "type": "/Tx"},
    {"name": "First Name[name]", "type": "/Tx"},
    {"name": "Email[email]", "type": "/Tx"},
    {"name": "Agree[checkbox]", "type": "/Btn"},
]

# Create AcroForm
acroform = DictionaryObject()
acroform[NameObject("/Fields")] = ArrayObject()

for field_info in fields:
    field = DictionaryObject()
    field[NameObject("/T")] = TextStringObject(field_info["name"])
    field[NameObject("/FT")] = NameObject(field_info["type"])
    field[NameObject("/Ff")] = NumberObject(0)
    acroform["/Fields"].append(writer._add_object(field))

writer._root_object[NameObject("/AcroForm")] = writer._add_object(acroform)

# Save the test PDF
output_path = "test_fields.pdf"
with open(output_path, "wb") as f:
    writer.write(f)

print(f"Created test PDF with fields: {output_path}")
print("Fields:")
for field in fields:
    print(f"  - {field['name']}")