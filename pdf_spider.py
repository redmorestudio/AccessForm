#!/usr/bin/env python3
"""
PDF Spider - Searches for PDFs from a specific city using Google Custom Search API
Filters PDFs by date and downloads metadata (or files) up to specified limit
"""

import requests
import time
import json
from datetime import datetime
from typing import List, Dict, Optional
import argparse
import os

API_KEY = "AIzaSyBgbSqCLu4XQ6P7et2u0ftKYOur89fjl24"
CSE_ID = "e6e56a5735e5748a6"
BASE_URL = "https://www.googleapis.com/customsearch/v1"

class PDFSpider:
    def __init__(self, api_key: str, cse_id: str):
        self.api_key = api_key
        self.cse_id = cse_id
        self.session = requests.Session()

    def search_pdfs(
        self,
        city: str,
        state: Optional[str] = None,
        site_domain: Optional[str] = None,
        max_results: Optional[int] = None,
        max_age_years: int = 5,
        search_terms: str = "",
        download_pdfs: bool = False,
        base_dir: Optional[str] = None
    ) -> List[Dict]:
        """
        Search for PDFs from a specific city

        Args:
            city: City name to search for
            state: State name (for directory organization)
            site_domain: City's website domain (e.g., "alexandriava.gov", "cityofpleasanthill.com")
                        If provided, uses site:domain to get all PDFs from that site
            max_results: Maximum number of PDFs to retrieve (default: 1000, API max: 100 per query)
            max_age_years: Maximum age of PDFs in years (default: 5)
            search_terms: Search terms including negative keywords (e.g., "form -tax" to exclude tax forms)
            download_pdfs: Whether to download the actual PDF files
            base_dir: Base directory for StateAssets (if None, uses current directory)

        Returns:
            List of PDF metadata dictionaries
        """
        if max_results is None:
            max_results = 1000

        # Set up output directory using StateAssets structure
        if base_dir is None:
            base_dir = os.path.dirname(os.path.abspath(__file__))

        if state:
            output_dir = os.path.join(base_dir, "StateAssets", state, city)
        else:
            output_dir = os.path.join(base_dir, "StateAssets", city)

        os.makedirs(output_dir, exist_ok=True)

        # Google CSE API limits to 100 results per query (10 pages * 10 results)
        # To get more, we'll use multiple queries with different terms
        query_variations = self._generate_query_variations(city, site_domain, search_terms)

        all_pdfs = []
        seen_urls = set()

        print(f"🕷️  PDF Spider started")
        print(f"📍 City: {city}" + (f", {state}" if state else ""))
        if site_domain:
            print(f"🌐 Site domain: {site_domain}")
        if search_terms:
            print(f"🔎 Search terms: {search_terms}")
        print(f"📊 Target: {max_results} PDFs (max {max_age_years} years old)")
        print(f"📁 Output directory: {output_dir}")
        print(f"🔍 Query variations: {len(query_variations)}")
        print("-" * 60)

        for query_idx, query in enumerate(query_variations, 1):
            if len(all_pdfs) >= max_results:
                break

            print(f"\n🔎 Query {query_idx}/{len(query_variations)}: {query}")

            # Paginate through results (max 10 pages per query)
            start_index = 1
            page = 1

            while start_index <= 91 and len(all_pdfs) < max_results:  # 91 is max start index (100 results total)
                try:
                    params = {
                        'key': self.api_key,
                        'cx': self.cse_id,
                        'q': query,
                        'fileType': 'pdf',
                        'start': start_index,
                        'num': 10,  # Max results per page
                        'dateRestrict': f'y{max_age_years}',  # Last X years
                    }

                    print(f"  📄 Page {page} (results {start_index}-{start_index+9})...", end=" ", flush=True)

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

                        # Skip duplicates
                        if url in seen_urls:
                            continue

                        seen_urls.add(url)

                        pdf_info = {
                            'title': item.get('title', 'Untitled'),
                            'url': url,
                            'snippet': item.get('snippet', ''),
                            'mime': item.get('mime', ''),
                            'fileFormat': item.get('fileFormat', ''),
                            'displayLink': item.get('displayLink', ''),
                            'retrieved_at': datetime.now().isoformat(),
                            'query': query
                        }

                        all_pdfs.append(pdf_info)
                        new_count += 1

                        if len(all_pdfs) >= max_results:
                            break

                    print(f"✓ {new_count} new PDFs (total: {len(all_pdfs)})")

                    if len(items) < 10:
                        print("  ℹ️  Less than 10 results, end of results for this query")
                        break

                    start_index += 10
                    page += 1

                    # Rate limiting to avoid hitting API limits
                    time.sleep(0.5)

                except requests.exceptions.HTTPError as e:
                    if e.response.status_code == 429:
                        print(f"⚠️  Rate limit hit, waiting 60 seconds...")
                        time.sleep(60)
                        continue
                    else:
                        print(f"❌ Error: {e}")
                        break
                except Exception as e:
                    print(f"❌ Error: {e}")
                    break

        print("\n" + "=" * 60)
        print(f"✅ Completed! Found {len(all_pdfs)} unique PDFs")

        # Download PDFs if requested
        if download_pdfs and all_pdfs:
            self._download_pdfs(all_pdfs, output_dir)

        return all_pdfs

    def _generate_query_variations(self, city: str, site_domain: Optional[str], search_terms: str) -> List[str]:
        """
        Generate multiple query variations to work around the 100-result API limit
        Supports negative keywords (e.g., "form -tax" excludes tax-related results)

        If site_domain is provided, focuses on that specific domain.
        Otherwise, searches broadly for the city name.
        """
        # If site domain is provided, use that as the primary constraint
        if site_domain:
            if search_terms:
                # Site-specific search with custom terms
                base_terms = [
                    f"site:{site_domain} {search_terms} filetype:pdf",
                    f"site:{site_domain} filetype:pdf",
                ]
            else:
                # Get all PDFs from the site - use multiple queries to get past 100 limit
                # Add variations to potentially get different results
                base_terms = [
                    f"site:{site_domain} filetype:pdf",
                    f'site:{site_domain} "application" filetype:pdf',
                    f'site:{site_domain} "form" filetype:pdf',
                    f'site:{site_domain} "permit" filetype:pdf',
                    f'site:{site_domain} "document" filetype:pdf',
                    f'site:{site_domain} "pdf"',  # Broader variation
                ]
        elif search_terms:
            # City name with custom search terms
            base_terms = [
                f"{city} {search_terms} filetype:pdf",
                f'site:.gov "{city}" {search_terms} filetype:pdf',
                f'site:.org "{city}" {search_terms} filetype:pdf',
            ]
        else:
            # Default: broad city search
            base_terms = [
                f"{city} filetype:pdf",
                f'site:.gov "{city}" filetype:pdf',
                f'site:.org "{city}" filetype:pdf',
                f'{city} "application" filetype:pdf',
                f'{city} "form" filetype:pdf',
                f'{city} "permit" filetype:pdf',
                f'{city} "document" filetype:pdf',
            ]

        # Filter out None values
        return [q for q in base_terms if q]

    def _download_pdfs(self, pdfs: List[Dict], output_dir: str):
        """
        Download actual PDF files
        """
        os.makedirs(output_dir, exist_ok=True)
        print(f"\n📥 Downloading PDFs to {output_dir}...")

        for idx, pdf in enumerate(pdfs, 1):
            try:
                url = pdf['url']
                # Create safe filename
                filename = f"{idx:04d}_{pdf['title'][:50]}.pdf"
                filename = "".join(c for c in filename if c.isalnum() or c in (' ', '-', '_', '.')).rstrip()
                filepath = os.path.join(output_dir, filename)

                print(f"  [{idx}/{len(pdfs)}] {filename}...", end=" ", flush=True)

                response = self.session.get(url, timeout=30)
                response.raise_for_status()

                with open(filepath, 'wb') as f:
                    f.write(response.content)

                print(f"✓ ({len(response.content) / 1024:.1f} KB)")
                time.sleep(0.5)  # Be nice to servers

            except Exception as e:
                print(f"❌ {e}")

    def save_metadata(self, pdfs: List[Dict], output_dir: str, filename: str = "pdf_results.json"):
        """
        Save PDF metadata to JSON file
        """
        filepath = os.path.join(output_dir, filename)
        with open(filepath, 'w', encoding='utf-8') as f:
            json.dump(pdfs, f, indent=2, ensure_ascii=False)
        print(f"\n💾 Metadata saved to {filepath}")


def main():
    parser = argparse.ArgumentParser(
        description='PDF Spider - Search for PDFs from a specific city using Google Custom Search API',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Get all PDFs from Alexandria's official website
  python pdf_spider.py Alexandria --state Virginia --site alexandriava.gov --download

  # Get forms from a city site, excluding tax forms
  python pdf_spider.py "Pleasant Hill" --state California --site cityofpleasanthill.com --terms "form -tax" --download

  # Search broadly without site constraint (searches entire web for city name)
  python pdf_spider.py "Green Bay" --state Wisconsin --download

  # Get permit applications from city site with custom limit
  python pdf_spider.py Centennial --state Colorado --site centennialco.gov --terms "permit application" --max-results 100

  # Custom base directory (default: current directory)
  python pdf_spider.py Alexandria --state Virginia --site alexandriava.gov --base-dir "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
        """
    )

    parser.add_argument('city', help='City name (e.g., "Alexandria", "Pleasant Hill")')
    parser.add_argument('--state', required=True, help='State name (e.g., "Virginia", "California")')
    parser.add_argument('--site', default=None,
                        help='City website domain (e.g., "alexandriava.gov", "cityofpleasanthill.com")')
    parser.add_argument('--max-results', type=int, default=1000,
                        help='Maximum number of PDFs to retrieve (default: 1000)')
    parser.add_argument('--max-age', type=int, default=5,
                        help='Maximum age of PDFs in years (default: 5)')
    parser.add_argument('--terms', default='',
                        help='Search terms including negative keywords (e.g., "form -tax" to exclude tax)')
    parser.add_argument('--download', action='store_true',
                        help='Download actual PDF files (not just metadata)')
    parser.add_argument('--base-dir', default=None,
                        help='Base directory for StateAssets (default: current directory)')
    parser.add_argument('--output-json', default='pdf_results.json',
                        help='JSON filename for metadata (default: pdf_results.json)')

    args = parser.parse_args()

    spider = PDFSpider(API_KEY, CSE_ID)

    results = spider.search_pdfs(
        city=args.city,
        state=args.state,
        site_domain=args.site,
        max_results=args.max_results,
        max_age_years=args.max_age,
        search_terms=args.terms,
        download_pdfs=args.download,
        base_dir=args.base_dir
    )

    # Get the output directory that was created
    if args.base_dir:
        output_dir = os.path.join(args.base_dir, "StateAssets", args.state, args.city)
    else:
        base_dir = os.path.dirname(os.path.abspath(__file__))
        output_dir = os.path.join(base_dir, "StateAssets", args.state, args.city)

    spider.save_metadata(results, output_dir, args.output_json)

    print(f"\n📊 Summary:")
    print(f"  • Total PDFs found: {len(results)}")
    print(f"  • Unique domains: {len(set(pdf['displayLink'] for pdf in results))}")
    print(f"  • Output directory: {output_dir}")
    print(f"  • Metadata saved: {os.path.join(output_dir, args.output_json)}")
    if args.download:
        print(f"  • PDFs downloaded: Yes")


if __name__ == "__main__":
    main()
