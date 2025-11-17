#!/usr/bin/env python3
"""
Archive PDFs for cities that had non-city domains collected
"""

import os
import shutil
from datetime import datetime
from pathlib import Path

# Cities with excluded domains that need to be redone (from filter report)
CITIES_TO_ARCHIVE = [
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

def archive_cities(base_dir, archive_dir):
    """Archive PDFs for affected cities"""

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    archive_root = f"{archive_dir}_archived_{timestamp}"

    archived_count = 0
    missing_count = 0
    archived_cities = []

    print(f"=" * 80)
    print(f"ARCHIVING AFFECTED CITIES")
    print(f"=" * 80)
    print(f"Archive location: {archive_root}\n")

    for state, city in CITIES_TO_ARCHIVE:
        source_dir = os.path.join(base_dir, "StateAssets", state, city)
        dest_dir = os.path.join(archive_root, state, city)

        if os.path.exists(source_dir):
            # Count PDFs before archiving
            pdf_count = len([f for f in os.listdir(source_dir) if f.endswith('.pdf')])

            print(f"📦 Archiving: {city}, {state} ({pdf_count} PDFs)")

            # Create destination directory
            os.makedirs(dest_dir, exist_ok=True)

            # Move the entire directory
            shutil.copytree(source_dir, dest_dir, dirs_exist_ok=True)

            archived_cities.append({
                'state': state,
                'city': city,
                'pdf_count': pdf_count,
                'source': source_dir,
                'archive': dest_dir
            })
            archived_count += 1
        else:
            print(f"⚠️  NOT FOUND: {city}, {state} (directory doesn't exist)")
            missing_count += 1

    print(f"\n" + "=" * 80)
    print(f"ARCHIVE SUMMARY")
    print(f"=" * 80)
    print(f"Archived: {archived_count} cities")
    print(f"Missing: {missing_count} cities")
    print(f"Archive location: {archive_root}\n")

    # Create manifest
    manifest_file = os.path.join(archive_root, "ARCHIVE_MANIFEST.txt")
    with open(manifest_file, 'w') as f:
        f.write("=" * 80 + "\n")
        f.write("ARCHIVE MANIFEST\n")
        f.write(f"Created: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")
        f.write(f"Reason: Re-collecting PDFs from city government domains only\n")
        f.write("=" * 80 + "\n\n")

        f.write(f"Archived Cities: {archived_count}\n")
        f.write(f"Missing Cities: {missing_count}\n\n")

        if archived_cities:
            f.write("=" * 80 + "\n")
            f.write("ARCHIVED CITIES\n")
            f.write("=" * 80 + "\n\n")

            for city_info in archived_cities:
                f.write(f"{city_info['city']}, {city_info['state']}\n")
                f.write(f"  PDFs: {city_info['pdf_count']}\n")
                f.write(f"  Source: {city_info['source']}\n")
                f.write(f"  Archive: {city_info['archive']}\n\n")

        f.write("\n" + "=" * 80 + "\n")
        f.write("NOTES\n")
        f.write("=" * 80 + "\n\n")
        f.write("These cities had PDFs collected from non-city domains (airports, transit,\n")
        f.write("courts, counties, schools, etc.) that will be replaced with PDFs from\n")
        f.write("official city government websites only.\n")

    print(f"✓ Manifest written to: {manifest_file}\n")

    return archived_count, missing_count, archive_root

if __name__ == "__main__":
    base_dir = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
    archive_dir = os.path.join(base_dir, "StateAssets")

    archived, missing, archive_root = archive_cities(base_dir, archive_dir)

    if archived > 0:
        print(f"✓ Successfully archived {archived} cities to:")
        print(f"  {archive_root}\n")

    if missing > 0:
        print(f"⚠️  {missing} cities were not found (may not have been processed yet)")
