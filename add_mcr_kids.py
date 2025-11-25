#!/usr/bin/env python3
"""
Pikepdf script to manually add MCR (Marked Content Reference) kids to structure elements.
This links structure elements to actual page content via MCIDs (Marked Content Identifiers).
"""
import sys
import pikepdf
from collections import defaultdict

def extract_mcids_from_content_streams(pdf):
    """
    Scan all page content streams and extract MCID assignments.
    Returns: dict mapping (page_index, mcid) -> True
    """
    mcids_found = defaultdict(set)

    for page_idx, page in enumerate(pdf.pages):
        try:
            # Get content stream as string
            content = page.obj.get('/Contents')
            if content is None:
                continue

            # Handle both single content stream and array of streams
            if isinstance(content, pikepdf.Array):
                streams = content
            else:
                streams = [content]

            # Extract text from each stream
            for stream_obj in streams:
                if not hasattr(stream_obj, 'read_bytes'):
                    continue

                content_bytes = stream_obj.read_bytes()
                content_str = content_bytes.decode('latin-1', errors='ignore')

                # Look for MCID patterns: "/MCID <number> BDC"
                import re
                mcid_pattern = r'/MCID\s+(\d+)\s+BDC'
                for match in re.finditer(mcid_pattern, content_str):
                    mcid = int(match.group(1))
                    mcids_found[page_idx].add(mcid)

        except Exception as e:
            print(f"⚠️  Error scanning page {page_idx}: {e}")
            continue

    return mcids_found

def add_mcr_kids_to_structure(pdf_path, output_path, verbose=True):
    """
    Add MCR kids to structure elements to link them to content.

    Strategy:
    1. Open PDF with pikepdf
    2. Check if StructTreeRoot exists and has structure elements
    3. Scan content streams to find available MCIDs
    4. For each structure element, add MCR dictionaries linking to MCIDs
    5. Save modified PDF
    """

    results = {
        'structure_elements_found': 0,
        'mcr_kids_added': 0,
        'mcids_available': 0,
        'status': 'FAIL'
    }

    try:
        pdf = pikepdf.open(pdf_path)

        # Check 1: StructTreeRoot exists
        if not hasattr(pdf.Root, 'StructTreeRoot'):
            if verbose:
                print("❌ No StructTreeRoot found - cannot add MCR kids")
            return results

        root = pdf.Root.StructTreeRoot

        # Check 2: Root has /K (kids)
        if '/K' not in root:
            if verbose:
                print("❌ StructTreeRoot has no /K (kids) - structure tree is empty")
            return results

        if verbose:
            print("✅ Found StructTreeRoot with kids")

        # Extract available MCIDs from content streams
        mcids_by_page = extract_mcids_from_content_streams(pdf)
        total_mcids = sum(len(mcids) for mcids in mcids_by_page.values())
        results['mcids_available'] = total_mcids

        if verbose:
            print(f"📊 Found {total_mcids} MCID markers across {len(mcids_by_page)} pages:")
            for page_idx in sorted(mcids_by_page.keys()):
                mcids = sorted(mcids_by_page[page_idx])
                print(f"   Page {page_idx}: MCIDs {mcids}")

        if total_mcids == 0:
            if verbose:
                print("⚠️  No MCID markers found in content streams - nothing to link to")
            return results

        # Walk structure tree and add MCR kids
        mcid_usage = {}  # Track which MCIDs we've assigned

        def add_mcr_to_element(elem, page_idx, mcid):
            """Helper to add a single MCR kid to an element."""
            try:
                # Create MCR dictionary
                mcr_dict = pikepdf.Dictionary(
                    Type=pikepdf.Name('/MCR'),
                    Pg=pdf.pages[page_idx].obj,
                    MCID=mcid
                )

                # Add to element's /K array
                has_k = False
                try:
                    has_k = '/K' in elem
                except (ValueError, TypeError):
                    pass

                if has_k:
                    # Element already has kids - append
                    k = elem['/K']
                    if isinstance(k, pikepdf.Array):
                        k.append(mcr_dict)
                    else:
                        # Single kid - convert to array
                        elem[pikepdf.Name('/K')] = pikepdf.Array([k, mcr_dict])
                else:
                    # No kids yet - create array
                    try:
                        elem[pikepdf.Name('/K')] = pikepdf.Array([mcr_dict])
                    except (ValueError, TypeError) as e2:
                        # Try alternate approach - directly set K attribute
                        elem.K = pikepdf.Array([mcr_dict])

                return True
            except Exception as e:
                if verbose:
                    print(f"⚠️  Error adding MCR to element: {e}")
                    import traceback
                    traceback.print_exc()
                return False

        def walk_and_link(elem, depth=0):
            """Recursively walk structure tree and add MCR kids."""
            results['structure_elements_found'] += 1

            # Get element tag
            tag = None
            if hasattr(elem, 'get') and '/S' in elem:
                tag = str(elem['/S']).replace('/', '')

            # Try to find an unused MCID to link this element to
            # Simple strategy: assign MCIDs sequentially from page 0
            for page_idx in sorted(mcids_by_page.keys()):
                for mcid in sorted(mcids_by_page[page_idx]):
                    key = (page_idx, mcid)
                    if key not in mcid_usage:
                        # Found unused MCID - link it to this element
                        if add_mcr_to_element(elem, page_idx, mcid):
                            mcid_usage[key] = tag or 'Unknown'
                            results['mcr_kids_added'] += 1
                            if verbose and depth < 3:  # Only log top-level elements
                                indent = "  " * depth
                                print(f"{indent}✅ Linked {tag or 'Element'} to Page {page_idx} MCID {mcid}")
                        return  # Only link one MCID per element for now

            # Recursively process structure element kids (if any)
            if hasattr(elem, 'get') and '/K' in elem:
                k = elem['/K']
                if hasattr(k, '__iter__') and not isinstance(k, str):
                    kids = list(k)
                else:
                    kids = [k]

                for kid in kids:
                    if not hasattr(kid, 'get'):
                        continue
                    # Only recurse into structure elements, not MCRs
                    kid_type = kid.get('/Type')
                    if kid_type and str(kid_type) == '/StructElem':
                        walk_and_link(kid, depth + 1)

        # Start walking from root's kids
        k = root['/K']
        # Convert pikepdf Array to Python list
        if hasattr(k, '__iter__') and not isinstance(k, str):
            kids = list(k)
        else:
            kids = [k]

        for kid in kids:
            if hasattr(kid, 'get'):
                kid_type = kid.get('/Type')
                if kid_type and str(kid_type) == '/StructElem':
                    walk_and_link(kid)

        # Save modified PDF
        pdf.save(output_path)
        pdf.close()

        if verbose:
            print(f"\n{'='*70}")
            print(f"✅ Added {results['mcr_kids_added']} MCR kids to {results['structure_elements_found']} structure elements")
            print(f"📄 Saved to: {output_path}")
            print(f"{'='*70}")

        results['status'] = 'SUCCESS' if results['mcr_kids_added'] > 0 else 'PARTIAL'

    except Exception as e:
        if verbose:
            print(f"\n❌ ERROR: {e}")
            import traceback
            traceback.print_exc()
        results['status'] = 'ERROR'

    return results

if __name__ == '__main__':
    if len(sys.argv) < 3:
        print("Usage: python3 add_mcr_kids.py <input_pdf> <output_pdf>")
        sys.exit(1)

    input_path = sys.argv[1]
    output_path = sys.argv[2]

    print(f"\n🔧 Adding MCR kids to structure tree...")
    print(f"Input:  {input_path}")
    print(f"Output: {output_path}")
    print("=" * 70)

    results = add_mcr_kids_to_structure(input_path, output_path, verbose=True)

    # Exit with appropriate code
    if results['status'] == 'SUCCESS':
        sys.exit(0)
    elif results['status'] == 'PARTIAL':
        sys.exit(1)
    else:
        sys.exit(2)
