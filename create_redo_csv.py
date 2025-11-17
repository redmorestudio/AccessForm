#!/usr/bin/env python3
"""
Create CSV with only the 18 cities that need to be redone
"""

import csv

# Cities that need to be redone (from archive)
CITIES_TO_REDO = [
    ("California", "Bakersfield"),
    ("California", "Burbank"),
    ("California", "Elk Grove"),
    ("California", "Fontana"),
    ("California", "Fremont"),
    ("California", "Modesto"),
    ("California", "Moreno Valley"),
    ("California", "Oceanside"),
    ("California", "Pomona"),
    ("California", "Redding"),
    ("California", "Riverside"),
    ("California", "San Bernardino"),
    ("California", "San Mateo"),
    ("California", "Santa Ana"),
    ("California", "Ventura"),
    ("Colorado", "Aurora"),
    ("Nevada", "Reno"),
    ("Pennsylvania", "Erie"),
]

def create_redo_csv(filtered_csv, output_csv):
    """Extract only the cities that need to be redone"""

    # Read filtered CSV
    rows_to_redo = []

    with open(filtered_csv, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            state = row.get('State', '').strip()
            city = row.get('City', '').strip()

            if (state, city) in CITIES_TO_REDO:
                rows_to_redo.append(row)

    # Write redo CSV
    if rows_to_redo:
        with open(output_csv, 'w', encoding='utf-8', newline='') as f:
            fieldnames = rows_to_redo[0].keys()
            writer = csv.DictWriter(f, fieldnames=fieldnames)
            writer.writeheader()
            writer.writerows(rows_to_redo)

        print(f"✓ Created CSV with {len(rows_to_redo)} cities to redo")
        print(f"✓ Output: {output_csv}\n")

        print("Cities to be re-processed:")
        print("-" * 80)
        for row in rows_to_redo:
            site = row.get('city site', '')
            domain = site.split('/')[2] if '://' in site else site.split('/')[0]
            print(f"  {row['City']}, {row['State']}: {domain}")

    else:
        print("⚠️  No cities found to redo")

if __name__ == "__main__":
    filtered_csv = "StateAssets/city_domains_only.csv"
    output_csv = "StateAssets/cities_to_redo.csv"

    create_redo_csv(filtered_csv, output_csv)
