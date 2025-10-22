#!/usr/bin/env python3
import csv
import os
import requests
from pathlib import Path
from urllib.parse import urlparse
import time

def download_file(url, folder_path, state):
    """Download a file from URL to the specified folder."""
    try:
        # Get filename from URL
        parsed_url = urlparse(url)
        filename = os.path.basename(parsed_url.path)

        # If no filename, generate one
        if not filename or filename == '':
            filename = f"document_{hash(url)}"

        # Create full path
        file_path = os.path.join(folder_path, filename)

        # Skip if already exists
        if os.path.exists(file_path):
            print(f"  ✓ Already exists: {filename}")
            return True

        # Download the file
        print(f"  Downloading: {filename}")
        response = requests.get(url, timeout=30, allow_redirects=True)
        response.raise_for_status()

        # Save the file
        with open(file_path, 'wb') as f:
            f.write(response.content)

        print(f"  ✓ Downloaded: {filename}")
        return True

    except Exception as e:
        print(f"  ✗ Error downloading {url}: {str(e)}")
        return False

def main():
    # Read the CSV file
    csv_path = "StateSampleList.csv"

    if not os.path.exists(csv_path):
        print(f"Error: {csv_path} not found")
        return

    # Track statistics
    stats = {
        'total': 0,
        'downloaded': 0,
        'failed': 0,
        'skipped': 0
    }

    # Read CSV and download files
    with open(csv_path, 'r', encoding='utf-8') as csvfile:
        reader = csv.reader(csvfile)
        next(reader)  # Skip header row

        for row in reader:
            if len(row) < 7:
                continue

            state = row[0].strip()
            url = row[6].strip()

            # Skip if no URL
            if not url or url == '':
                continue

            stats['total'] += 1

            # Create state folder if it doesn't exist
            state_folder = Path(state)
            state_folder.mkdir(exist_ok=True)

            print(f"\n{state}:")

            # Download the file
            if download_file(url, state_folder, state):
                stats['downloaded'] += 1
            else:
                stats['failed'] += 1

            # Small delay to be respectful to servers
            time.sleep(0.5)

    # Print summary
    print("\n" + "="*50)
    print("Download Summary:")
    print(f"  Total documents: {stats['total']}")
    print(f"  Successfully downloaded: {stats['downloaded']}")
    print(f"  Failed: {stats['failed']}")
    print("="*50)

if __name__ == "__main__":
    main()
