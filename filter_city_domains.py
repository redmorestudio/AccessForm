#!/usr/bin/env python3
"""
Filter CSV to keep only city government domains, excluding:
- Airports, transit authorities, courts, counties, schools, universities
"""

import csv
import re
from collections import defaultdict
from datetime import datetime

def extract_domain(url):
    """Extract clean domain from URL"""
    if not url:
        return ""
    if '://' in url:
        url = url.split('://', 1)[1]
    domain = url.split('/')[0]
    if domain.startswith('www.'):
        domain = domain[4:]
    return domain.lower()

def is_non_city_domain(domain, city_name):
    """Check if domain belongs to non-city entity"""
    domain_lower = domain.lower()
    city_lower = city_name.lower().replace(' ', '').replace('-', '')

    # Exclude patterns
    non_city_patterns = [
        'airport', 'transit', 'court', 'county', 'judicial',
        'school', 'district', '.edu', 'university', 'college',
        'transportation', 'metro', 'ride-', 'adsd.nv.gov',
        'lacourt', 'cssd.', 'dir.ca.gov', 'coaccess.com',
        'dpa.colorado.gov', 'coloradojudicial', 'realjourney.org',
        'webbcountytx', 'hcfl.gov', 'hollywoodburbankairport',
        'shastacounty', 'jeffparish.net', 'clarkcountynv',
        'snhpc.org', 'michigan.gov', 'nebraskajudicial',
        'lextran.com', 'kerncounty.com', 'norfun.org',
        'goventura.org', 'gonctd.com', 'factsd.org',
        'rtcwashoe.com', 'washoecourts', 'washoecounty',
        'stanislaus.courts', 'stancounty.com', 'mcs4kids',
        'alameda.courts', 'sanbernardino.courts', 'riverside.courts',
        'sanmateo.courts', 'en.ocgov.com', 'rc-hr.com', 'rm.sbcounty',
        'trans.rctlma', 'mvusd.net', 'csusb.edu', 'egusd.net',
        'provo.edu', 'louisville.edu'
    ]

    for pattern in non_city_patterns:
        if pattern in domain_lower:
            return True

    return False

def is_likely_city_domain(domain, city_name, state_name):
    """Check if domain looks like a city government website"""
    domain_lower = domain.lower()
    city_lower = city_name.lower().replace(' ', '').replace('-', '')
    state_lower = state_name.lower()

    # Positive indicators
    city_indicators = [
        f'{city_lower}',
        'city',
        '.gov',
        '.us'
    ]

    # Check if domain contains city name and gov-like TLD
    has_city_name = any(part in domain_lower for part in [city_lower, city_name.lower().replace(' ', '')])
    is_gov_tld = domain_lower.endswith(('.gov', '.us', '.org'))

    return has_city_name and is_gov_tld

def filter_csv(input_csv, output_csv):
    """Filter CSV to keep only city government domains"""

    # Read all rows and group by (state, city)
    city_domains = defaultdict(list)

    with open(input_csv, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            state = row.get('State', '').strip()
            city = row.get('City', '').strip()
            site = row.get('city site', '').strip()

            if not state or not city or not site:
                continue

            domain = extract_domain(site)
            if not domain:
                continue

            key = (state, city)
            city_domains[key].append({
                'row': row,
                'domain': domain,
                'site': site
            })

    # Analyze and filter
    filtered_rows = []
    excluded_cities = defaultdict(list)
    kept_cities = defaultdict(str)

    for (state, city), entries in city_domains.items():
        # Get unique domains for this city
        unique_domains = {}
        for entry in entries:
            domain = entry['domain']
            if domain not in unique_domains:
                unique_domains[domain] = entry

        # If only one domain, keep it (unless it's clearly non-city)
        if len(unique_domains) == 1:
            domain = list(unique_domains.keys())[0]
            if not is_non_city_domain(domain, city):
                filtered_rows.append(unique_domains[domain]['row'])
                kept_cities[(state, city)] = domain
            else:
                excluded_cities[(state, city)].append(f"EXCLUDED (only domain): {domain}")
        else:
            # Multiple domains - filter to city government only
            city_domains_found = []
            non_city_domains_found = []

            for domain, entry in unique_domains.items():
                if is_non_city_domain(domain, city):
                    non_city_domains_found.append(domain)
                else:
                    city_domains_found.append((domain, entry))

            # Keep the first city domain found
            if city_domains_found:
                domain, entry = city_domains_found[0]
                filtered_rows.append(entry['row'])
                kept_cities[(state, city)] = domain

                if non_city_domains_found:
                    excluded_cities[(state, city)] = non_city_domains_found
            else:
                # No clear city domain - keep first one and flag it
                domain, entry = list(unique_domains.items())[0]
                filtered_rows.append(entry['row'])
                kept_cities[(state, city)] = f"{domain} (UNCERTAIN)"
                excluded_cities[(state, city)] = list(unique_domains.keys())[1:]

    # Write filtered CSV
    with open(output_csv, 'w', encoding='utf-8', newline='') as f:
        if filtered_rows:
            fieldnames = filtered_rows[0].keys()
            writer = csv.DictWriter(f, fieldnames=fieldnames)
            writer.writeheader()
            writer.writerows(filtered_rows)

    # Generate report
    report_file = f"filter_report_{datetime.now().strftime('%Y%m%d_%H%M%S')}.txt"
    with open(report_file, 'w') as f:
        f.write("=" * 80 + "\n")
        f.write("CITY DOMAIN FILTERING REPORT\n")
        f.write(f"Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")
        f.write("=" * 80 + "\n\n")

        f.write(f"Total unique cities: {len(city_domains)}\n")
        f.write(f"Cities with multiple domains: {len(excluded_cities)}\n")
        f.write(f"Filtered rows output: {len(filtered_rows)}\n\n")

        if excluded_cities:
            f.write("=" * 80 + "\n")
            f.write("CITIES WITH EXCLUDED DOMAINS\n")
            f.write("=" * 80 + "\n\n")

            for (state, city), excluded in sorted(excluded_cities.items()):
                f.write(f"{city}, {state}\n")
                f.write(f"  Kept: {kept_cities[(state, city)]}\n")
                if isinstance(excluded, list):
                    for domain in excluded:
                        f.write(f"  Excluded: {domain}\n")
                else:
                    f.write(f"  {excluded}\n")
                f.write("\n")

    print(f"✓ Filtered CSV written to: {output_csv}")
    print(f"✓ Report written to: {report_file}")
    print(f"\nSummary:")
    print(f"  Total cities: {len(city_domains)}")
    print(f"  Cities with excluded domains: {len(excluded_cities)}")
    print(f"  Filtered rows: {len(filtered_rows)}")

if __name__ == "__main__":
    input_csv = "StateAssets/ada_contacts - Full List - 1027.csv"
    output_csv = "StateAssets/city_domains_only.csv"

    filter_csv(input_csv, output_csv)
