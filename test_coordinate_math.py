#!/usr/bin/env python3
"""
Test coordinate transformations to debug misalignment
"""

def test_coordinate_transform():
    # Test with first field from API response
    field_name = "Provider's headquarter region 1"
    pdf_x = 303.67
    pdf_y = 370.8498
    width = 20
    height = 13.799
    page_width = 612
    page_height = 792

    print(f"Testing field: {field_name}")
    print(f"Page size: {page_width} x {page_height}")
    print("=" * 60)

    # PDF coordinates (bottom-left origin, Y increases upward)
    print("\n1. PDF COORDINATES (bottom-left origin):")
    print(f"   Position: ({pdf_x:.2f}, {pdf_y:.2f})")
    print(f"   Size: {width:.2f} x {height:.2f}")
    print(f"   Box: ({pdf_x:.2f}, {pdf_y:.2f}) to ({pdf_x + width:.2f}, {pdf_y + height:.2f})")

    # Display coordinates (top-left origin, Y increases downward)
    # To convert: display_y = page_height - (pdf_y + height)
    display_x = pdf_x
    display_y = page_height - (pdf_y + height)
    print("\n2. DISPLAY COORDINATES (top-left origin):")
    print(f"   Position: ({display_x:.2f}, {display_y:.2f})")
    print(f"   Formula: display_y = {page_height} - ({pdf_y:.2f} + {height:.2f}) = {display_y:.2f}")

    # CSS Percentage coordinates
    pct_x = (pdf_x / page_width) * 100
    pct_y = (display_y / page_height) * 100
    pct_width = (width / page_width) * 100
    pct_height = (height / page_height) * 100

    print("\n3. CSS PERCENTAGES (for HTML display):")
    print(f"   left: {pct_x:.2f}%")
    print(f"   top: {pct_y:.2f}%")
    print(f"   width: {pct_width:.2f}%")
    print(f"   height: {pct_height:.2f}%")

    # Check what the C# code should be calculating
    print("\n4. C# ToPercentageCoordinates(asTopLeftOrigin=true):")
    print(f"   float xPercent = ({pdf_x:.2f} / {page_width}) * 100 = {pct_x:.2f}%")
    print(f"   float yTopLeft = {page_height} - {pdf_y:.2f} - {height:.2f} = {display_y:.2f}")
    print(f"   float yPercent = ({display_y:.2f} / {page_height}) * 100 = {pct_y:.2f}%")

    # Position analysis
    print("\n5. POSITION ANALYSIS:")
    print(f"   From top of page: {display_y:.2f} points ({pct_y:.2f}%)")
    print(f"   From bottom of page: {pdf_y:.2f} points ({(pdf_y/page_height)*100:.2f}%)")
    print(f"   This field is in the {('upper' if display_y < page_height/2 else 'lower')} half of the page")

    # Expected position (middle of page approximately)
    print("\n6. REASONABLENESS CHECK:")
    if 300 < display_y < 500:
        print(f"   ✓ Display Y ({display_y:.2f}) is in middle range (300-500)")
    else:
        print(f"   ✗ Display Y ({display_y:.2f}) seems off - expected middle range (300-500)")

    if 40 < pct_y < 65:
        print(f"   ✓ Top percentage ({pct_y:.2f}%) is in middle range (40-65%)")
    else:
        print(f"   ✗ Top percentage ({pct_y:.2f}%) seems off - expected middle range (40-65%)")

def test_multiple_fields():
    """Test coordinate transformations for multiple fields"""
    print("\n" + "=" * 60)
    print("TESTING MULTIPLE FIELDS")
    print("=" * 60)

    # Sample fields from the API response
    fields = [
        {"name": "Region 1", "x": 303.67, "y": 370.8498, "w": 20, "h": 13.799},
        {"name": "Region 2", "x": 334.14, "y": 370.8498, "w": 20, "h": 13.799},
        {"name": "Region 3", "x": 364.61, "y": 370.8498, "w": 20, "h": 13.799},
        {"name": "Region 4", "x": 395.07, "y": 370.8498, "w": 20, "h": 13.799},
    ]

    page_width = 612
    page_height = 792

    print(f"\nAll fields have Y={fields[0]['y']:.2f} (PDF coordinates)")
    print("This means they're all on the same horizontal line")

    display_y = page_height - (fields[0]['y'] + fields[0]['h'])
    print(f"Display Y (top-left origin): {display_y:.2f}")
    print(f"That's {(display_y/page_height)*100:.1f}% from the top")

    print("\nX positions (left to right):")
    for field in fields:
        x_pct = (field['x'] / page_width) * 100
        print(f"  {field['name']}: {field['x']:.2f} ({x_pct:.1f}% from left)")

if __name__ == "__main__":
    test_coordinate_transform()
    test_multiple_fields()