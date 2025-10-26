import fitz
import sys

doc = fitz.open(sys.argv[1])
page = doc[2]  # Page 3

# Get page content and wrap ALL paths in artifacts
content = doc.xref_stream(page.xref).decode('latin-1', errors='ignore')
lines = content.split('\n')
new_lines = []

for line in lines:
    # If it's a path operation, wrap it
    if any(op in line for op in [' re', ' m ', ' l ', ' c ', ' f', ' S', ' B']):
        if '/Artifact' not in line and 'BMC' not in line:
            new_lines.append('/Artifact BMC')
            new_lines.append(line)
            new_lines.append('EMC')
        else:
            new_lines.append(line)
    else:
        new_lines.append(line)

doc.update_stream(page.xref, '\n'.join(new_lines).encode('latin-1'))
doc.save(sys.argv[2])
print(f"Saved to {sys.argv[2]}")
