#!/usr/bin/env python3
"""Test script to examine detail page structure"""
from selenium import webdriver
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
import time

# Setup Chrome
options = webdriver.ChromeOptions()
options.add_argument('--headless=new')
options.add_argument('--disable-gpu')
options.add_argument('--window-size=1920,1080')

driver = webdriver.Chrome(options=options)
wait = WebDriverWait(driver, 30)

try:
    # Navigate to a detail page
    url = "https://www.txsmartbuy.gov/esbd/802-26-67404-R"
    print(f"Loading: {url}")
    driver.get(url)
    time.sleep(5)

    # Save page source
    with open('detail_page_debug.html', 'w', encoding='utf-8') as f:
        f.write(driver.page_source)
    print("Saved to detail_page_debug.html")

    # Get all text
    body = driver.find_element(By.TAG_NAME, "body")
    print("\n" + "="*80)
    print("PAGE TEXT:")
    print("="*80)
    print(body.text[:2000])

finally:
    driver.quit()
