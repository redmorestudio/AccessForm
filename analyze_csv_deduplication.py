#!/usr/bin/env python3
"""Analyze CSV to show deduplication results"""

import csv

def extract_domain(url):
    """Extract clean domain from URL"""
    if not url:
        return ""
    if '://' in url:
        url = url.split('://', 1)[1]
    domain = url.split('/')[0]
    if domain.startswith('www.'):
        domain = domain[4:]
    return domain

csv_file = "StateAssets/ada_contacts - Full List - 1027.csv"

all_rows = []
seen = set()
unique_entries = []

with open(csv_file, 'r', encoding='utf-8') as f:
    reader = csv.DictReader(f)
    for row in reader:
        state = row.get('State', '').strip()
        city = row.get('City', '').strip()
        site = row.get('city site', '').strip()

        if state and city:
            site_domain = extract_domain(site)
            all_rows.append((state, city, site_domain))

            unique_key = (state, city, site_domain)
            if unique_key not in seen:
                seen.add(unique_key)
                unique_entries.append((state, city, site_domain))

print("CSV Deduplication Analysis")
print("=" * 80)
print(f"Total rows in CSV: {len(all_rows)}")
print(f"Unique (State, City, Site) combinations: {len(unique_entries)}")
print(f"Duplicate rows removed: {len(all_rows) - len(unique_entries)}")
print()

# Show examples of cities with multiple entries
from collections import Counter
city_counts = Counter([(state, city) for state, city, _ in all_rows])
duplicates = [(city, count) for city, count in city_counts.items() if count > 1]
duplicates.sort(key=lambda x: x[1], reverse=True)

print(f"Cities appearing multiple times in CSV: {len(duplicates)}")
print("\nTop 10 cities with most entries:")
print("-" * 80)
for (state, city), count in duplicates[:10]:
    print(f"  {city}, {state}: {count} entries")
    # Show the different sites for this city
    sites = [domain for s, c, domain in all_rows if s == state and c == city]
    unique_sites = list(dict.fromkeys(sites))  # preserve order
    for site in unique_sites:
        site_count = sites.count(site)
        print(f"    - {site} ({site_count}x)")
