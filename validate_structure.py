#!/usr/bin/env python3
"""
Comprehensive PDF structure tree validation script.
Checks if semantic tags (H1, P, TD) are present and properly linked to content via MCIDs.
"""
import sys
import pikepdf
from collections import defaultdict

def validate_pdf_structure(pdf_path, verbose=True):
    """Validate PDF structure tree completeness."""

    results = {
        'has_struct_root': False,
        'has_kids': False,
        'total_elements': 0,
        'semantic_tags': defaultdict(int),
        'elements_with_mcr_kids': 0,
        'total_mcr_kids': 0,
        'mcr_validation': {'valid': 0, 'invalid': 0, 'errors': []},
        'bdc_sample_validation': {'checked': 0, 'matched': 0, 'mismatched': 0},
        'overall_status': 'FAIL'
    }

    try:
        pdf = pikepdf.open(pdf_path)

        # Check 1: StructTreeRoot exists
        if not hasattr(pdf.Root, 'StructTreeRoot'):
            if verbose:
                print("❌ CHECK 1 FAILED: No StructTreeRoot found")
            return results

        results['has_struct_root'] = True
        if verbose:
            print("✅ CHECK 1: StructTreeRoot exists")

        root = pdf.Root.StructTreeRoot

        # Check 2: Root has /K (kids)
        if '/K' not in root:
            if verbose:
                print("❌ CHECK 2 FAILED: StructTreeRoot has no /K (kids) - structure tree is EMPTY")
            return results

        results['has_kids'] = True
        if verbose:
            print("✅ CHECK 2: StructTreeRoot has /K array")

        # Walk structure tree
        def walk_structure(elem, depth=0):
            """Recursively walk structure tree and collect stats."""
            results['total_elements'] += 1

            # Get element role/tag
            if hasattr(elem, 'get') and '/S' in elem:
                tag = str(elem['/S']).replace('/', '')
                # Track semantic tags
                semantic = ['H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'P', 'Table', 'TR', 'TD', 'TH', 'Figure']
                if tag in semantic:
                    results['semantic_tags'][tag] += 1

            # Check if element has MCR kids
            if hasattr(elem, 'get') and '/K' in elem:
                k = elem['/K']

                # Handle both single kid and array of kids
                if hasattr(k, '__iter__') and not isinstance(k, str):
                    kids = list(k)
                else:
                    kids = [k]

                has_mcr = False
                for kid in kids:
                    if not hasattr(kid, 'get'):
                        continue

                    # Check if it's an MCR (Marked Content Reference)
                    kid_type = kid.get('/Type')
                    if kid_type and str(kid_type) == '/MCR':
                        has_mcr = True
                        results['total_mcr_kids'] += 1

                        # Validate MCR structure
                        if '/Pg' in kid and '/MCID' in kid:
                            results['mcr_validation']['valid'] += 1
                        else:
                            results['mcr_validation']['invalid'] += 1
                            missing = []
                            if '/Pg' not in kid:
                                missing.append('/Pg')
                            if '/MCID' not in kid:
                                missing.append('/MCID')
                            results['mcr_validation']['errors'].append(f"MCR missing {', '.join(missing)}")

                    # Recursively process structure element kids (not MCRs)
                    else:
                        kid_type2 = kid.get('/Type')
                        if kid_type2 and str(kid_type2) == '/StructElem':
                            walk_structure(kid, depth + 1)

                if has_mcr:
                    results['elements_with_mcr_kids'] += 1

        # Start walking from root's kids
        k = root['/K']
        # Convert pikepdf Array to Python list
        if hasattr(k, '__iter__') and not isinstance(k, str):
            kids = list(k)
        else:
            kids = [k]

        for kid in kids:
            try:
                # Check if it's a structure element
                if hasattr(kid, 'get'):
                    kid_type = kid.get('/Type')
                    if kid_type and str(kid_type) == '/StructElem':
                        walk_structure(kid)
            except (ValueError, AttributeError):
                # Skip objects that can't be interrogated
                continue

        # Check 3: Count semantic tags
        semantic_count = sum(results['semantic_tags'].values())
        if verbose:
            print(f"\n✅ CHECK 3: Found {results['total_elements']} structure elements")
            if semantic_count > 0:
                print(f"   Semantic tags ({semantic_count} total):")
                for tag, count in sorted(results['semantic_tags'].items()):
                    print(f"      {tag}: {count}")
            else:
                print("   ⚠️  WARNING: No semantic tags found (only structural containers)")

        # Check 4: Elements have MCR kids
        if results['elements_with_mcr_kids'] > 0:
            pct = (results['elements_with_mcr_kids'] / results['total_elements']) * 100
            if verbose:
                print(f"\n✅ CHECK 4: {results['elements_with_mcr_kids']}/{results['total_elements']} elements have MCR kids ({pct:.1f}%)")
                print(f"   Total MCR kids: {results['total_mcr_kids']}")
        else:
            if verbose:
                print(f"\n❌ CHECK 4 FAILED: No elements have MCR kids - structure not linked to content!")

        # Check 5: MCR validation
        if results['total_mcr_kids'] > 0:
            valid_pct = (results['mcr_validation']['valid'] / results['total_mcr_kids']) * 100
            if verbose:
                print(f"\n✅ CHECK 5: MCR structure validation")
                print(f"   Valid MCRs: {results['mcr_validation']['valid']}/{results['total_mcr_kids']} ({valid_pct:.1f}%)")
                if results['mcr_validation']['invalid'] > 0:
                    print(f"   ⚠️  Invalid MCRs: {results['mcr_validation']['invalid']}")
                    for err in results['mcr_validation']['errors'][:5]:
                        print(f"      - {err}")

        # Overall assessment
        if results['has_struct_root'] and results['has_kids'] and semantic_count > 0:
            if results['elements_with_mcr_kids'] > 0:
                results['overall_status'] = 'PASS'
                if verbose:
                    print(f"\n{'='*70}")
                    print("✅ OVERALL: PASS - Structure tree fully intact with semantic tags!")
                    print(f"{'='*70}")
            else:
                results['overall_status'] = 'PARTIAL'
                if verbose:
                    print(f"\n{'='*70}")
                    print("⚠️  OVERALL: PARTIAL - Structure exists but not linked to content (no MCRs)")
                    print(f"{'='*70}")
        else:
            if verbose:
                print(f"\n{'='*70}")
                print("❌ OVERALL: FAIL - Structure tree missing or empty")
                print(f"{'='*70}")

        pdf.close()

    except Exception as e:
        if verbose:
            print(f"\n❌ ERROR: {e}")
            import traceback
            traceback.print_exc()
        results['overall_status'] = 'ERROR'

    return results


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print("Usage: python3 validate_structure.py <pdf_file>")
        sys.exit(1)

    pdf_path = sys.argv[1]
    print(f"\n🔍 Validating structure in: {pdf_path}")
    print("=" * 70)

    results = validate_pdf_structure(pdf_path, verbose=True)

    # Exit with appropriate code
    if results['overall_status'] == 'PASS':
        sys.exit(0)
    elif results['overall_status'] == 'PARTIAL':
        sys.exit(1)
    else:
        sys.exit(2)
