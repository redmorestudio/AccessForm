#!/usr/bin/env python3
"""
Smart PDF Spider - Intelligent PDF collection with form prioritization
"""

import requests
import time
import json
from datetime import datetime
from typing import List, Dict, Optional, Set
import os

API_KEY = "AIzaSyBgbSqCLu4XQ6P7et2u0ftKYOur89fjl24"
CSE_ID = "e6e56a5735e5748a6"
BASE_URL = "https://www.googleapis.com/customsearch/v1"

class SmartPDFSpider:
    def __init__(self, api_key: str, cse_id: str):
        self.api_key = api_key
        self.cse_id = cse_id
        self.session = requests.Session()

    def search_smart(
        self,
        city: str,
        state: str,
        site_domain: str,
        base_dir: str,
        max_results: int = 1000
    ) -> List[Dict]:
        """
        Smart search strategy:
        1. Get forms (excluding tax) from last 2 years
        2. Get all PDFs from last 2 years
        3. If < 100 total, expand forms to 5 years
        4. Cap at max_results
        """

        output_dir = os.path.join(base_dir, "StateAssets", state, city)
        os.makedirs(output_dir, exist_ok=True)

        all_pdfs = []
        seen_urls: Set[str] = set()

        print(f"🕷️  Smart PDF Spider started")
        print(f"📍 City: {city}, {state}")
        print(f"🌐 Site: {site_domain}")
        print(f"📊 Target: Up to {max_results} PDFs")
        print(f"📁 Output: {output_dir}")
        print("=" * 80)

        # Phase 1: Forms (excluding tax) - Last 5 years
        print("\n🔍 Phase 1: Forms (excluding tax) - Last 5 years")
        print("-" * 80)
        forms_5yr = self._search_query(
            f"site:{site_domain} form -tax filetype:pdf",
            max_results=max_results,
            date_restrict="y5",
            seen_urls=seen_urls
        )
        all_pdfs.extend(forms_5yr)
        print(f"✓ Found {len(forms_5yr)} forms")

        # Phase 2: All PDFs - Last 2 years
        print("\n🔍 Phase 2: All PDFs - Last 2 years")
        print("-" * 80)
        all_2yr = self._search_query(
            f"site:{site_domain} filetype:pdf",
            max_results=max_results - len(all_pdfs),
            date_restrict="y2",
            seen_urls=seen_urls
        )
        all_pdfs.extend(all_2yr)
        print(f"✓ Found {len(all_2yr)} additional PDFs")
        print(f"📊 Total so far: {len(all_pdfs)}")

        # Phase 3: If < 100, expand all PDFs to 5 years
        if len(all_pdfs) < 100:
            print("\n🔍 Phase 3: Expanding all PDFs to 5 years (< 100 total)")
            print("-" * 80)
            all_5yr = self._search_query(
                f"site:{site_domain} filetype:pdf",
                max_results=max_results - len(all_pdfs),
                date_restrict="y5",
                seen_urls=seen_urls
            )
            all_pdfs.extend(all_5yr)
            print(f"✓ Found {len(all_5yr)} additional PDFs")
            print(f"📊 Total: {len(all_pdfs)}")

        print("\n" + "=" * 80)
        print(f"✅ Completed! Found {len(all_pdfs)} unique PDFs")

        return all_pdfs

    def _search_query(
        self,
        query: str,
        max_results: int,
        date_restrict: str,
        seen_urls: Set[str]
    ) -> List[Dict]:
        """Execute a single search query with pagination"""
        results = []
        start_index = 1

        while start_index <= 91 and len(results) < max_results:
            try:
                params = {
                    'key': self.api_key,
                    'cx': self.cse_id,
                    'q': query,
                    'start': start_index,
                    'num': 10,
                    'dateRestrict': date_restrict,
                }

                print(f"  📄 Results {start_index}-{start_index+9}...", end=" ", flush=True)

                response = self.session.get(BASE_URL, params=params)
                response.raise_for_status()
                data = response.json()

                if 'items' not in data:
                    print("No more results")
                    break

                items = data['items']
                new_count = 0

                for item in items:
                    url = item.get('link')

                    if url in seen_urls:
                        continue

                    seen_urls.add(url)

                    pdf_info = {
                        'title': item.get('title', 'Untitled'),
                        'url': url,
                        'snippet': item.get('snippet', ''),
                        'displayLink': item.get('displayLink', ''),
                        'retrieved_at': datetime.now().isoformat(),
                        'query': query,
                        'date_filter': date_restrict
                    }

                    results.append(pdf_info)
                    new_count += 1

                    if len(results) >= max_results:
                        break

                print(f"✓ {new_count} new")

                if len(items) < 10:
                    break

                start_index += 10
                time.sleep(0.5)

            except requests.exceptions.HTTPError as e:
                if e.response.status_code == 429:
                    print(f"⚠️  Rate limit, waiting 60s...")
                    time.sleep(60)
                    continue
                else:
                    print(f"❌ Error: {e}")
                    break
            except Exception as e:
                print(f"❌ Error: {e}")
                break

        return results

    def download_pdfs(self, pdfs: List[Dict], output_dir: str):
        """Download actual PDF files"""
        print(f"\n📥 Downloading {len(pdfs)} PDFs to {output_dir}...")
        print("-" * 80)

        for idx, pdf in enumerate(pdfs, 1):
            try:
                url = pdf['url']
                filename = f"{idx:04d}_{pdf['title'][:50]}.pdf"
                filename = "".join(c for c in filename if c.isalnum() or c in (' ', '-', '_', '.')).rstrip()
                filepath = os.path.join(output_dir, filename)

                print(f"  [{idx}/{len(pdfs)}] {filename[:60]}...", end=" ", flush=True)

                response = self.session.get(url, timeout=30)
                response.raise_for_status()

                with open(filepath, 'wb') as f:
                    f.write(response.content)

                print(f"✓ ({len(response.content) / 1024:.1f} KB)")
                time.sleep(0.5)

            except Exception as e:
                print(f"❌ {e}")

    def save_metadata(self, pdfs: List[Dict], output_dir: str, filename: str = "pdf_results.json"):
        """Save PDF metadata to JSON"""
        filepath = os.path.join(output_dir, filename)
        with open(filepath, 'w', encoding='utf-8') as f:
            json.dump(pdfs, f, indent=2, ensure_ascii=False)
        print(f"\n💾 Metadata saved to {filepath}")


# Batch processor using smart spider
class SmartBatchProcessor:
    def __init__(self, csv_file: str, base_dir: str, progress_file: str = "smart_progress.json"):
        import csv
        from pathlib import Path

        self.csv_file = csv_file
        self.base_dir = base_dir
        self.progress_file = progress_file
        self.spider = SmartPDFSpider(API_KEY, CSE_ID)
        self.progress = self._load_progress()

        # Initialize logging
        self.log_file = "spider_log.txt"
        self.summary_file = "spider_summary.txt"
        self._init_logs()

    def _load_progress(self):
        """Load progress from file"""
        from pathlib import Path
        import json

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
        """Save progress"""
        self.progress["last_update"] = datetime.now().isoformat()
        with open(self.progress_file, 'w') as f:
            json.dump(self.progress, f, indent=2)

    def _init_logs(self):
        """Initialize log files"""
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        with open(self.log_file, 'a') as f:
            f.write(f"\n{'='*80}\n")
            f.write(f"Spider started at {timestamp}\n")
            f.write(f"{'='*80}\n")

    def _log(self, message: str):
        """Write to log file"""
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        with open(self.log_file, 'a') as f:
            f.write(f"[{timestamp}] {message}\n")

    def _update_summary(self):
        """Update summary file with current progress"""
        with open(self.summary_file, 'w') as f:
            f.write(f"PDF Spider Summary\n")
            f.write(f"Updated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")
            f.write(f"{'='*80}\n\n")

            f.write(f"Progress:\n")
            f.write(f"  Completed: {len(self.progress['completed'])}\n")
            f.write(f"  Failed: {len(self.progress['failed'])}\n")
            f.write(f"  Skipped: {len(self.progress['skipped'])}\n")
            f.write(f"  Last Index: {self.progress['last_processed_index']}\n\n")

            if self.progress['completed']:
                f.write(f"Completed Cities:\n")
                f.write(f"{'-'*80}\n")
                total_pdfs = 0
                for item in self.progress['completed']:
                    count = item.get('count', 0)
                    total_pdfs += count
                    f.write(f"  {item['city']}, {item['state']}: {count} PDFs\n")
                f.write(f"{'-'*80}\n")
                f.write(f"Total PDFs collected: {total_pdfs}\n\n")

            if self.progress['failed']:
                f.write(f"\nFailed Cities:\n")
                f.write(f"{'-'*80}\n")
                for item in self.progress['failed']:
                    f.write(f"  {item['city']}, {item['state']}: {item.get('error', 'Unknown error')}\n")

    def _extract_domain(self, url: str) -> str:
        """Extract clean domain from URL"""
        if not url:
            return ""
        if '://' in url:
            url = url.split('://', 1)[1]
        domain = url.split('/')[0]
        if domain.startswith('www.'):
            domain = domain[4:]
        return domain

    def _read_cities(self):
        """Read cities from CSV"""
        import csv

        cities = []
        with open(self.csv_file, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                state = row.get('State', '').strip()
                city = row.get('City', '').strip()
                site = row.get('city site', '').strip()

                if state and city:
                    site_domain = self._extract_domain(site)
                    cities.append({
                        'state': state,
                        'city': city,
                        'site': site_domain,
                    })
        return cities

    def process_all(self, max_results: int = 1000, download_pdfs: bool = False, start_from: int = None):
        """Process all cities"""
        cities = self._read_cities()

        if start_from is not None:
            start_idx = start_from
        else:
            start_idx = self.progress["last_processed_index"] + 1

        if self.progress["started_at"] is None:
            self.progress["started_at"] = datetime.now().isoformat()

        total_cities = len(cities)

        print("=" * 80)
        print("🕷️  SMART BATCH PDF SPIDER")
        print("=" * 80)
        print(f"📊 Total cities: {total_cities}")
        print(f"✅ Completed: {len(self.progress['completed'])}")
        print(f"❌ Failed: {len(self.progress['failed'])}")
        print(f"⏭️  Skipped: {len(self.progress['skipped'])}")
        print(f"🎯 Starting from: {start_idx}")
        print(f"📁 Base dir: {self.base_dir}")
        print("=" * 80)

        for idx in range(start_idx, total_cities):
            city_info = cities[idx]
            state = city_info['state']
            city = city_info['city']
            site = city_info['site']
            city_key = f"{state}|{city}"

            print(f"\n\n{'='*80}")
            print(f"[{idx + 1}/{total_cities}] {city}, {state}")
            print(f"{'='*80}")

            try:
                if not site:
                    print(f"⏭️  Skipping - no website")
                    self._log(f"SKIPPED: {city}, {state} - no website")
                    self.progress["skipped"].append(city_key)
                    self.progress["last_processed_index"] = idx
                    self._save_progress()
                    self._update_summary()
                    continue

                # Run smart spider
                results = self.spider.search_smart(
                    city=city,
                    state=state,
                    site_domain=site,
                    base_dir=self.base_dir,
                    max_results=max_results
                )

                # Save metadata
                output_dir = os.path.join(self.base_dir, "StateAssets", state, city)
                self.spider.save_metadata(results, output_dir)

                # Download if requested
                if download_pdfs and results:
                    self.spider.download_pdfs(results, output_dir)

                print(f"\n✅ Success: {len(results)} PDFs")
                self._log(f"SUCCESS: {city}, {state} - {len(results)} PDFs collected from {site}")
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
                self._update_summary()

                if idx < total_cities - 1:
                    print("\n⏳ Waiting 5 seconds...")
                    time.sleep(5)

            except KeyboardInterrupt:
                print(f"\n\n⚠️  Interrupted. Resume with: --start-from {idx}")
                self._save_progress()
                return

            except Exception as e:
                print(f"\n❌ Error: {e}")
                self._log(f"FAILED: {city}, {state} - {str(e)}")
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
                self._update_summary()
                continue

        print("\n\n" + "=" * 80)
        print("🎉 BATCH COMPLETE!")
        print("=" * 80)
        print(f"✅ Completed: {len(self.progress['completed'])}")
        print(f"❌ Failed: {len(self.progress['failed'])}")
        print(f"⏭️  Skipped: {len(self.progress['skipped'])}")


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description='Smart PDF Spider with form prioritization')
    parser.add_argument('--csv', required=True, help='CSV file with cities')
    parser.add_argument('--base-dir', default=None, help='Base directory')
    parser.add_argument('--max-results', type=int, default=1000, help='Max PDFs per city')
    parser.add_argument('--download', action='store_true', help='Download PDFs')
    parser.add_argument('--start-from', type=int, default=None, help='Start from index')
    parser.add_argument('--progress-file', default='smart_progress.json', help='Progress file')

    args = parser.parse_args()

    from pathlib import Path
    base_dir = args.base_dir or str(Path(__file__).parent)

    processor = SmartBatchProcessor(args.csv, base_dir, args.progress_file)
    processor.process_all(
        max_results=args.max_results,
        download_pdfs=args.download,
        start_from=args.start_from
    )
