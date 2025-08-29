---
title: Field Type Specifications - All 33 Types
type: note
permalink: access-form/fields/field-type-specifications-all-33-types-1
tags:
- '["field-types"'
- '"specifications"'
- '"complete"'
- '"all-33-types"'
- '"navigation"]'
---

# Field Type Specifications - All 33 Types

## Table of Contents

### Quick Navigation
- [Overview](#overview)
- [Field Categories](#field-categories)
- [Core Field Types (1-23)](#core-field-types)
  - [1. Text Field](#1-text-field-single-line)
  - [2. Text Area](#2-text-area-multi-line)
  - [3. Checkbox](#3-checkbox)
  - [4. Radio Button](#4-radio-button)
  - [5. Dropdown/Select](#5-dropdownselect)
  - [6. Date Field](#6-date-field)
  - [7. Time Field](#7-time-field)
  - [8. Email Field](#8-email-field)
  - [9. Phone Number](#9-phone-number-field)
  - [10. SSN Full](#10-ssn-field-full)
  - [11. SSN Partial](#11-ssn-field-partial)
  - [12. EIN Field](#12-ein-field)
  - [13. Signature Field](#13-signature-field)
  - [14. Initials Field](#14-initials-field)
  - [15. Numeric Field](#15-numeric-field)
  - [16. Currency Field](#16-currency-field)
  - [17. Percentage Field](#17-percentage-field)
  - [18. File Upload](#18-file-upload-field)
  - [19. Case Number](#19-case-number-field)
  - [20. Calculated Field](#20-calculated-field)
  - [21. Protected Field](#21-protected-field)
  - [22. Conditional Field](#22-conditional-field)
  - [23. Field Group Container](#23-field-group-container)
- [Advanced Field Types (24-33)](#advanced-field-types)
  - [24. Full Name (Composite)](#24-full-name-field-composite)
  - [25. Address (Composite)](#25-address-field-composite)
  - [26. URL Field](#26-url-field)
  - [27. Driver's License](#27-drivers-licensestate-id-field)
  - [28. Gender/Pronoun](#28-genderpronoun-field)
  - [29. Language Preference](#29-language-preference-field)
  - [30. Taxpayer ID (TIN)](#30-taxpayer-id-tin-field)
  - [31. Repeatable Section](#31-repeatable-section)
  - [32. Error Display](#32-error-display-field)
  - [33. Compliance Acknowledgment](#33-compliance-acknowledgment-field)
- [Complex Table Fields](#complex-table-field-specifications)
- [Implementation Notes](#implementation-notes)

---

## Overview

This document defines all **33 field types** supported by AccessForm, their properties, validation rules, and implementation requirements. Based on analysis of real Texas Workforce Commission (TWC) forms, these field types provide comprehensive coverage for government form conversion.

### Quick Stats
- **Total Field Types:** 33
- **Standard Fields:** 10 types
- **Government-Specific:** 12 types  
- **Composite Fields:** 2 types
- **Advanced Interactive:** 6 types
- **Inclusive Fields:** 2 types
- **Structural Elements:** 1 type

---

## Field Categories

### 1. Basic Input Fields
These are the fundamental building blocks found in all forms.

### 2. Government-Specific Fields
Specialized fields commonly found in government forms with specific formatting and validation requirements.

### 3. Complex Interactive Fields
Fields that involve conditional logic, calculations, or special behaviors.

### 4. Accessibility-Enhanced Fields
All fields must include accessibility features, but some require special handling.

---

## Core Field Types

### 1. Text Field (Single Line)
**Type ID:** `text`
**Description:** Standard single-line text input field

#### Properties
```javascript
{
  type: 'text',
  id: String,           // Unique field identifier
  name: String,         // Field name for form submission
  label: String,        // Display label
  placeholder: String,  // Placeholder text
  defaultValue: String, // Default value
  maxLength: Number,    // Maximum character length
  required: Boolean,    // Is field required
  readonly: Boolean,    // Is field read-only
  disabled: Boolean,    // Is field disabled
  tooltip: String,      // Help text/tooltip
  tabIndex: Number,     // Tab order
  position: {
    page: Number,
    x: Number,
    y: Number,
    width: Number,
    height: Number
  },
  validation: {
    pattern: RegExp,
    minLength: Number,
    maxLength: Number,
    customMessage: String
  },
  accessibility: {
    ariaLabel: String,
    ariaDescribedBy: String,
    ariaRequired: Boolean,
    screenReaderHint: String
  }
}
```

#### Validation Rules
- Character limit enforcement
- Pattern matching (if specified)
- Required field validation
- Trim whitespace on submission

#### AI Detection Hints
- Look for labels ending with colon
- Single underline or box in Word document
- Keywords: "Name", "Title", "ID", "Number"

[↑ Back to top](#table-of-contents)

---

### 2. Text Area (Multi-line)
**Type ID:** `textarea`
**Description:** Multi-line text input for longer responses

#### Properties
```javascript
{
  type: 'textarea',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  defaultValue: String,
  rows: Number,         // Visible rows
  cols: Number,         // Visible columns
  maxLength: Number,
  required: Boolean,
  readonly: Boolean,
  disabled: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  wordWrap: Boolean,    // Enable word wrapping
  resizable: Boolean,   // Allow user to resize
  validation: {
    minLength: Number,
    maxLength: Number,
    wordCount: {
      min: Number,
      max: Number
    }
  },
  accessibility: Object
}
```

#### Validation Rules
- Character and word count limits
- Required field validation
- Preserve line breaks and formatting

#### AI Detection Hints
- Large boxes or multiple lines in Word
- Keywords: "Description", "Comments", "Explain", "Details", "Notes"
- Multiple underscores or large rectangular areas

[↑ Back to top](#table-of-contents)

---

### 3. Checkbox
**Type ID:** `checkbox`
**Description:** Boolean selection field

#### Properties
```javascript
{
  type: 'checkbox',
  id: String,
  name: String,
  label: String,
  value: String,        // Value when checked
  defaultChecked: Boolean,
  required: Boolean,    // Must be checked
  disabled: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  groupName: String,    // For checkbox groups
  groupRole: String,    // 'independent' or 'group'
  conditionalLogic: {
    showFields: Array,  // Field IDs to show when checked
    hideFields: Array,  // Field IDs to hide when checked
    enableFields: Array,
    disableFields: Array
  },
  accessibility: Object
}
```

#### Validation Rules
- Required checkbox must be checked
- Group validation (min/max selections)
- Conditional field triggering

#### AI Detection Hints
- Square boxes (☐) in Word
- Keywords: "Check", "Select all that apply", "Yes", "No", "I agree"
- Multiple options with boxes

[↑ Back to top](#table-of-contents)

---

### 4. Radio Button
**Type ID:** `radio`
**Description:** Single selection from multiple options

#### Properties
```javascript
{
  type: 'radio',
  id: String,
  name: String,         // Same name for all in group
  label: String,
  value: String,        // Unique value for this option
  groupName: String,    // Radio button group identifier
  defaultChecked: Boolean,
  required: Boolean,    // Group is required
  disabled: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  options: Array,       // All options in the group
  orientation: String,  // 'horizontal' or 'vertical'
  conditionalLogic: Object,
  accessibility: {
    groupLabel: String, // Label for the entire group
    ariaLabel: String,
    ariaDescribedBy: String
  }
}
```

#### Validation Rules
- Exactly one selection per group
- Required group must have selection
- Value must be from defined options

#### AI Detection Hints
- Circle symbols (○) in Word
- Keywords: "Choose one", "Select one", mutually exclusive options
- Yes/No questions

[↑ Back to top](#table-of-contents)

---

### 5. Dropdown/Select
**Type ID:** `dropdown`
**Description:** Selection from a list of options

#### Properties
```javascript
{
  type: 'dropdown',
  id: String,
  name: String,
  label: String,
  placeholder: String,  // First option text
  required: Boolean,
  disabled: Boolean,
  multiple: Boolean,    // Allow multiple selections
  searchable: Boolean,  // Allow type-to-search
  tooltip: String,
  tabIndex: Number,
  position: Object,
  options: [
    {
      value: String,
      label: String,
      disabled: Boolean,
      group: String     // For option groups
    }
  ],
  defaultValue: String | Array,
  maxSelections: Number,
  validation: {
    required: Boolean,
    minSelections: Number,
    maxSelections: Number
  },
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 6. Date Field
**Type ID:** `date`
**Description:** Date input with calendar picker

#### Properties
```javascript
{
  type: 'date',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  format: String,       // 'MM/DD/YYYY', 'DD/MM/YYYY', etc.
  displayFormat: String,
  storageFormat: String, // ISO 8601
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  defaultValue: String | Date,
  validation: {
    minDate: String | Date,
    maxDate: String | Date,
    disabledDates: Array,
    disabledDays: Array, // [0-6] for days of week
    allowPastDates: Boolean,
    allowFutureDates: Boolean,
    customMessage: String
  },
  mask: String,         // Input mask
  calendarPicker: Boolean,
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 7. Time Field
**Type ID:** `time`
**Description:** Time input field

#### Properties
```javascript
{
  type: 'time',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  format: String,       // '12h' or '24h'
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  defaultValue: String,
  validation: {
    minTime: String,
    maxTime: String,
    step: Number,       // Minutes increment (15, 30, etc.)
    customMessage: String
  },
  showSeconds: Boolean,
  mask: String,
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 8. Email Field
**Type ID:** `email`
**Description:** Email address input with validation

#### Properties
```javascript
{
  type: 'email',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  defaultValue: String,
  validation: {
    pattern: RegExp,
    allowedDomains: Array, // ['texas.gov', 'twc.state.tx.us']
    blockedDomains: Array,
    requireGovernmentEmail: Boolean,
    customMessage: String
  },
  confirmEmail: Boolean, // Require confirmation field
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 9. Phone Number Field
**Type ID:** `phone`
**Description:** Phone number with formatting

#### Properties
```javascript
{
  type: 'phone',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  defaultValue: String,
  format: String,       // 'US', 'International'
  validation: {
    pattern: RegExp,
    countryCode: String,
    allowExtensions: Boolean,
    minDigits: Number,
    maxDigits: Number,
    customMessage: String
  },
  mask: String,         // '(999) 999-9999'
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 10. SSN Field (Full)
**Type ID:** `ssn_full`
**Description:** Full Social Security Number field (use with extreme caution)

#### Properties
```javascript
{
  type: 'ssn_full',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  secure: Boolean,      // Always mask display
  tooltip: String,
  tabIndex: Number,
  position: Object,
  validation: {
    pattern: /^\d{3}-\d{2}-\d{4}$/,
    customMessage: String
  },
  mask: String,         // '999-99-9999'
  security: {
    maskDisplay: true,  // Show as ***-**-1234
    encryptInPDF: true, // Encrypt field in PDF
    warningMessage: 'This field contains sensitive information'
  },
  accessibility: {
    ariaLabel: 'Social Security Number',
    screenReaderHint: 'Enter 9 digits with dashes'
  }
}
```

#### Security Notes
- **Use only when absolutely required by law**
- Consider using SSN_partial instead when possible
- Must encrypt field in PDF
- Should include user consent acknowledgment
- May require additional security compliance

[↑ Back to top](#table-of-contents)

---

### 11. SSN Field (Partial)
**Type ID:** `ssn_partial`
**Description:** Last 4 digits of SSN only (preferred over full SSN)

#### Properties
```javascript
{
  type: 'ssn_partial',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  secure: Boolean,      // Mask display
  tooltip: String,
  tabIndex: Number,
  position: Object,
  validation: {
    pattern: /^\d{4}$/,
    customMessage: String
  },
  mask: String,         // '9999'
  accessibility: {
    ariaLabel: 'Last 4 digits of Social Security Number',
    screenReaderHint: 'Enter only the last 4 digits'
  }
}
```

[↑ Back to top](#table-of-contents)

---

### 12. EIN Field
**Type ID:** `ein`
**Description:** Employer Identification Number

#### Properties
```javascript
{
  type: 'ein',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  validation: {
    pattern: /^\d{2}-\d{7}$/,
    customMessage: String
  },
  mask: String,         // '99-9999999'
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 13. Signature Field
**Type ID:** `signature`
**Description:** Digital or typed signature field

#### Properties
```javascript
{
  type: 'signature',
  id: String,
  name: String,
  label: String,
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  signatureType: String, // 'typed', 'drawn', 'digital'
  validation: {
    minLength: Number,  // For typed signatures
    requireFullName: Boolean,
    matchName: String,  // Field ID to match
    customMessage: String
  },
  appearance: {
    fontFamily: String, // For typed signatures
    fontSize: Number,
    italic: Boolean,
    showLine: Boolean,
    showDate: Boolean,
    dateFormat: String
  },
  bindingAgreement: String, // Legal text
  timestamp: Boolean,   // Add timestamp
  ipCapture: Boolean,   // Capture IP for audit
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 14. Initials Field
**Type ID:** `initials`
**Description:** Small text field for initials

#### Properties
```javascript
{
  type: 'initials',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  maxLength: Number,    // Usually 3-4
  validation: {
    pattern: /^[A-Z]{2,4}$/,
    minLength: 2,
    maxLength: 4,
    uppercase: Boolean,
    customMessage: String
  },
  appearance: {
    width: Number,      // Smaller than regular text
    height: Number,
    fontSize: Number
  },
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 15. Numeric Field
**Type ID:** `numeric`
**Description:** Number-only input field

#### Properties
```javascript
{
  type: 'numeric',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  defaultValue: Number,
  validation: {
    min: Number,
    max: Number,
    step: Number,
    integer: Boolean,   // Integer only
    decimal: Number,    // Decimal places
    negative: Boolean,  // Allow negative
    customMessage: String
  },
  format: {
    thousandsSeparator: String,
    decimalSeparator: String,
    prefix: String,     // '$', etc.
    suffix: String,     // '%', etc.
  },
  calculation: {       // For calculated fields
    formula: String,
    dependencies: Array // Field IDs
  },
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 16. Currency Field
**Type ID:** `currency`
**Description:** Monetary amount input

#### Properties
```javascript
{
  type: 'currency',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  defaultValue: Number,
  currencyCode: String, // 'USD'
  validation: {
    min: Number,
    max: Number,
    allowNegative: Boolean,
    customMessage: String
  },
  format: {
    symbol: String,     // '$'
    symbolPosition: String, // 'prefix' or 'suffix'
    thousandsSeparator: String,
    decimalSeparator: String,
    decimalPlaces: Number
  },
  calculation: Object,
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 17. Percentage Field
**Type ID:** `percentage`
**Description:** Percentage value input

#### Properties
```javascript
{
  type: 'percentage',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  defaultValue: Number,
  validation: {
    min: Number,        // Usually 0
    max: Number,        // Usually 100
    decimal: Number,
    customMessage: String
  },
  format: {
    displayAsDecimal: Boolean, // 0.5 vs 50%
    decimalPlaces: Number,
    showSymbol: Boolean
  },
  calculation: Object,
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 18. File Upload Field
**Type ID:** `file_upload`
**Description:** File attachment field

#### Properties
```javascript
{
  type: 'file_upload',
  id: String,
  name: String,
  label: String,
  required: Boolean,
  disabled: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  validation: {
    allowedTypes: Array, // ['.pdf', '.doc', '.docx']
    maxFileSize: Number, // In bytes
    maxFiles: Number,
    minFiles: Number,
    customMessage: String
  },
  multiple: Boolean,
  dragDrop: Boolean,
  preview: Boolean,
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 19. Case Number Field
**Type ID:** `case_number`
**Description:** Government case/reference number

#### Properties
```javascript
{
  type: 'case_number',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  required: Boolean,
  disabled: Boolean,
  readonly: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  format: String,       // Agency-specific format
  validation: {
    pattern: RegExp,
    length: Number,
    prefix: String,
    customMessage: String
  },
  mask: String,
  autoGenerate: Boolean, // System-generated
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 20. Calculated Field
**Type ID:** `calculated`
**Description:** Auto-calculated based on other fields

#### Properties
```javascript
{
  type: 'calculated',
  id: String,
  name: String,
  label: String,
  readonly: true,       // Always readonly
  tooltip: String,
  tabIndex: Number,
  position: Object,
  calculation: {
    formula: String,    // Mathematical expression
    dependencies: Array, // Field IDs used in calculation
    triggerOn: String,  // 'change', 'blur', 'submit'
    precision: Number,  // Decimal places
    roundingMethod: String // 'round', 'floor', 'ceil'
  },
  format: Object,       // Display formatting
  showCalculation: Boolean, // Show formula to user
  accessibility: {
    ariaLabel: String,
    ariaLive: 'polite', // Announce changes
    screenReaderFormula: String // Human-readable formula
  }
}
```

[↑ Back to top](#table-of-contents)

---

### 21. Protected Field
**Type ID:** `protected`
**Description:** Agency-use only field with restricted editing

#### Properties
```javascript
{
  type: 'protected',
  id: String,
  name: String,
  label: String,
  value: String,        // Pre-filled value or formula
  readonly: false,      // Can be edited by authorized users
  disabled: false,      // Still submittable
  tooltip: String,
  tabIndex: Number,
  position: Object,
  protection: {
    level: String,      // 'agency_only', 'admin_only', 'system'
    editableBy: Array,  // User roles that can edit
    password: Boolean,  // Require password to edit
    auditChanges: true  // Track all modifications
  },
  behavior: {
    // For authorized users
    whenAuthorized: {
      readonly: false,
      editable: true,
      showEditIcon: true
    },
    // For regular users
    whenUnauthorized: {
      readonly: true,
      editable: false,
      showLockIcon: true
    }
  },
  calculation: {       // Can be calculated field
    formula: String,
    dependencies: Array,
    autoUpdate: Boolean
  },
  styling: {
    backgroundColor: String, // Often grayed/blue tint
    borderStyle: String,
    fontSize: Number,
    authorizedStyle: Object, // Different style when editable
    unauthorizedStyle: Object
  },
  accessibility: {
    ariaLabel: String,
    announceProtection: true,
    skipInTabOrder: Boolean // Only for regular users
  }
}
```

[↑ Back to top](#table-of-contents)

---

### 22. Conditional Field
**Type ID:** `conditional`
**Description:** Field shown/hidden based on conditions

#### Properties
```javascript
{
  type: 'conditional',
  id: String,
  name: String,
  label: String,
  baseType: String,     // Underlying field type
  required: Boolean,    // When visible
  disabled: Boolean,
  tooltip: String,
  tabIndex: Number,
  position: Object,
  conditionalLogic: {
    conditions: [
      {
        field: String,  // Field ID to watch
        operator: String, // 'equals', 'contains', 'greater_than'
        value: Any,
        logicalOperator: String // 'AND', 'OR' for multiple
      }
    ],
    action: String,     // 'show', 'hide', 'enable', 'disable'
    defaultState: String // Initial state
  },
  animation: String,    // Transition effect
  accessibility: {
    announceVisibility: Boolean,
    ariaLive: 'polite'
  }
}
```

[↑ Back to top](#table-of-contents)

---

### 23. Field Group Container
**Type ID:** `field_group`
**Description:** Logical grouping of related fields

#### Properties
```javascript
{
  type: 'field_group',
  id: String,
  name: String,
  label: String,        // Group title
  description: String,  // Group instructions
  fields: Array,        // Child field IDs
  position: Object,
  layout: {
    type: String,       // 'vertical', 'horizontal', 'grid'
    columns: Number,
    spacing: Number,
    border: Boolean,
    backgroundColor: String
  },
  conditionalLogic: Object, // Apply to entire group
  validation: {
    requiredFields: Number, // Min required in group
    maxFields: Number,
    customMessage: String
  },
  accessibility: {
    role: 'group',
    ariaLabelledBy: String,
    groupDescription: String
  }
}
```

[↑ Back to top](#table-of-contents)

---

## Advanced Field Types

### 24. Full Name Field (Composite)
**Type ID:** `full_name`
**Description:** Structured name field with components

#### Properties
```javascript
{
  type: 'full_name',
  id: String,
  name: String,
  label: String,
  required: Boolean,
  components: {
    prefix: {
      enabled: Boolean,
      required: Boolean,
      options: ['Mr.', 'Ms.', 'Mrs.', 'Dr.', 'Prof.'],
      label: 'Title'
    },
    firstName: {
      enabled: true,
      required: Boolean,
      label: 'First Name',
      maxLength: Number
    },
    middleName: {
      enabled: Boolean,
      required: false,
      label: 'Middle Name/Initial',
      maxLength: Number
    },
    lastName: {
      enabled: true,
      required: Boolean,
      label: 'Last Name',
      maxLength: Number
    },
    suffix: {
      enabled: Boolean,
      required: false,
      options: ['Jr.', 'Sr.', 'II', 'III', 'IV'],
      label: 'Suffix'
    }
  },
  format: {
    display: String, // 'formal', 'informal', 'legal'
    order: String    // 'western', 'eastern'
  },
  validation: {
    allowNumbers: false,
    allowSpecialChars: ['-', "'", ' '],
    minLength: Number
  },
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 25. Address Field (Composite)
**Type ID:** `address`
**Description:** Structured address with validation

#### Properties
```javascript
{
  type: 'address',
  id: String,
  name: String,
  label: String,
  required: Boolean,
  components: {
    street1: {
      label: 'Street Address',
      required: Boolean,
      maxLength: 100
    },
    street2: {
      label: 'Apt/Suite/Unit',
      required: false,
      maxLength: 50
    },
    city: {
      label: 'City',
      required: Boolean,
      maxLength: 50
    },
    state: {
      label: 'State/Province',
      required: Boolean,
      type: 'dropdown',
      options: [] // Populated based on country
    },
    postalCode: {
      label: 'ZIP/Postal Code',
      required: Boolean,
      pattern: RegExp, // Based on country
      mask: String
    },
    country: {
      label: 'Country',
      required: Boolean,
      type: 'dropdown',
      default: 'US'
    }
  },
  validation: {
    validatePostalCode: Boolean,
    validateCity: Boolean,
    autocomplete: Boolean
  },
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 26. URL Field
**Type ID:** `url`
**Description:** Web address with validation

#### Properties
```javascript
{
  type: 'url',
  id: String,
  name: String,
  label: String,
  placeholder: String,
  required: Boolean,
  validation: {
    requireProtocol: Boolean, // https://
    allowedProtocols: ['http', 'https', 'ftp'],
    validateDomain: Boolean,
    customMessage: String
  },
  autocomplete: 'url',
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 27. Driver's License/State ID Field
**Type ID:** `drivers_license`
**Description:** State-specific ID validation

#### Properties
```javascript
{
  type: 'drivers_license',
  id: String,
  name: String,
  label: String,
  required: Boolean,
  state: String, // Two-letter state code
  validation: {
    pattern: RegExp, // State-specific pattern
    length: Number,  // State-specific length
    customMessage: String
  },
  mask: String,
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 28. Gender/Pronoun Field
**Type ID:** `gender_pronoun`
**Description:** Inclusive gender and pronoun selection

#### Properties
```javascript
{
  type: 'gender_pronoun',
  id: String,
  name: String,
  label: String,
  required: Boolean,
  allowCustom: Boolean,
  options: {
    gender: [
      'Male',
      'Female', 
      'Non-binary',
      'Prefer not to say',
      'Prefer to self-describe'
    ],
    pronouns: [
      'he/him',
      'she/her',
      'they/them',
      'ze/zir',
      'Prefer not to say',
      'Other'
    ]
  },
  customField: {
    enabled: Boolean,
    label: 'Please specify',
    maxLength: 50
  },
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 29. Language Preference Field
**Type ID:** `language_preference`
**Description:** User's preferred language

#### Properties
```javascript
{
  type: 'language_preference',
  id: String,
  name: String,
  label: String,
  required: Boolean,
  options: [
    { code: 'en', label: 'English' },
    { code: 'es', label: 'Spanish' },
    { code: 'zh', label: 'Chinese' },
    { code: 'fr', label: 'French' },
    // ... more languages
  ],
  multiple: Boolean, // Allow multiple selections
  includeDialects: Boolean,
  defaultValue: 'en',
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 30. Taxpayer ID (TIN) Field
**Type ID:** `tin`
**Description:** Various taxpayer identification numbers

#### Properties
```javascript
{
  type: 'tin',
  id: String,
  name: String,
  label: String,
  required: Boolean,
  tinType: String, // 'SSN', 'EIN', 'ITIN', 'ATIN'
  validation: {
    pattern: RegExp,
    checksum: Boolean, // Validate checksum if applicable
    customMessage: String
  },
  mask: String,
  secure: Boolean,
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

### 31. Repeatable Section
**Type ID:** `repeatable_section`
**Description:** Dynamic sections that can be added/removed

#### Properties
```javascript
{
  type: 'repeatable_section',
  id: String,
  name: String,
  label: String,
  minInstances: Number,
  maxInstances: Number,
  template: {
    fields: Array, // Field definitions to repeat
    layout: Object
  },
  controls: {
    addButton: {
      label: 'Add Another',
      icon: String
    },
    removeButton: {
      label: 'Remove',
      icon: String,
      confirmRemoval: Boolean
    }
  },
  accessibility: {
    announceChanges: true,
    groupLabel: String
  }
}
```

[↑ Back to top](#table-of-contents)

---

### 32. Error Display Field
**Type ID:** `error_display`
**Description:** Dedicated error message display area

#### Properties
```javascript
{
  type: 'error_display',
  id: String,
  position: String, // 'inline', 'top', 'bottom'
  displayType: String, // 'summary', 'inline', 'both'
  errors: Array,
  styling: {
    icon: Boolean,
    backgroundColor: String,
    textColor: String,
    border: String
  },
  accessibility: {
    role: 'alert',
    ariaLive: 'assertive',
    focusOnError: Boolean
  }
}
```

[↑ Back to top](#table-of-contents)

---

### 33. Compliance Acknowledgment Field
**Type ID:** `compliance_acknowledgment`
**Description:** Legal/compliance checkbox with enhanced tracking

#### Properties
```javascript
{
  type: 'compliance_acknowledgment',
  id: String,
  name: String,
  label: String,
  required: true, // Always required
  agreementText: String, // Full legal text
  agreementLink: String, // Link to full terms
  tracking: {
    timestamp: Boolean,
    ipAddress: Boolean,
    userAgent: Boolean
  },
  validation: {
    mustCheck: true,
    customMessage: 'You must acknowledge to proceed'
  },
  styling: {
    emphasis: Boolean, // Bold/highlight
    border: Boolean
  },
  accessibility: Object
}
```

[↑ Back to top](#table-of-contents)

---

## Complex Table Field Specifications

### Table with Form Fields
**Type ID:** `form_table`
**Description:** Table structure containing form fields

#### Properties
```javascript
{
  type: 'form_table',
  id: String,
  name: String,
  label: String,        // Table title
  position: Object,
  structure: {
    rows: Number,
    columns: Number,
    headerRows: Array,  // Row indices that are headers
    headerColumns: Array,
    mergedCells: [
      {
        startRow: Number,
        startCol: Number,
        rowSpan: Number,
        colSpan: Number
      }
    ]
  },
  cells: [
    {
      row: Number,
      column: Number,
      type: String,     // 'label', 'field', 'empty'
      content: Any,     // Field definition or text
      styling: Object
    }
  ],
  repeatableRows: {
    enabled: Boolean,
    startRow: Number,
    endRow: Number,
    minRepeat: Number,
    maxRepeat: Number,
    addButtonLabel: String,
    removeButtonLabel: String
  },
  accessibility: {
    summary: String,
    captionText: String,
    headerAssociations: Object
  }
}
```

[↑ Back to top](#table-of-contents)

---

## Implementation Notes

### Field Detection AI Prompts

#### Prompt Template for Field Detection

```text
Analyze this document section and identify form fields:

Document Text: [extracted text]
Visual Layout: [layout description]

For each detected field, provide:
1. Field type (from our supported types)
2. Field label
3. Required/optional status
4. Any validation hints
5. Conditional relationships
6. Position in document structure
7. Confidence score (0-1)

Return as structured JSON matching our field schema.
```

#### Confidence Scoring

```javascript
const CONFIDENCE_THRESHOLDS = {
  high: 0.9,      // Automatic processing
  medium: 0.7,    // Process with warning
  low: 0.5,       // Requires user confirmation
  reject: 0.5     // Below this, ask user
};
```

### Testing Requirements

Each field type must be tested for:

1. **Functional Testing**
   - Data entry
   - Validation
   - Default values
   - Required field behavior
   - Conditional logic

2. **Accessibility Testing**
   - Screen reader compatibility
   - Keyboard navigation
   - ARIA attribute correctness
   - Focus management
   - Error announcement

3. **Cross-browser Testing**
   - Chrome/Edge 90+
   - Firefox 90+
   - Safari 14+
   - PDF reader compatibility

4. **Performance Testing**
   - Large forms (500+ fields)
   - Complex tables
   - Conditional logic chains
   - Calculation performance

5. **Government Compliance**
   - Section 508
   - WCAG 2.1 AA
   - PDF/UA
   - Agency-specific requirements

### Error Handling

#### Field-Level Error States

```javascript
const ERROR_STATES = {
  INVALID_FORMAT: {
    severity: 'error',
    message: 'Invalid format',
    action: 'highlight',
    recovery: 'show_format_hint'
  },
  
  REQUIRED_MISSING: {
    severity: 'error',
    message: 'This field is required',
    action: 'focus',
    recovery: 'prevent_submission'
  },
  
  RANGE_ERROR: {
    severity: 'warning',
    message: 'Value outside expected range',
    action: 'highlight',
    recovery: 'allow_with_confirmation'
  },
  
  DEPENDENCY_ERROR: {
    severity: 'info',
    message: 'Related field needs attention',
    action: 'show_relationship',
    recovery: 'guide_to_dependency'
  }
};
```

### Key Implementation Guidelines

1. **Progressive Enhancement**: Start with basic HTML5 input types, enhance with JavaScript
2. **Fallback Strategy**: Always provide text field fallback for unsupported types
3. **Memory Management**: Lazy load complex fields, virtualize large tables
4. **Batch Processing**: Process fields in chunks for large forms
5. **User Communication**: Always explain what's happening during processing
6. **Review Interface**: Highlight any fields needing review with clear visual indicators

---

*Document Version: 2.0*
*Last Updated: August 8, 2025*
*Status: Complete and Ready for Implementation*

[↑ Back to top](#table-of-contents)