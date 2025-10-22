#!/usr/bin/env python3
"""
Texas SmartBuy ESBD Scraper
Scrapes solicitation data from https://www.txsmartbuy.gov/esbd
"""
import csv
import time
import os
from selenium import webdriver
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
from selenium.common.exceptions import TimeoutException, NoSuchElementException
from datetime import datetime

class TxSmartBuyScraper:
    def __init__(self, output_file="txsmartbuy_solicitations.csv", start_page=1):
        self.output_file = output_file
        self.start_page = start_page
        self.driver = None
        self.wait = None

    def setup_driver(self):
        """Initialize Chrome driver with options"""
        options = webdriver.ChromeOptions()

        # Run headless (no visible browser window)
        options.add_argument('--headless=new')
        options.add_argument('--disable-gpu')
        options.add_argument('--window-size=1920,1080')

        options.add_argument('--disable-blink-features=AutomationControlled')
        options.add_experimental_option("excludeSwitches", ["enable-automation"])
        options.add_experimental_option('useAutomationExtension', False)

        # Add user agent to look more like a real browser
        options.add_argument('user-agent=Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36')

        self.driver = webdriver.Chrome(options=options)
        self.wait = WebDriverWait(self.driver, 30)  # Increased timeout for JS-heavy pages

    def init_csv(self):
        """Initialize CSV file with headers"""
        file_exists = os.path.exists(self.output_file)

        if not file_exists:
            with open(self.output_file, 'w', newline='', encoding='utf-8') as f:
                writer = csv.writer(f)
                writer.writerow([
                    'Title',
                    'Link',
                    'Solicitation ID',
                    'Status (List)',
                    'Due Date',
                    'Due Time',
                    'Agency/Texas SmartBuy Member Number',
                    'Posting Date',
                    'Status (Detail)',
                    'Contact Name',
                    'Contact Number',
                    'Contact Email',
                    'Response Due Date',
                    'Response Due Time',
                    'Posting Requirement',
                    'Last Modified',
                    'Class/Item Code',
                    'Solicitation Description'
                ])

    def scrape_list_entry(self, entry_element):
        """Extract data from a single entry on the list page"""
        try:
            # Get title and link
            title_element = entry_element.find_element(By.CSS_SELECTOR, ".esbd-result-title a")
            title = title_element.text.strip()
            link = title_element.get_attribute('href')

            # Make link absolute if it's relative
            if link and link.startswith('/'):
                link = 'https://www.txsmartbuy.gov' + link

            # Get other fields from the list page
            entry_data = {
                'title': title,
                'link': link,
                'solicitation_id': '',
                'status_list': '',
                'due_date': '',
                'due_time': '',
                'agency_number': '',
                'posting_date': ''
            }

            # Extract text content and parse fields
            entry_text = entry_element.text
            lines = [line.strip() for line in entry_text.split('\n') if line.strip()]

            for line in lines:
                if 'Solicitation ID:' in line:
                    entry_data['solicitation_id'] = line.split('Solicitation ID:')[1].strip()
                elif 'Status:' in line:
                    entry_data['status_list'] = line.split('Status:')[1].strip()
                elif 'Due Date:' in line:
                    entry_data['due_date'] = line.split('Due Date:')[1].strip()
                elif 'Due Time:' in line:
                    entry_data['due_time'] = line.split('Due Time:')[1].strip()
                elif 'Agency/Texas SmartBuy Member Number:' in line:
                    entry_data['agency_number'] = line.split('Agency/Texas SmartBuy Member Number:')[1].strip()
                elif 'Posting Date:' in line:
                    entry_data['posting_date'] = line.split('Posting Date:')[1].strip()

            return entry_data

        except Exception as e:
            print(f"Error: {str(e)}")
            return None

    def scrape_detail_page(self, url):
        """Navigate to detail page and extract additional information"""
        detail_data = {
            'status_detail': '',
            'contact_name': '',
            'contact_number': '',
            'contact_email': '',
            'response_due_date': '',
            'response_due_time': '',
            'posting_requirement': '',
            'last_modified': '',
            'class_item_code': '',
            'solicitation_description': ''
        }

        try:
            # Open detail page in same window
            self.driver.get(url)
            time.sleep(2)

            # Wait for content to load
            self.wait.until(EC.presence_of_element_located((By.CLASS_NAME, "esbd-result-cell")))

            # Extract fields using CSS selectors
            # Find all result cells
            cells = self.driver.find_elements(By.CLASS_NAME, "esbd-result-cell")

            for cell in cells:
                try:
                    # Get the label (strong tag) and value (p tag)
                    label_elem = cell.find_element(By.TAG_NAME, "strong")
                    value_elem = cell.find_element(By.TAG_NAME, "p")

                    label = label_elem.text.strip().replace('\xa0', '').replace(':', '').strip()
                    value = value_elem.text.strip()

                    # Map to our fields
                    if label == 'Status':
                        detail_data['status_detail'] = value
                    elif label == 'Contact Name':
                        detail_data['contact_name'] = value
                    elif label == 'Contact Number':
                        detail_data['contact_number'] = value
                    elif label == 'Contact Email':
                        detail_data['contact_email'] = value
                    elif label == 'Response Due Date':
                        detail_data['response_due_date'] = value
                    elif label == 'Response Due Time':
                        detail_data['response_due_time'] = value
                    elif label == 'Posting Requirement':
                        detail_data['posting_requirement'] = value
                    elif label == 'Last Modified':
                        detail_data['last_modified'] = value
                    elif label == 'Class/Item Code':
                        detail_data['class_item_code'] = value

                except:
                    continue

            # Get solicitation description (different structure)
            try:
                desc_section = self.driver.find_element(By.CLASS_NAME, "esbd-full-width")
                desc_content = desc_section.find_element(By.CLASS_NAME, "rich-text-editor-content")
                detail_data['solicitation_description'] = desc_content.text.strip()
            except:
                pass

            return detail_data

        except Exception as e:
            print(f"  Error scraping detail page: {str(e)}")
            return detail_data

    def scrape_page(self, page_num):
        """Scrape all entries on a single page"""
        print(f"\nPage {page_num}:")

        # Navigate to the page
        if page_num == 1:
            url = "https://www.txsmartbuy.gov/esbd"
        else:
            url = f"https://www.txsmartbuy.gov/esbd?page={page_num}"
        self.driver.get(url)

        # Wait for JavaScript to load and render content
        print(f"  Waiting for page to load...")
        time.sleep(5)  # Give extra time for JS to execute

        # Wait for the main div to become visible
        try:
            main_div = self.wait.until(
                EC.presence_of_element_located((By.ID, "main"))
            )
            print(f"  Main div found")
        except TimeoutException:
            print(f"  Main div not found - page may not have loaded")

        # Wait a bit more for content
        time.sleep(3)

        # Save page source for debugging
        if page_num == 1:
            with open('page_source_debug.html', 'w', encoding='utf-8') as f:
                f.write(self.driver.page_source)
            print(f"  Saved page source to page_source_debug.html for inspection")

            # Print out any h1, h2, h3 tags to understand page structure
            headers = self.driver.find_elements(By.CSS_SELECTOR, "h1, h2, h3")
            if headers:
                print(f"  Found {len(headers)} headers:")
                for h in headers[:5]:
                    print(f"    - {h.text[:100]}")

        # Find all entry elements
        try:
            entries = self.driver.find_elements(By.CLASS_NAME, "esbd-result-row")
            print(f"  Found {len(entries)} entries")
        except Exception as e:
            print(f"  Error finding entries: {e}")
            return 0

        if not entries:
            print(f"  No entries found")
            return 0
        print(f"  Found {len(entries)} entries")

        entries_scraped = 0

        for idx, entry in enumerate(entries, 1):
            print(f"  Entry {idx}/{len(entries)}...", end=' ')

            # Scrape list data
            list_data = self.scrape_list_entry(entry)
            if not list_data:
                print("FAILED (list)")
                continue

            # Scrape detail page
            detail_data = self.scrape_detail_page(list_data['link'])

            # Combine and save
            row = [
                list_data['title'],
                list_data['link'],
                list_data['solicitation_id'],
                list_data['status_list'],
                list_data['due_date'],
                list_data['due_time'],
                list_data['agency_number'],
                list_data['posting_date'],
                detail_data['status_detail'],
                detail_data['contact_name'],
                detail_data['contact_number'],
                detail_data['contact_email'],
                detail_data['response_due_date'],
                detail_data['response_due_time'],
                detail_data['posting_requirement'],
                detail_data['last_modified'],
                detail_data['class_item_code'],
                detail_data['solicitation_description']
            ]

            # Append to CSV
            with open(self.output_file, 'a', newline='', encoding='utf-8') as f:
                writer = csv.writer(f)
                writer.writerow(row)

            print("OK")
            entries_scraped += 1

            # Navigate back to list page
            self.driver.back()
            time.sleep(1)

        return entries_scraped

    def run(self, max_pages=None):
        """Run the scraper"""
        try:
            print("Texas SmartBuy ESBD Scraper")
            print("=" * 50)

            self.setup_driver()
            self.init_csv()

            # Determine number of pages
            if max_pages is None:
                # Navigate to first page to find total pages
                self.driver.get("https://www.txsmartbuy.gov/esbd")
                time.sleep(2)

                try:
                    # Look for pagination info
                    pagination = self.driver.find_element(By.CLASS_NAME, "pagination")
                    pagination_text = pagination.text
                    print(f"Pagination info: {pagination_text}")
                    max_pages = 2000  # Default if we can't parse
                except:
                    max_pages = 2000

            print(f"Starting from page {self.start_page}")
            print(f"Maximum pages: {max_pages}")
            print(f"Output file: {self.output_file}")
            print("=" * 50)

            total_entries = 0
            start_time = time.time()

            for page_num in range(self.start_page, max_pages + 1):
                try:
                    entries = self.scrape_page(page_num)
                    total_entries += entries

                    elapsed = time.time() - start_time
                    avg_time = elapsed / (page_num - self.start_page + 1)
                    remaining = (max_pages - page_num) * avg_time

                    print(f"  Progress: {page_num}/{max_pages} pages | "
                          f"Total entries: {total_entries} | "
                          f"Est. remaining: {remaining/3600:.1f}h")

                except Exception as e:
                    print(f"  Error on page {page_num}: {str(e)}")
                    continue

            print("\n" + "=" * 50)
            print(f"Scraping complete!")
            print(f"Total entries: {total_entries}")
            print(f"Total time: {(time.time() - start_time)/3600:.2f}h")
            print("=" * 50)

        finally:
            if self.driver:
                self.driver.quit()

def main():
    import sys

    # Parse command line arguments
    start_page = 1
    max_pages = None
    output_file = "txsmartbuy_solicitations.csv"

    if len(sys.argv) > 1:
        start_page = int(sys.argv[1])
    if len(sys.argv) > 2:
        max_pages = int(sys.argv[2])
    if len(sys.argv) > 3:
        output_file = sys.argv[3]

    scraper = TxSmartBuyScraper(output_file=output_file, start_page=start_page)
    scraper.run(max_pages=max_pages)

if __name__ == "__main__":
    main()
