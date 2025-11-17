#!/usr/bin/env python3
"""
Apply fixes to PDFs:
1. Remove empty Form elements from structure tree (7.18.4-2)
2. Wrap unmarked XObject references in Artifact markers (7.1-3)
"""

import sys
import os
from PyPDF2 import PdfReader, PdfWriter
from PyPDF2.generic import DictionaryObject, ArrayObject, IndirectObject, StreamObject


def fix_empty_form_elements(input_path, output_path):
    """Remove empty Form elements from structure tree"""
    print(f"\n{'='*80}")
    print(f"Fixing empty Form elements in: {input_path}")
    print(f"{'='*80}\n")

    with open(input_path, 'rb') as f:
        reader = PdfReader(f)
        writer = PdfWriter()

        # Copy all pages
        for page in reader.pages:
            writer.add_page(page)

        # Get the catalog
        catalog = reader.trailer['/Root']

        if '/StructTreeRoot' not in catalog:
            print("❌ No structure tree root found")
            return False

        struct_tree = catalog['/StructTreeRoot']

        # Recursively process structure tree
        removed_count = [0]  # Use list to allow modification in nested function
        if '/K' in struct_tree:
            kids = struct_tree['/K']
            if isinstance(kids, IndirectObject):
                kids = reader.get_object(kids)

            if isinstance(kids, ArrayObject):
                for kid in kids:
                    if isinstance(kid, IndirectObject):
                        kid = reader.get_object(kid)
                    process_structure_element(kid, reader, removed_count)
            else:
                process_structure_element(kids, reader, removed_count)

        # Write output
        with open(output_path, 'wb') as out_f:
            writer.write(out_f)

        print(f"\n✅ Fixed {removed_count[0]} empty Form elements")
        print(f"📄 Output written to: {output_path}\n")
        return True


def process_structure_element(elem, reader, removed_count):
    """Recursively process structure elements and remove empty Forms"""
    if not isinstance(elem, DictionaryObject):
        return

    if isinstance(elem, IndirectObject):
        elem = reader.get_object(elem)

    # Process children first
    if '/K' in elem:
        kids = elem['/K']
        if isinstance(kids, IndirectObject):
            kids = reader.get_object(kids)

        if isinstance(kids, ArrayObject):
            # Process children and remove empty Forms
            new_kids = ArrayObject()
            for kid in kids:
                if isinstance(kid, IndirectObject):
                    kid_obj = reader.get_object(kid)
                else:
                    kid_obj = kid

                # Check if this kid should be removed
                if should_remove_element(kid_obj, reader):
                    removed_count[0] += 1
                    print(f"🗑️  Removed empty Form element: {kid}")
                else:
                    new_kids.append(kid)
                    # Recursively process this kid
                    if isinstance(kid_obj, DictionaryObject):
                        process_structure_element(kid_obj, reader, removed_count)

            # Update kids array if we removed any
            if len(new_kids) != len(kids):
                elem['/K'] = new_kids
        else:
            # Single kid
            if isinstance(kids, IndirectObject):
                kid_obj = reader.get_object(kids)
            else:
                kid_obj = kids

            if should_remove_element(kid_obj, reader):
                removed_count[0] += 1
                print(f"🗑️  Removed empty Form element")
                # Remove the /K entry
                del elem['/K']
            else:
                # Recursively process
                if isinstance(kid_obj, DictionaryObject):
                    process_structure_element(kid_obj, reader, removed_count)


def should_remove_element(elem, reader):
    """Check if element should be removed"""
    if not isinstance(elem, DictionaryObject):
        return False

    if isinstance(elem, IndirectObject):
        elem = reader.get_object(elem)

    # Check if this is a Form element
    if '/S' in elem:
        struct_type = elem['/S']
        if isinstance(struct_type, IndirectObject):
            struct_type = reader.get_object(struct_type)

        if str(struct_type).replace('/', '') == 'Form':
            # Check if it has Role attribute
            has_role = '/A' in elem and isinstance(elem['/A'], DictionaryObject)

            # Check if it has children
            has_children = '/K' in elem

            # Remove if no Role and no children
            if not has_role and not has_children:
                return True

    return False


def fix_unmarked_xobjects(input_path, output_path):
    """Wrap unmarked XObject references in Artifact markers"""
    print(f"\n{'='*80}")
    print(f"Fixing unmarked XObject content in: {input_path}")
    print(f"{'='*80}\n")

    with open(input_path, 'rb') as f:
        reader = PdfReader(f)
        writer = PdfWriter()

        fixed_pages = 0

        # Process each page
        for page_num, page in enumerate(reader.pages, 1):
            if '/Contents' in page:
                contents = page['/Contents']
                if isinstance(contents, IndirectObject):
                    contents = reader.get_object(contents)

                # Get content stream
                try:
                    if hasattr(contents, 'get_data'):
                        content_data = contents.get_data()
                        content_string = content_data.decode('latin-1')

                        # Check if page has XObject references that aren't marked
                        if has_unmarked_xobjects(content_string):
                            print(f"Page {page_num}: Found unmarked XObjects")
                            new_content = wrap_xobjects_in_artifacts(content_string)

                            # Create new content stream
                            new_stream = StreamObject()
                            new_stream._data = new_content.encode('latin-1')
                            page['/Contents'] = new_stream
                            fixed_pages += 1
                    elif isinstance(contents, ArrayObject):
                        # Multiple content streams
                        print(f"Page {page_num}: Multiple content streams (advanced fix needed)")
                except Exception as e:
                    print(f"Page {page_num}: Error processing - {e}")

            writer.add_page(page)

        # Write output
        with open(output_path, 'wb') as out_f:
            writer.write(out_f)

        print(f"\n✅ Fixed {fixed_pages} pages with unmarked XObjects")
        print(f"📄 Output written to: {output_path}\n")
        return True


def has_unmarked_xobjects(content):
    """Check if content has unmarked XObject references"""
    has_do = ' Do' in content or '\nDo' in content
    has_xobject = '/Fm' in content or '/Im' in content

    if not (has_do and has_xobject):
        return False

    # Check if already marked
    has_markers = '/Artifact BMC' in content or 'BMC' in content or 'BDC' in content
    return not has_markers


def wrap_xobjects_in_artifacts(content):
    """Wrap XObject Do operators in Artifact markers"""
    lines = content.split('\n')
    result = []

    for line in lines:
        stripped = line.strip()

        # Check if line contains XObject Do operator
        if stripped.endswith(' Do') and ('/' in stripped):
            # Wrap in Artifact markers
            result.append('/Artifact BMC')
            result.append(line)
            result.append('EMC')
        else:
            result.append(line)

    return '\n'.join(result)


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: python apply_pdf_fixes.py <mode> <input_pdf> <output_pdf>")
        print("Modes: empty-forms, unmarked-xobjects")
        sys.exit(1)

    mode = sys.argv[1]
    input_path = sys.argv[2]
    output_path = sys.argv[3]

    if not os.path.exists(input_path):
        print(f"❌ Input file not found: {input_path}")
        sys.exit(1)

    if mode == 'empty-forms':
        fix_empty_form_elements(input_path, output_path)
    elif mode == 'unmarked-xobjects':
        fix_unmarked_xobjects(input_path, output_path)
    else:
        print(f"❌ Unknown mode: {mode}")
        sys.exit(1)
