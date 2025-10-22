#!/usr/bin/env python3
"""Test scraping a single entry"""
import sys
sys.path.insert(0, '/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets')
from scrape_txsmartbuy import TxSmartBuyScraper

scraper = TxSmartBuyScraper()
scraper.setup_driver()

# Test detail page scraping
url = "https://www.txsmartbuy.gov/esbd/802-26-67404-R"
print(f"Testing: {url}\n")

detail_data = scraper.scrape_detail_page(url)

print("Detail Data:")
for key, value in detail_data.items():
    if value:
        print(f"  {key}: {value[:100] if len(value) > 100 else value}")
    else:
        print(f"  {key}: [EMPTY]")

scraper.driver.quit()
