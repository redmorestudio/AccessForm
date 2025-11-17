#!/usr/bin/env python3
"""
Magical City Batch Processor for PDF Remediation
Handles: tagging, monitoring, organizing, and reporting for city PDF batches
"""

import os
import sys
import shutil
import time
import re
from collections import defaultdict
from datetime import datetime

class CityBatchProcessor:
    def __init__(self, city_path, city_code, base_dir=None):
        """
        Args:
            city_path: Full path to city folder (e.g., StateAssets/Colorado/Centennial)
            city_code: 3-letter city code (e.g., CEN)
            base_dir: Base directory for the project
        """
        self.city_path = city_path
        self.city_code = city_code.upper()
        self.base_dir = base_dir or "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
        self.remediation_dir = os.path.join(self.base_dir, "remediation-best")

        # Extract city name from path
        self.city_name = os.path.basename(city_path)
        self.state_name = os.path.basename(os.path.dirname(city_path))

    def tag_source_pdfs(self, dry_run=False):
        """Tag all PDFs in city folder with city code prefix"""
        pdfs = [f for f in os.listdir(self.city_path)
                if f.endswith('.pdf') and not f.startswith('_') and not f.startswith(self.city_code + '_')]

        print(f"\n{'='*70}")
        print(f"STEP 1: Tagging {len(pdfs)} PDFs with prefix '{self.city_code}_'")
        print(f"{'='*70}\n")

        tagged = []
        for pdf in pdfs:
            old_path = os.path.join(self.city_path, pdf)
            new_name = f"{self.city_code}_{pdf}"
            new_path = os.path.join(self.city_path, new_name)

            if dry_run:
                print(f"  [DRY RUN] Would rename: {pdf} → {new_name}")
            else:
                os.rename(old_path, new_path)
                print(f"  ✓ Tagged: {new_name}")
                tagged.append(new_name)

        return tagged

    def get_expected_pdfs(self):
        """Get list of tagged PDFs that should be processed"""
        return [f for f in os.listdir(self.city_path)
                if f.startswith(self.city_code + '_') and f.endswith('.pdf')]

    def check_completion(self):
        """Check if all city PDFs have completed remediation"""
        expected = self.get_expected_pdfs()

        if not expected:
            return False, [], []

        # Strip tags to get base names
        expected_bases = [f.replace(self.city_code + '_', '').replace('.pdf', '') for f in expected]

        # Check remediation-best for final files (iter3 or higher)
        completed = []
        for base in expected_bases:
            pattern = f"{self.city_code}_{base}_best_iter"
            matches = [f for f in os.listdir(self.remediation_dir)
                      if f.startswith(pattern) and f.endswith('.pdf') and '_violations' not in f]

            if matches:
                # Get highest iteration
                matches.sort()
                completed.append(matches[-1])

        is_complete = len(completed) == len(expected)
        return is_complete, completed, expected

    def parse_violations_report(self, filepath):
        """Parse a violations report file"""
        with open(filepath, 'r') as f:
            content = f.read()

        # Extract key info
        doc_name = re.search(r'Document: (.+?)\.pdf', content)
        iteration = re.search(r'Iteration: (\d+)', content)
        total_violations = re.search(r'Total Violations: (\d+)', content)
        compliance = re.search(r'Compliance Score: ([\d.]+)%', content)

        # Extract categories
        categories = {}
        for match in re.finditer(r'Category: ([\d.]+) \((\d+) violations?\)', content):
            cat = match.group(1)
            count = int(match.group(2))
            categories[cat] = count

        # Extract cost information
        total_cost = 0.0
        costs_by_service = {}

        cost_section = re.search(r'COST BREAKDOWN\s*=+\s*Total Cost: \$([0-9.]+)', content)
        if cost_section:
            total_cost = float(cost_section.group(1))

            # Parse individual service costs
            for match in re.finditer(r'- ([^:]+): \$([0-9.]+)', content):
                service_name = match.group(1).strip()
                service_cost = float(match.group(2))
                costs_by_service[service_name] = service_cost

        return {
            'doc_name': doc_name.group(1) if doc_name else 'Unknown',
            'iteration': int(iteration.group(1)) if iteration else 0,
            'total': int(total_violations.group(1)) if total_violations else 0,
            'compliance': float(compliance.group(1)) if compliance else 0.0,
            'categories': categories,
            'total_cost': total_cost,
            'costs_by_service': costs_by_service
        }

    def collect_all_iterations(self):
        """Collect all iteration data for all documents"""
        expected = self.get_expected_pdfs()
        expected_bases = [f.replace(self.city_code + '_', '').replace('.pdf', '') for f in expected]

        docs_data = {}

        for base in expected_bases:
            pattern = f"{self.city_code}_{base}_best_iter"

            # Find all violation reports for this document
            vio_files = [f for f in os.listdir(self.remediation_dir)
                        if f.startswith(pattern) and f.endswith('_violations.txt')]

            if not vio_files:
                continue

            # Parse each iteration
            iterations = {}
            for vio_file in vio_files:
                filepath = os.path.join(self.remediation_dir, vio_file)
                data = self.parse_violations_report(filepath)
                iterations[data['iteration']] = data

            docs_data[base] = iterations

        return docs_data

    def categorize_documents(self, docs_data):
        """Categorize documents into tiers based on final compliance"""
        tiers = {
            '100': {'name': '100 percent compliant', 'range': (100.0, 101.0), 'docs': []},
            '99': {'name': '99 percent (nearly complete)', 'range': (99.0, 100.0), 'docs': []},
            '97-98': {'name': '97-98 percent (minor work)', 'range': (97.0, 99.0), 'docs': []},
            '95-96': {'name': '95-96 percent (moderate work)', 'range': (95.0, 97.0), 'docs': []},
            'below-95': {'name': 'below 95 percent (significant work)', 'range': (0.0, 95.0), 'docs': []}
        }

        for doc_name, iterations in docs_data.items():
            if not iterations:
                continue

            # Get final iteration
            final_iter = max(iterations.keys())
            final_data = iterations[final_iter]
            compliance = final_data['compliance']

            # Categorize
            for tier_key, tier_info in tiers.items():
                min_c, max_c = tier_info['range']
                if min_c <= compliance < max_c:
                    tier_info['docs'].append((doc_name, iterations))
                    break

        return tiers

    def categorize_by_violation_reduction(self, docs_data):
        """Categorize documents by violation reduction percentage"""
        tiers = {
            '100': {'name': '100% violations fixed (fully remediated)', 'range': (100.0, 101.0), 'docs': []},
            '90-99': {'name': '90-99% violations fixed (nearly complete)', 'range': (90.0, 100.0), 'docs': []},
            '70-89': {'name': '70-89% violations fixed (major progress)', 'range': (70.0, 90.0), 'docs': []},
            '50-69': {'name': '50-69% violations fixed (moderate progress)', 'range': (50.0, 70.0), 'docs': []},
            '25-49': {'name': '25-49% violations fixed (minor progress)', 'range': (25.0, 50.0), 'docs': []},
            'below-25': {'name': 'Below 25% violations fixed (minimal progress)', 'range': (0.0, 25.0), 'docs': []}
        }

        for doc_name, iterations in docs_data.items():
            if not iterations:
                continue

            # Calculate violation reduction percentage
            if 0 not in iterations:
                # No initial data, can't calculate reduction
                continue

            initial = iterations[0]['total']
            if initial == 0:
                # Started with 0 violations
                continue

            final_iter = max(iterations.keys())
            final = iterations[final_iter]['total']

            reduction_pct = (initial - final) / initial * 100

            # Categorize by reduction percentage
            for tier_key, tier_info in tiers.items():
                min_r, max_r = tier_info['range']
                if min_r <= reduction_pct < max_r:
                    tier_info['docs'].append((doc_name, iterations))
                    break

        return tiers

    def generate_report(self, docs_data, tiers):
        """Generate comprehensive iteration-by-iteration report"""
        report_lines = []
        report_lines.append("="*120)
        report_lines.append(f"{self.city_name.upper()}, {self.state_name.upper()} - PDF REMEDIATION REPORT")
        report_lines.append("="*120)
        report_lines.append(f"Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        report_lines.append(f"City Code: {self.city_code}")
        report_lines.append(f"Total Documents: {len(docs_data)}")
        report_lines.append("")

        # Overall summary
        total_initial = sum(iters[0]['total'] for iters in docs_data.values() if 0 in iters)
        total_final = sum(iters[max(iters.keys())]['total'] for iters in docs_data.values() if iters)
        reduction = total_initial - total_final
        pct_reduction = (reduction / total_initial * 100) if total_initial > 0 else 0

        # Cost aggregation
        total_cost = 0.0
        service_costs = defaultdict(float)
        doc_costs = []

        for doc_name, iters in docs_data.items():
            # Get the final iteration's cost data
            if iters:
                final_iter = max(iters.keys())
                doc_cost = iters[final_iter].get('total_cost', 0.0)
                total_cost += doc_cost
                if doc_cost > 0:
                    doc_costs.append((doc_name, doc_cost))

                # Aggregate service costs
                costs_by_service = iters[final_iter].get('costs_by_service', {})
                for service, cost in costs_by_service.items():
                    service_costs[service] += cost

        avg_cost = total_cost / len(docs_data) if docs_data else 0.0

        report_lines.append("OVERALL SUMMARY")
        report_lines.append("-"*120)
        report_lines.append(f"  Initial Violations (Iteration 0): {total_initial:,}")
        report_lines.append(f"  Final Violations: {total_final:,}")
        report_lines.append(f"  Violations Fixed: {reduction:,} ({pct_reduction:.1f}% reduction)")
        report_lines.append("")

        # Add cost summary
        if total_cost > 0:
            report_lines.append("COST SUMMARY")
            report_lines.append("-"*120)
            report_lines.append(f"  Total Cost (All Documents): ${total_cost:.4f}")
            report_lines.append(f"  Average Cost per Document: ${avg_cost:.4f}")
            report_lines.append(f"  Number of Documents Processed: {len(docs_data)}")

            if service_costs:
                report_lines.append("")
                report_lines.append("  Cost Breakdown by Service:")
                for service in sorted(service_costs.keys()):
                    service_cost = service_costs[service]
                    pct_of_total = (service_cost / total_cost * 100) if total_cost > 0 else 0
                    report_lines.append(f"    - {service}: ${service_cost:.4f} ({pct_of_total:.1f}%)")

            if doc_costs:
                # Sort by cost (descending) to show most/least expensive
                doc_costs.sort(key=lambda x: x[1], reverse=True)
                report_lines.append("")
                report_lines.append(f"  Most Expensive Document: {doc_costs[0][0]} (${doc_costs[0][1]:.4f})")
                report_lines.append(f"  Least Expensive Document: {doc_costs[-1][0]} (${doc_costs[-1][1]:.4f})")

            report_lines.append("")
        report_lines.append("")
        report_lines.append("IMPORTANT: TWO DIFFERENT METRICS")
        report_lines.append("-"*120)
        report_lines.append("This report contains TWO tables with different tier classifications:")
        report_lines.append("")
        report_lines.append("TABLE 1: RULES-BASED COMPLIANCE (VeraPDF Metric)")
        report_lines.append("  - Shows what % of PDF/UA rules passed (NOT violation counts)")
        report_lines.append("  - A document with 2 failed rules but 100 violations shows ~98% compliant")
        report_lines.append("  - This is VeraPDF's standard compliance score")
        report_lines.append("")
        report_lines.append("TABLE 2: VIOLATIONS-BASED REMEDIATION PROGRESS (Actual Work Done)")
        report_lines.append("  - Shows actual violations fixed: (Initial - Final) / Initial × 100")
        report_lines.append("  - Example: 311 violations → 100 remaining = 67.8% fixed")
        report_lines.append("  - This reflects the real remediation work accomplished")
        report_lines.append("")
        report_lines.append("KEY INSIGHT: A document can be \"99% compliant\" by rules but have fixed only 5% of")
        report_lines.append("violations. Use Table 2 to understand actual remediation progress.")
        report_lines.append("")
        report_lines.append("")

        # Generate both tables
        report_lines.append("="*120)
        report_lines.append("TABLE 1: RULES-BASED COMPLIANCE (VeraPDF Rule Pass/Fail Ratio)")
        report_lines.append("="*120)
        report_lines.append("Note: This shows what % of PDF/UA rules passed, NOT violation counts.")
        report_lines.append("A document with 2 failed rules but 100 violations will show ~98% compliant.")
        report_lines.append("")

        # Rules-based table (existing logic)
        for tier_key in ['100', '99', '97-98', '95-96', 'below-95']:
            tier = tiers[tier_key]
            if not tier['docs']:
                continue

            report_lines.append("")
            report_lines.append(f"TIER: {tier['name'].upper()}")
            report_lines.append("-"*120)

            # Sort by initial violations (descending)
            sorted_docs = sorted(tier['docs'],
                               key=lambda x: x[1][0]['total'] if 0 in x[1] else 0,
                               reverse=True)

            # Build table
            header = f"{'Document':<50} {'Iter 0':>10} {'Iter 1':>10} {'Iter 2':>10} {'Iter 3':>10} {'Rule %':>10}"
            report_lines.append(header)
            report_lines.append("-"*120)

            for doc_name, iterations in sorted_docs:
                row = f"{doc_name[:48]:<50}"

                # Show compliance % for each iteration
                max_iter = max(iterations.keys())
                for i in range(4):  # iter 0-3
                    if i in iterations:
                        row += f" {iterations[i]['compliance']:>9.1f}%"
                    else:
                        row += f" {'-':>10}"

                # Final column
                final = iterations[max_iter]
                row += f" {final['compliance']:>9.1f}%"

                report_lines.append(row)

            report_lines.append("-"*120)
            report_lines.append(f"TIER TOTAL: {len(tier['docs'])} documents")
            report_lines.append("")

        # Violations-based table - use different categorization
        violation_tiers = self.categorize_by_violation_reduction(docs_data)

        report_lines.append("")
        report_lines.append("")
        report_lines.append("="*120)
        report_lines.append("TABLE 2: VIOLATIONS-BASED REMEDIATION PROGRESS (Actual Violation Counts)")
        report_lines.append("="*120)
        report_lines.append("Note: This shows actual violations fixed. % = (Initial - Final) / Initial * 100")
        report_lines.append("")

        for tier_key in ['100', '90-99', '70-89', '50-69', '25-49', 'below-25']:
            tier = violation_tiers[tier_key]
            if not tier['docs']:
                continue

            report_lines.append("")
            report_lines.append(f"TIER: {tier['name'].upper()}")
            report_lines.append("-"*120)

            # Sort by initial violations (descending)
            sorted_docs = sorted(tier['docs'],
                               key=lambda x: x[1][0]['total'] if 0 in x[1] else 0,
                               reverse=True)

            # Build table
            header = f"{'Document':<50} {'Iter 0':>10} {'Iter 1':>10} {'Iter 2':>10} {'Iter 3':>10} {'FINAL':>10} {'Fixed %':>10}"
            report_lines.append(header)
            report_lines.append("-"*120)

            for doc_name, iterations in sorted_docs:
                row = f"{doc_name[:48]:<50}"

                # Show each iteration violation count
                max_iter = max(iterations.keys())
                initial_violations = iterations[0]['total'] if 0 in iterations else 0

                for i in range(4):  # iter 0-3
                    if i in iterations:
                        row += f" {iterations[i]['total']:>10}"
                    else:
                        row += f" {'-':>10}"

                # Final column
                final = iterations[max_iter]
                final_violations = final['total']
                row += f" {final_violations:>10}"

                # Calculate violation reduction %
                if initial_violations > 0:
                    reduction_pct = (initial_violations - final_violations) / initial_violations * 100
                    row += f" {reduction_pct:>9.1f}%"
                else:
                    row += f" {'-':>10}"

                report_lines.append(row)

            # Tier summary
            report_lines.append("-"*120)
            tier_initial = sum(iters[0]['total'] for _, iters in tier['docs'] if 0 in iters)
            tier_final = sum(iters[max(iters.keys())]['total'] for _, iters in tier['docs'] if iters)
            report_lines.append(f"{'TIER TOTAL: ' + str(len(tier['docs'])) + ' documents':<50} " +
                              f"{tier_initial:>10} {'':>10} {'':>10} {'':>10} {tier_final:>10}")
            report_lines.append("")

        return "\n".join(report_lines)

    def organize_files(self, tiers, dry_run=False):
        """Organize remediated PDFs into tier folders"""
        print(f"\n{'='*70}")
        print(f"STEP 3: Organizing files into tier folders")
        print(f"{'='*70}\n")

        organized_count = 0

        for tier_key, tier in tiers.items():
            if not tier['docs']:
                continue

            # Create tier folder
            tier_folder = os.path.join(self.city_path, tier['name'])
            if not dry_run:
                os.makedirs(tier_folder, exist_ok=True)
            print(f"\n📁 {tier['name']}/")

            for doc_name, iterations in tier['docs']:
                # Find the final PDF
                max_iter = max(iterations.keys())
                pattern = f"{self.city_code}_{doc_name}_best_iter"

                pdf_files = [f for f in os.listdir(self.remediation_dir)
                           if f.startswith(pattern) and f.endswith('.pdf') and '_violations' not in f]
                vio_files = [f for f in os.listdir(self.remediation_dir)
                           if f.startswith(pattern) and f.endswith('_violations.txt')]

                if pdf_files:
                    pdf_files.sort()
                    final_pdf = pdf_files[-1]

                    # Strip city code from filename
                    new_pdf_name = final_pdf.replace(self.city_code + '_', '')

                    if dry_run:
                        print(f"  [DRY RUN] Would copy: {final_pdf} → {new_pdf_name}")
                    else:
                        src = os.path.join(self.remediation_dir, final_pdf)
                        dst = os.path.join(tier_folder, new_pdf_name)
                        shutil.copy2(src, dst)
                        print(f"  ✓ PDF: {new_pdf_name}")
                        organized_count += 1

                # Copy all violation reports
                if vio_files:
                    vio_files.sort()
                    for vio_file in vio_files:
                        new_vio_name = vio_file.replace(self.city_code + '_', '')

                        if dry_run:
                            print(f"  [DRY RUN] Would copy: {vio_file} → {new_vio_name}")
                        else:
                            src = os.path.join(self.remediation_dir, vio_file)
                            dst = os.path.join(tier_folder, new_vio_name)
                            if not os.path.exists(dst):
                                shutil.copy2(src, dst)

        return organized_count

    def run_full_pipeline(self, dry_run=False):
        """Run the complete processing pipeline"""
        print(f"\n{'='*70}")
        print(f"MAGICAL CITY BATCH PROCESSOR")
        print(f"{'='*70}")
        print(f"City: {self.city_name}, {self.state_name}")
        print(f"Code: {self.city_code}")
        print(f"Path: {self.city_path}")
        print(f"{'='*70}\n")

        # Step 1: Tag source PDFs
        self.tag_source_pdfs(dry_run=dry_run)

        print(f"\n✓ Step 1 complete. PDFs tagged and ready for remediation.")
        print(f"\n{'='*70}")
        print(f"NEXT STEP: Run your remediation service on these tagged PDFs")
        print(f"{'='*70}\n")

        return True

    def run_organization(self, dry_run=False):
        """Run organization after remediation completes"""
        print(f"\n{'='*70}")
        print(f"STEP 2: Checking remediation completion...")
        print(f"{'='*70}\n")

        is_complete, completed, expected = self.check_completion()

        if not is_complete:
            print(f"❌ Remediation not complete yet.")
            print(f"   Expected: {len(expected)} documents")
            print(f"   Completed: {len(completed)} documents")
            return False

        print(f"✓ All {len(completed)} documents completed!\n")

        # Collect iteration data
        docs_data = self.collect_all_iterations()

        # Categorize into tiers
        tiers = self.categorize_documents(docs_data)

        # Generate report
        report = self.generate_report(docs_data, tiers)

        # Save report
        report_filename = f"{self.city_name.lower()}_remediation_report_{datetime.now().strftime('%Y%m%d_%H%M%S')}.txt"
        report_path = os.path.join(self.city_path, report_filename)

        if not dry_run:
            with open(report_path, 'w') as f:
                f.write(report)

        print(report)
        print(f"\n{'='*70}")
        print(f"Report saved to: {report_filename}")
        print(f"{'='*70}\n")

        # Organize files
        self.organize_files(tiers, dry_run=dry_run)

        print(f"\n{'='*70}")
        print(f"✓ ORGANIZATION COMPLETE!")
        print(f"{'='*70}\n")

        return True


def main():
    if len(sys.argv) < 3:
        print("Usage: python city_batch_processor.py <city_path> <city_code> [command]")
        print("Commands:")
        print("  tag       - Tag PDFs and prepare for remediation")
        print("  organize  - Organize completed remediation results")
        print("  check     - Check remediation completion status")
        sys.exit(1)

    city_path = sys.argv[1]
    city_code = sys.argv[2]
    command = sys.argv[3] if len(sys.argv) > 3 else "tag"

    processor = CityBatchProcessor(city_path, city_code)

    if command == "tag":
        processor.run_full_pipeline()
    elif command == "organize":
        processor.run_organization()
    elif command == "check":
        is_complete, completed, expected = processor.check_completion()
        print(f"\nCompletion Status:")
        print(f"  Expected: {len(expected)} documents")
        print(f"  Completed: {len(completed)} documents")
        print(f"  Status: {'✓ COMPLETE' if is_complete else '⏳ In Progress'}\n")
    else:
        print(f"Unknown command: {command}")
        sys.exit(1)


if __name__ == "__main__":
    main()
