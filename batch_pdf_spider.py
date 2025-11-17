#!/usr/bin/env python3
"""
Batch PDF Spider - Processes multiple cities from a CSV file
Tracks progress and can resume from where it left off
"""

import csv
import json
import time
from datetime import datetime
from pathlib import Path
import argparse
from urllib.parse import urlparse
from pdf_spider import PDFSpider, API_KEY, CSE_ID

class BatchPDFSpider:
    def __init__(self, csv_file: str, base_dir: str, progress_file: str = "batch_progress.json"):
        self.csv_file = csv_file
        self.base_dir = base_dir
        self.progress_file = progress_file
        self.spider = PDFSpider(API_KEY, CSE_ID)
        self.progress = self._load_progress()

    def _load_progress(self):
        """Load progress from file if it exists"""
        progress_path = Path(self.progress_file)
        if progress_path.exists():
            with open(progress_path, 'r') as f:
                return json.load(f)
        return {
            "completed": [],
            "failed": [],
            "skipped": [],
            "last_processed_index": -1,
            "started_at": None,
            "last_update": None
        }

    def _save_progress(self):
        """Save current progress to file"""
        self.progress["last_update"] = datetime.now().isoformat()
        with open(self.progress_file, 'w') as f:
            json.dump(self.progress, f, indent=2)

    def _extract_domain(self, url: str) -> str:
        """Extract clean domain from URL"""
        if not url:
            return ""

        # Remove protocol if present
        if '://' in url:
            url = url.split('://', 1)[1]

        # Remove path
        domain = url.split('/')[0]

        # Remove www. prefix
        if domain.startswith('www.'):
            domain = domain[4:]

        return domain

    def _read_cities(self):
        """Read cities from CSV file and deduplicate by (state, city, site domain)"""
        seen = set()
        cities = []

        with open(self.csv_file, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                state = row.get('State', '').strip()
                city = row.get('City', '').strip()
                site = row.get('city site', '').strip()

                if state and city:
                    # Extract clean domain from site URL
                    site_domain = self._extract_domain(site)

                    # Create unique key for deduplication
                    unique_key = (state, city, site_domain)

                    # Only add if we haven't seen this combination before
                    if unique_key not in seen:
                        seen.add(unique_key)
                        cities.append({
                            'state': state,
                            'city': city,
                            'site': site_domain,
                            'original_site': site
                        })

        return cities

    def process_all(
        self,
        max_results: int = 1000,
        max_age_years: int = 5,
        search_terms: str = "",
        download_pdfs: bool = False,
        start_from: int = None
    ):
        """
        Process all cities in the CSV file

        Args:
            max_results: Max PDFs per city
            max_age_years: Max age filter
            search_terms: Search terms (e.g., "form -tax")
            download_pdfs: Whether to download PDFs
            start_from: Index to start from (overrides progress file)
        """
        cities = self._read_cities()

        # Determine starting point
        if start_from is not None:
            start_idx = start_from
        else:
            start_idx = self.progress["last_processed_index"] + 1

        if self.progress["started_at"] is None:
            self.progress["started_at"] = datetime.now().isoformat()

        total_cities = len(cities)

        print("=" * 80)
        print("🕷️  BATCH PDF SPIDER")
        print("=" * 80)
        print(f"📊 Total cities: {total_cities}")
        print(f"✅ Already completed: {len(self.progress['completed'])}")
        print(f"❌ Previously failed: {len(self.progress['failed'])}")
        print(f"⏭️  Skipped (no site): {len(self.progress['skipped'])}")
        print(f"🎯 Starting from index: {start_idx}")
        print(f"📁 Base directory: {self.base_dir}")
        if search_terms:
            print(f"🔎 Search terms: {search_terms}")
        print("=" * 80)

        for idx in range(start_idx, total_cities):
            city_info = cities[idx]
            state = city_info['state']
            city = city_info['city']
            site = city_info['site']

            city_key = f"{state}|{city}"

            print(f"\n[{idx + 1}/{total_cities}] Processing: {city}, {state}")
            print(f"Site: {site if site else 'NO SITE - will search broadly'}")
            print("-" * 80)

            try:
                # Skip if no site and user wants site-only
                if not site:
                    print(f"⏭️  Skipping {city}, {state} - no website available")
                    self.progress["skipped"].append(city_key)
                    self.progress["last_processed_index"] = idx
                    self._save_progress()
                    continue

                # Run spider for this city
                results = self.spider.search_pdfs(
                    city=city,
                    state=state,
                    site_domain=site if site else None,
                    max_results=max_results,
                    max_age_years=max_age_years,
                    search_terms=search_terms,
                    download_pdfs=download_pdfs,
                    base_dir=self.base_dir
                )

                # Save metadata
                output_dir = Path(self.base_dir) / "StateAssets" / state / city
                self.spider.save_metadata(results, str(output_dir), "pdf_results.json")

                print(f"✅ Success: {len(results)} PDFs found")
                self.progress["completed"].append({
                    "key": city_key,
                    "city": city,
                    "state": state,
                    "site": site,
                    "count": len(results),
                    "timestamp": datetime.now().isoformat()
                })

                self.progress["last_processed_index"] = idx
                self._save_progress()

                # Rate limiting between cities
                if idx < total_cities - 1:
                    print("⏳ Waiting 5 seconds before next city...")
                    time.sleep(5)

            except KeyboardInterrupt:
                print("\n\n⚠️  Interrupted by user. Progress saved.")
                print(f"Resume with: --start-from {idx}")
                self._save_progress()
                return

            except Exception as e:
                print(f"❌ Error processing {city}, {state}: {e}")
                self.progress["failed"].append({
                    "key": city_key,
                    "city": city,
                    "state": state,
                    "site": site,
                    "error": str(e),
                    "timestamp": datetime.now().isoformat()
                })
                self.progress["last_processed_index"] = idx
                self._save_progress()

                # Continue to next city
                continue

        # Final summary
        print("\n" + "=" * 80)
        print("🎉 BATCH PROCESSING COMPLETE!")
        print("=" * 80)
        print(f"✅ Successfully processed: {len(self.progress['completed'])} cities")
        print(f"❌ Failed: {len(self.progress['failed'])} cities")
        print(f"⏭️  Skipped: {len(self.progress['skipped'])} cities")

        if self.progress['failed']:
            print("\n❌ Failed cities:")
            for failure in self.progress['failed']:
                print(f"  • {failure['city']}, {failure['state']}: {failure['error']}")

        print(f"\n📁 Progress file: {self.progress_file}")
        self._save_progress()

    def retry_failed(self, max_results: int = 1000, max_age_years: int = 5,
                     search_terms: str = "", download_pdfs: bool = False):
        """Retry all previously failed cities"""
        if not self.progress['failed']:
            print("No failed cities to retry!")
            return

        print(f"Retrying {len(self.progress['failed'])} failed cities...")

        failed_list = self.progress['failed'].copy()
        self.progress['failed'] = []

        for failure in failed_list:
            city = failure['city']
            state = failure['state']
            site = failure['site']
            city_key = failure['key']

            print(f"\n🔄 Retrying: {city}, {state}")
            print("-" * 80)

            try:
                results = self.spider.search_pdfs(
                    city=city,
                    state=state,
                    site_domain=site if site else None,
                    max_results=max_results,
                    max_age_years=max_age_years,
                    search_terms=search_terms,
                    download_pdfs=download_pdfs,
                    base_dir=self.base_dir
                )

                output_dir = Path(self.base_dir) / "StateAssets" / state / city
                self.spider.save_metadata(results, str(output_dir), "pdf_results.json")

                print(f"✅ Success: {len(results)} PDFs found")
                self.progress["completed"].append({
                    "key": city_key,
                    "city": city,
                    "state": state,
                    "site": site,
                    "count": len(results),
                    "timestamp": datetime.now().isoformat(),
                    "retry": True
                })

                self._save_progress()
                time.sleep(5)

            except Exception as e:
                print(f"❌ Still failing: {e}")
                self.progress['failed'].append(failure)
                self._save_progress()


def main():
    parser = argparse.ArgumentParser(
        description='Batch PDF Spider - Process multiple cities from CSV',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Process all cities from CSV
  python batch_pdf_spider.py --csv "StateAssets/ada_contacts - Full List - 1027.csv"

  # Search for forms, excluding tax
  python batch_pdf_spider.py --csv cities.csv --terms "form -tax" --download

  # Resume from index 150
  python batch_pdf_spider.py --csv cities.csv --start-from 150

  # Retry all failed cities
  python batch_pdf_spider.py --csv cities.csv --retry-failed

  # Custom output directory
  python batch_pdf_spider.py --csv cities.csv --base-dir "/path/to/output"
        """
    )

    parser.add_argument('--csv', required=True,
                        help='CSV file with city list (State, City, city site columns)')
    parser.add_argument('--base-dir', default=None,
                        help='Base directory (default: current directory)')
    parser.add_argument('--max-results', type=int, default=1000,
                        help='Max PDFs per city (default: 1000)')
    parser.add_argument('--max-age', type=int, default=5,
                        help='Max age in years (default: 5)')
    parser.add_argument('--terms', default='',
                        help='Search terms (e.g., "form -tax")')
    parser.add_argument('--download', action='store_true',
                        help='Download PDFs (not just metadata)')
    parser.add_argument('--start-from', type=int, default=None,
                        help='Start from specific index (overrides progress file)')
    parser.add_argument('--progress-file', default='batch_progress.json',
                        help='Progress tracking file (default: batch_progress.json)')
    parser.add_argument('--retry-failed', action='store_true',
                        help='Retry all previously failed cities')

    args = parser.parse_args()

    # Use current directory as base if not specified
    base_dir = args.base_dir or str(Path(__file__).parent)

    batch_spider = BatchPDFSpider(args.csv, base_dir, args.progress_file)

    if args.retry_failed:
        batch_spider.retry_failed(
            max_results=args.max_results,
            max_age_years=args.max_age,
            search_terms=args.terms,
            download_pdfs=args.download
        )
    else:
        batch_spider.process_all(
            max_results=args.max_results,
            max_age_years=args.max_age,
            search_terms=args.terms,
            download_pdfs=args.download,
            start_from=args.start_from
        )


if __name__ == "__main__":
    main()
