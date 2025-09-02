#!/usr/bin/env python3
"""
Test if rebuilt PDFs can be properly rendered as images.
"""
import sys
import os
import tempfile
import fitz  # PyMuPDF

def test_pdf_to_image(pdf_path):
    """Test converting PDF to image"""
    try:
        doc = fitz.open(pdf_path)
        page = doc[0]  # First page
        
        # Get a pixmap (image) of the page
        mat = fitz.Matrix(2, 2)  # 2x zoom for better quality
        pixmap = page.get_pixmap(matrix=mat, alpha=False)
        
        # Save as PNG
        output_path = tempfile.mktemp(suffix='.png')
        pixmap.save(output_path)
        
        print(f"✓ PDF successfully converted to image: {output_path}")
        print(f"  Image size: {pixmap.width}x{pixmap.height}")
        print(f"  File size: {os.path.getsize(output_path)} bytes")
        
        # Check if the image has actual content (not just white)
        # Get the pixel data to verify it's not blank
        samples = pixmap.samples
        if samples:
            # Check if all pixels are white (255,255,255)
            # Sample more bytes to be sure
            sample_size = min(10000, len(samples))
            white_count = sum(1 for b in samples[:sample_size] if b == 255)
            white_percentage = (white_count / sample_size) * 100
            
            if white_percentage > 99:
                print(f"⚠️  Warning: Image appears to be mostly blank ({white_percentage:.1f}% white)")
            else:
                print(f"✓ Image contains visible content ({100-white_percentage:.1f}% non-white)")
        
        doc.close()
        return True
        
    except Exception as e:
        print(f"✗ Failed to convert PDF to image: {e}")
        return False

if __name__ == "__main__":
    if len(sys.argv) < 2:
        # Use the test rebuilt PDF if it exists
        test_files = [
            "/var/folders/57/lz4fpmf917n6x5yz7mysln4m0000gn/T/tmpui5_yb_7_rebuilt.pdf",
            "test_fields.pdf"
        ]
        
        pdf_path = None
        for test_file in test_files:
            if os.path.exists(test_file):
                pdf_path = test_file
                break
        
        if not pdf_path:
            print("Usage: test_preview.py <pdf_path>")
            print("No test PDF found")
            sys.exit(1)
    else:
        pdf_path = sys.argv[1]
    
    if not os.path.exists(pdf_path):
        print(f"PDF file not found: {pdf_path}")
        sys.exit(1)
    
    print(f"Testing PDF preview generation for: {pdf_path}")
    success = test_pdf_to_image(pdf_path)
    sys.exit(0 if success else 1)