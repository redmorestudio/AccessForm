---
title: AccessForm Product Requirements Document
type: note
permalink: product-requirements/access-form-product-requirements-document
tags:
- '["prd"'
- '"product-requirements"'
- '"accessform"'
- '"accessibility"'
- '"pdf-conversion"'
- '"government"]'
---

## Supported Field Types

AccessForm supports **33 distinct field types** organized into categories:

### Standard Fields (10 types)
Text, Text Area, Number, Dropdown, Radio Button, Checkbox, Date, Time, Email, Phone

### Government-Specific Fields (12 types)
- **Identification**: SSN (Full/Partial), EIN, TIN, Driver's License, Case Number
- **Verification**: Initials, Signature (digital/typed), Compliance Acknowledgment
- **Financial**: Currency, Percentage
- **Access Control**: Protected/Agency-Only fields

### Composite Fields (2 types)
- **Full Name**: Structured with prefix, first, middle, last, suffix
- **Address**: Street, city, state, ZIP with validation

### Advanced Interactive Fields (6 types)
- **Calculated Fields**: Auto-computed values based on formulas
- **Conditional Fields**: Show/hide based on other field values
- **File Upload**: Document attachments
- **URL Fields**: Web address validation
- **Repeatable Sections**: Dynamic add/remove sections
- **Error Display**: Dedicated error message areas

### Inclusive Fields (2 types)
- **Gender/Pronoun Selection**: Customizable options
- **Language Preference**: Multi-language support

### Structural Elements (1 type)
- **Form Tables**: Complex table structures with nested fields

For complete field specifications including properties, validation rules, and accessibility requirements, see: `access-form/specs/field-type-specifications`

---
### Actual TWC Form Scale Analysis

Based on detailed analysis of real TWC VR forms, we discovered:

#### Real-World Field Counts
- **VR3125 (Entering the World of Work)**: ~270 fields
- **VR3124 (Soft Skills to Pay the Bills)**: ~290 fields  
- **VR3133 (Money Smart Financial Education)**: ~300 fields
- **VR1838 (Situational Assessment & Work Sample)**: ~650 fields

**Critical Finding**: Most TWC forms EXCEED the original 200-field estimate. The system must handle 300-650+ fields as normal operation, not edge cases.

#### Complex Form Elements at Scale
- **Attendance tracking tables**: 21 rows × 5 columns = 105 fields alone
- **Group participant lists**: Up to 18 customers + 3 instructors
- **Assessment matrices**: 30+ items × 4-5 rating columns
- **Multiple signature blocks**: 6-10 signature fields per form

#### Implications for Design
1. The 1000-field limit provides adequate headroom for even the largest forms
2. Graceful degradation is essential for normal operation, not just edge cases
3. Users expect and accept longer processing times for government forms
4. Clear progress communication is more important than speed
# AccessForm Product Requirements Document (PRD)

## Executive Summary

AccessForm is a browser-based application that converts Microsoft Word documents into accessible, compliant PDF forms for government use. The product operates entirely within the user's browser, ensuring data security and eliminating installation barriers while serving both technical consultants and non-technical government employees.

### Key Innovations
- **33 Comprehensive Field Types**: Full support for standard, government-specific, and advanced form fields
- **Comprehensive Accessibility Compliance**: Exceeds current requirements with WCAG 2.1 AA and Section 508 compliance
- **Multi-Form Learning System**: Discovers patterns across form sets to improve accuracy
- **AI-Enhanced Processing**: Intelligent field detection without storing client data
- **1000-Field Capacity**: Handles extremely large government forms with graceful performance
- **Dual-Mode Interface**: Simple wizard for non-technical users, advanced mode for power users

### Accessibility Leadership
AccessForm sets the standard for government form accessibility by implementing:
- **WCAG 2.1 AA Compliance**: Full implementation of all 50 success criteria across four principles (Perceivable, Operable, Understandable, Robust)
- **Section 508 Compliance**: Meets and exceeds federal accessibility requirements for government forms
- **PDF/UA Specification**: Universal accessibility standard for maximum assistive technology compatibility
- **Future-Proofing**: Ready for upcoming government requirements (state/local WCAG 2.1 AA by April 2026-2027)
- **Comprehensive Testing**: Multi-layer validation including automated tools, manual testing, and user validation with disabled government employees

### Technical Documentation
- **Field Specifications**: `access-form/specs/field-type-specifications`
- **Data Flow Architecture**: `access-form/specs/data-flow-architecture`
- **Accessibility Compliance**: `access-form/compliance/accessibility-compliance-integration-summary`

## Product Vision

To democratize the creation of accessible government forms by providing a secure, user-friendly tool that transforms Word documents into compliant PDF forms without requiring technical expertise or compromising data security.

## Problem Statement

Government agencies face increasing pressure to create accessible PDF forms that comply with evolving accessibility standards. With new legal requirements taking effect (state/local governments must meet WCAG 2.1 AA by April 2026-2027), current solutions fall short:

- **Expensive desktop software** (Adobe Acrobat) with steep learning curves and high licensing costs
- **Manual accessibility processes** that are time-consuming, error-prone, and require specialized expertise
- **Outdated compliance standards** - many tools still target WCAG 2.0 while regulations move to WCAG 2.1 AA
- **Security concerns** with cloud-based solutions that transmit sensitive government data to external servers
- **Technical expertise gaps** - most government employees lack the specialized knowledge for accessibility compliance
- **Procurement challenges** - insufficient compliance documentation (VPAT, accessibility statements) for government contracting
- **Testing complexity** - ensuring compatibility with government-standard assistive technologies (NVDA, JAWS)

The accessibility compliance landscape is rapidly evolving, and government agencies need solutions that not only meet current requirements but are ready for upcoming mandates.

## Target Market

### Primary Users
1. **Government Consultants/Developers**: Technical professionals who provide form conversion services to government agencies
2. **Government Employees**: Non-technical staff responsible for form management and citizen services

### Secondary Users
- State and local government IT departments
- Federal agency form administrators  
- **Accessibility compliance officers** and Section 508 coordinators
- **Government procurement specialists** requiring VPAT documentation
- **Disability advocacy organizations** working with government agencies
- **Consulting firms** specializing in government accessibility compliance

## Core Value Propositions

1. **Zero Installation Barrier**: Works in any modern browser without IT approval
2. **Leading Accessibility Compliance**: Exceeds current requirements with WCAG 2.1 AA, Section 508, and PDF/UA compliance
3. **AI-Enhanced Processing**: Leverages external AI services for intelligent form field detection and optimization
4. **Dual-Mode Interface**: Serves both power users and non-technical users with persona-driven design
5. **Future-Proof Compliance**: Ready for upcoming government accessibility requirements (2025-2027 deadlines)
6. **No Client Data Storage**: Never stores customer/citizen data - processes blank forms only
7. **Comprehensive Validation**: Detailed accessibility and compliance reports with automated testing integration
8. **Procurement Ready**: Complete VPAT documentation and compliance reporting for government contracting

[CONTINUING WITH FULL PRD...]

---

**Note**: This is an executive summary extract. For the complete Product Requirements Document including detailed technical specifications, accessibility compliance requirements, user interface designs, and MVP development checklist, see: [AccessForm Product Requirements Document - Complete](product-requirements/access-form-product-requirements-document-1)