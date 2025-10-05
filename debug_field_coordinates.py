#!/usr/bin/env python3
import fitz  # PyMuPDF
import requests
import json
import sys
from pathlib import Path
import matplotlib.pyplot as plt
import matplotlib.patches as patches
from PIL import Image
import io
import numpy as np

def test_field_detection(pdf_path):
    """Test field detection and visualize coordinate system"""

    print(f"Testing with PDF: {pdf_path}")
    print("=" * 80)

    # Open PDF with PyMuPDF to get ground truth
    doc = fitz.open(pdf_path)
    page = doc[0]

    print(f"PDF Page Info:")
    print(f"  Page size: {page.rect.width} x {page.rect.height} points")
    print(f"  MediaBox: {page.mediabox}")
    print(f"  CropBox: {page.cropbox}")
    print()

    # Get fields using server detection
    print("Calling field detection API...")
    with open(pdf_path, 'rb') as f:
        files = {'file': ('test.pdf', f, 'application/pdf')}
        response = requests.post('http://localhost:5010/api/fields/detect', files=files)

    if response.status_code != 200:
        print(f"ERROR: API returned {response.status_code}")
        print(response.text)
        return

    fields = response.json()
    print(f"Detected {len(fields)} fields")
    print()

    # Print coordinate details for first few fields
    print("Field Coordinate Analysis:")
    print("-" * 80)
    for i, field in enumerate(fields[:5]):
        print(f"Field {i+1}: {field.get('fieldName', 'Unknown')}")
        print(f"  Type: {field.get('fieldType', 'Unknown')}")
        print(f"  Source: {field.get('source', 'Unknown')}")

        # Check for new coordinate system
        if 'coordinates' in field and field['coordinates']:
            coords = field['coordinates']
            print(f"  Universal Coordinates:")
            print(f"    PDF coords: ({coords.get('pdfX', 0):.2f}, {coords.get('pdfY', 0):.2f})")
            print(f"    Size: {coords.get('pdfWidth', 0):.2f} x {coords.get('pdfHeight', 0):.2f}")
            print(f"    Page: {coords.get('pageNumber', 1)}")
            print(f"    Source system: {coords.get('sourceSystem', 'Unknown')}")
        else:
            # Legacy coordinates
            print(f"  Legacy coords: ({field.get('x', 0):.2f}, {field.get('y', 0):.2f})")
            print(f"  Size: {field.get('width', 0):.2f} x {field.get('height', 0):.2f}")
            print(f"  Page: {field.get('pageNumber', 1)}")
        print()

    # Visualize the fields
    print("Creating visualization...")
    visualize_fields(doc, fields, pdf_path)

    # Test coordinate transformation
    print("\nTesting coordinate transformations:")
    print("-" * 80)
    test_coordinate_transforms(page.rect.width, page.rect.height)

    doc.close()

def visualize_fields(doc, fields, pdf_path):
    """Create visual representation of field positions"""

    page = doc[0]

    # Render page as image
    mat = fitz.Matrix(2, 2)  # 2x zoom for better quality
    pix = page.get_pixmap(matrix=mat)
    img_data = pix.tobytes("png")
    img = Image.open(io.BytesIO(img_data))

    # Create matplotlib figure
    fig, axes = plt.subplots(1, 2, figsize=(20, 12))

    # Left: Original PDF page
    axes[0].imshow(img)
    axes[0].set_title("PDF Page (for reference)")
    axes[0].axis('off')

    # Right: Fields overlaid
    axes[1].imshow(img)
    axes[1].set_title("Detected Fields Overlay")

    # Calculate scale factor (image pixels vs PDF points)
    img_width, img_height = img.size
    pdf_width = page.rect.width
    pdf_height = page.rect.height
    scale_x = img_width / pdf_width
    scale_y = img_height / pdf_height

    print(f"\nVisualization scaling:")
    print(f"  PDF dimensions: {pdf_width:.2f} x {pdf_height:.2f} points")
    print(f"  Image dimensions: {img_width} x {img_height} pixels")
    print(f"  Scale factor: {scale_x:.3f} x {scale_y:.3f}")

    # Draw fields
    colors = plt.cm.tab10(np.linspace(0, 1, 10))
    for i, field in enumerate(fields):
        color = colors[i % 10]

        # Get coordinates
        if 'coordinates' in field and field['coordinates']:
            coords = field['coordinates']
            x = coords.get('pdfX', 0)
            y = coords.get('pdfY', 0)
            width = coords.get('pdfWidth', 0)
            height = coords.get('pdfHeight', 0)

            # Convert PDF coordinates to image coordinates
            # PDF uses bottom-left origin, image uses top-left
            img_x = x * scale_x
            img_y = (pdf_height - (y + height)) * scale_y  # Flip Y and adjust for height
            img_width = width * scale_x
            img_height = height * scale_y

            coord_type = "Universal"
        else:
            # Legacy coordinates
            x = field.get('x', 0)
            y = field.get('y', 0)
            width = field.get('width', 0)
            height = field.get('height', 0)

            # Assume these are PDF coordinates
            img_x = x * scale_x
            img_y = (pdf_height - (y + height)) * scale_y
            img_width = width * scale_x
            img_height = height * scale_y

            coord_type = "Legacy"

        # Draw rectangle
        rect = patches.Rectangle((img_x, img_y), img_width, img_height,
                                linewidth=2, edgecolor=color, facecolor='none',
                                alpha=0.7)
        axes[1].add_patch(rect)

        # Add label
        field_name = field.get('fieldName', f'Field {i+1}')
        axes[1].text(img_x, img_y - 5, f"{field_name} ({coord_type})",
                    fontsize=8, color=color, weight='bold')

    axes[1].set_xlim(0, img_width)
    axes[1].set_ylim(img_height, 0)  # Invert Y axis for image coordinates

    # Save visualization
    output_path = pdf_path.replace('.pdf', '_field_visualization.png')
    plt.savefig(output_path, dpi=150, bbox_inches='tight')
    print(f"\nVisualization saved to: {output_path}")
    plt.show()

def test_coordinate_transforms(page_width, page_height):
    """Test specific coordinate transformations"""

    test_cases = [
        {"name": "Top-left corner", "pdf_x": 0, "pdf_y": page_height, "width": 100, "height": 30},
        {"name": "Bottom-left corner", "pdf_x": 0, "pdf_y": 30, "width": 100, "height": 30},
        {"name": "Center", "pdf_x": page_width/2 - 50, "pdf_y": page_height/2 + 15, "width": 100, "height": 30},
        {"name": "Top-right corner", "pdf_x": page_width - 100, "pdf_y": page_height, "width": 100, "height": 30},
    ]

    for test in test_cases:
        print(f"{test['name']}:")
        print(f"  PDF coords (bottom-left origin): ({test['pdf_x']:.2f}, {test['pdf_y']:.2f})")

        # Convert to display coordinates (top-left origin)
        display_x = test['pdf_x']
        display_y = page_height - test['pdf_y']
        print(f"  Display coords (top-left origin): ({display_x:.2f}, {display_y:.2f})")

        # Convert to percentages
        pct_x = (test['pdf_x'] / page_width) * 100
        pct_y = ((page_height - test['pdf_y']) / page_height) * 100
        print(f"  Percentage (for CSS): {pct_x:.2f}%, {pct_y:.2f}%")
        print()

def main():
    if len(sys.argv) < 2:
        # Look for PDFs in the current directory
        pdfs = list(Path('.').glob('*.pdf'))
        if pdfs:
            pdf_path = str(pdfs[0])
            print(f"No PDF specified, using: {pdf_path}")
        else:
            print("Usage: python debug_field_coordinates.py <pdf_file>")
            sys.exit(1)
    else:
        pdf_path = sys.argv[1]

    if not Path(pdf_path).exists():
        print(f"Error: PDF file not found: {pdf_path}")
        sys.exit(1)

    test_field_detection(pdf_path)

if __name__ == "__main__":
    main()