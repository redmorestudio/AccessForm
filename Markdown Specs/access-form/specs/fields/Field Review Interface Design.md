---
title: Field Review Interface Design
type: note
permalink: access-form/fields/field-review-interface-design-1
tags:
- '["ui"'
- '"field-review"'
- '"interface-design"'
- '"user-experience"'
- '"templates"]'
---

# Field Review Interface Design

## Reality Check
- **No server state**: Each session is independent, no "recent conversions" across sessions
- **Local storage only**: Can save templates/preferences locally in browser
- **Single file processing**: No batch in MVP
- **Templates**: Need to define what these actually are

## What Are Templates?

Templates are **pre-configured field detection rules** stored locally that help the system recognize common patterns. For example:

### Default Field Templates (Built-in)
```javascript
const DEFAULT_FIELD_TEMPLATES = {
  // Common government field patterns
  'ssn_last_4': {
    patterns: ['last 4', 'ssn (last four)', 'last 4 digits of ssn'],
    fieldType: 'ssn_partial',
    validation: 'exactly 4 digits',
    mask: '9999'
  },
  
  'case_number': {
    patterns: ['case number', 'case #', 'reference number', 'claim number'],
    fieldType: 'case_number',
    validation: 'alphanumeric',
    commonFormats: ['TX-YYYY-######', 'VR####', 'CASE-####-YY']
  },
  
  'government_email': {
    patterns: ['email', 'e-mail', 'email address'],
    fieldType: 'email',
    validation: 'must end in .gov or .state.tx.us',
    whenGovernmentForm: true
  }
}
```

### User-Created Templates (Saved Locally)
After processing forms, users can save patterns they've corrected for future use.

## Field Review Interface - By Field Type

### 1. Text Field Review
```
┌─────────────────────────────────────────────────────────┐
│ Field Review: Text Field                               │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ Found on: Page 3, Line 12                             │
│ Context: "Applicant Name: ________________"           │
│                                                         │
│ Field Settings:                                        │
│ ┌─────────────────────────────────────────┐          │
│ │ Label: [Applicant Name               ]  │          │
│ │ Type:  [Text Field                  ▼]  │          │
│ │                                         │          │
│ │ ☑ Required field                        │          │
│ │ Max length: [50                     ]   │          │
│ │                                         │          │
│ │ Should this be a special field type?    │          │
│ │ ○ Keep as text                          │          │
│ │ ○ Full name (split into parts)         │          │
│ │ ○ Case number                          │          │
│ └─────────────────────────────────────────┘          │
│                                                         │
│ [Apply to Similar] [Skip] [Confirm]                   │
└─────────────────────────────────────────────────────────┘
```

### 2. Checkbox Group Review
```
┌─────────────────────────────────────────────────────────┐
│ Field Review: Checkbox Group                           │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ Found on: Page 5                                       │
│ Context:                                                │
│ "Select all that apply:                                │
│  ☐ Accommodation needed                                │
│  ☐ Interpreter required                                │
│  ☐ Wheelchair accessible                               │
│  ☐ Large print materials"                              │
│                                                         │
│ Group Settings:                                        │
│ ┌─────────────────────────────────────────┐          │
│ │ Group name: [Accessibility Needs     ]  │          │
│ │                                         │          │
│ │ Selection rules:                        │          │
│ │ ○ Any number (0 or more)               │          │
│ │ ○ At least one required                │          │
│ │ ○ Exactly one required                 │          │
│ │                                         │          │
│ │ Individual checkboxes:                  │          │
│ │ ☑ Accommodation needed                  │          │
│ │ ☑ Interpreter required                  │          │
│ │ ☑ Wheelchair accessible                 │          │
│ │ ☑ Large print materials                 │          │
│ └─────────────────────────────────────────┘          │
│                                                         │
│ ⚠️ Potential conditional logic detected               │
│ [Review Logic] [Ignore] [Confirm]                     │
└─────────────────────────────────────────────────────────┘
```

### 3. Conditional Logic Review - THE HARD PART

This is where we need to be really thoughtful. Let me design a visual way to review conditional relationships:

```
┌─────────────────────────────────────────────────────────┐
│ Conditional Logic Detected                             │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ Pattern Found: Fields appear to depend on checkbox     │
│                                                         │
│ Visual Representation:                                 │
│ ┌─────────────────────────────────────────┐          │
│ │     ☐ Requesting accommodation            │          │
│ │          ↓ (when checked)                │          │
│ │    ┌──────────────────────────┐         │          │
│ │    │ Type: [_______________]   │ SHOW   │          │
│ │    │ Reason: [_____________]   │ SHOW   │          │
│ │    │ Date needed: [__/__/__]   │ SHOW   │          │
│ │    └──────────────────────────┘         │          │
│ │          ↓ (when unchecked)              │          │
│ │    [Fields hidden]                       │          │
│ └─────────────────────────────────────────┘          │
│                                                         │
│ Is this logic correct?                                 │
│                                                         │
│ ○ Yes - Hide fields when unchecked                    │
│ ○ No - Always show these fields                       │
│ ○ Partial - Let me specify which fields               │
│ ○ Complex - Multiple conditions involved              │
│                                                         │
│ [Test Logic] [Details] [Confirm]                      │
└─────────────────────────────────────────────────────────┘
```

### 4. Complex Conditional Logic Editor
When user selects "Complex" or needs to edit:

```
┌─────────────────────────────────────────────────────────┐
│ Conditional Logic Builder                              │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ Rule 1:                                                │
│ WHEN [Requesting accommodation ▼] is [checked ▼]      │
│ THEN [Show ▼] these fields:                           │
│      ☑ accommodation_type                             │
│      ☑ accommodation_reason                           │
│      ☑ date_needed                                    │
│                                                         │
│ Rule 2: [+ Add Rule]                                  │
│                                                         │
│ Test Your Logic:                                       │
│ ┌─────────────────────────────────────────┐          │
│ │ Try it:                                 │          │
│ │ ☐ Requesting accommodation              │          │
│ │                                         │          │
│ │ (Check/uncheck to see fields appear)    │          │
│ └─────────────────────────────────────────┘          │
│                                                         │
│ [Reset] [Save as Pattern] [Apply]                     │
└─────────────────────────────────────────────────────────┘
```

### 5. Table Field Review - Another Complex Case

Tables with form fields need special handling:

```
┌─────────────────────────────────────────────────────────┐
│ Table Structure Review                                 │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ Found: Table with 5 rows × 4 columns on Page 8        │
│                                                         │
│ Table Preview:                                         │
│ ┌──────────┬───────────┬──────────┬──────────┐       │
│ │ Employee │ Start Date│ End Date │ Hours    │       │
│ ├──────────┼───────────┼──────────┼──────────┤       │
│ │ [______] │ [__/__/__]│[__/__/__]│ [____]   │       │
│ │ [______] │ [__/__/__]│[__/__/__]│ [____]   │       │
│ │ [______] │ [__/__/__]│[__/__/__]│ [____]   │       │
│ └──────────┴───────────┴──────────┴──────────┘       │
│                                                         │
│ Field Types Detected:                                  │
│ Column 1: Text (Employee Name)                        │
│ Column 2: Date (Start Date)                           │
│ Column 3: Date (End Date)                             │
│ Column 4: Number (Hours)                              │
│                                                         │
│ Table Options:                                         │
│ ☑ First row is header                                 │
│ ☐ Allow adding more rows (dynamic)                    │
│ ☐ Calculate total for "Hours" column                  │
│                                                         │
│ ⚠️ Should End Date validate > Start Date?             │
│ [Add Validation] [Skip] [Confirm Structure]           │
└─────────────────────────────────────────────────────────┘
```

### 6. Calculated Field Review

```
┌─────────────────────────────────────────────────────────┐
│ Calculated Field Detection                             │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ Pattern: "Total: _______" appears after numeric fields │
│                                                         │
│ Suggested Calculation:                                 │
│ ┌─────────────────────────────────────────┐          │
│ │ hours_week_1: [    ]                    │          │
│ │ hours_week_2: [    ]                    │          │
│ │ hours_week_3: [    ]                    │          │
│ │ hours_week_4: [    ]                    │          │
│ │ ─────────────────                       │          │
│ │ total_hours: [AUTO-CALCULATED]          │          │
│ └─────────────────────────────────────────┘          │
│                                                         │
│ Formula: SUM(hours_week_1 + hours_week_2 +            │
│              hours_week_3 + hours_week_4)             │
│                                                         │
│ ○ Correct - This is a sum                             │
│ ○ Different - It's an average                         │
│ ○ Different - Custom formula                          │
│ ○ Not calculated - Regular input field                │
│                                                         │
│ [Test Calculation] [Edit Formula] [Confirm]           │
└─────────────────────────────────────────────────────────┘
```

### 7. SSN/Sensitive Field Review

```
┌─────────────────────────────────────────────────────────┐
│ ⚠️ Sensitive Field Detected                            │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ Found: "SSN (last 4): ____"                           │
│                                                         │
│ Security Settings:                                     │
│ ┌─────────────────────────────────────────┐          │
│ │ Field Type: SSN (Partial - Last 4)      │          │
│ │                                         │          │
│ │ ☑ Mask display (show as ****）         │          │
│ │ ☑ Restrict to 4 digits only            │          │
│ │ ☑ Mark as sensitive in PDF             │          │
│ │                                         │          │
│ │ ⚠️ Important: This field will be        │          │
│ │ marked for special handling.           │          │
│ └─────────────────────────────────────────┘          │
│                                                         │
│ ○ Confirm - Last 4 SSN only                          │
│ ○ Change - This is full SSN (not recommended)        │
│ ○ Change - This is a different ID number             │
│                                                         │
│ [Security Info] [Confirm]                             │
└─────────────────────────────────────────────────────────┘
```

## Bulk Review Mode (For Efficiency)

After individual reviews, show a summary for final confirmation:

```
┌─────────────────────────────────────────────────────────┐
│ Field Summary - Final Review                           │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ Total: 73 fields across 12 pages                      │
│                                                         │
│ By Type:                                               │
│ • Text fields: 28 ✓                                   │
│ • Checkboxes: 18 ✓                                    │
│ • Dates: 12 ✓                                         │
│ • Dropdowns: 6 ✓                                      │
│ • Signatures: 4 ✓                                     │
│ • SSN (partial): 2 ⚠️ (Review security)               │
│ • Calculated: 3 ⚠️ (Review formulas)                  │
│                                                         │
│ Conditional Logic:                                     │
│ • 3 show/hide rules detected ⚠️                       │
│                                                         │
│ Quick Actions:                                         │
│ [Review Warnings] [View All Fields] [Generate PDF]     │
└─────────────────────────────────────────────────────────┘
```

## Smart Defaults System

Pre-configured recognition patterns that reduce review burden:

```javascript
const SMART_DEFAULTS = {
  // Field naming patterns
  nameFields: {
    patterns: ['name', 'applicant', 'client', 'customer'],
    suggest: {
      type: 'text',
      required: true,
      maxLength: 50
    }
  },
  
  dateFields: {
    patterns: ['date', 'dob', 'birth', 'effective', 'expiration'],
    suggest: {
      type: 'date',
      format: 'MM/DD/YYYY',
      validation: 'contextual' // Past for DOB, future for expiration
    }
  },
  
  // Conditional patterns
  conditionalTriggers: {
    patterns: ['if yes', 'if checked', 'complete if', 'required when'],
    action: 'flag_for_logic_review'
  },
  
  // Table patterns
  commonTables: {
    employmentHistory: {
      headers: ['employer', 'from', 'to', 'position'],
      fieldTypes: ['text', 'date', 'date', 'text']
    },
    references: {
      headers: ['name', 'phone', 'email', 'relationship'],
      fieldTypes: ['text', 'phone', 'email', 'text']
    }
  }
}
```

## Review Workflow Optimization

### For Simple Forms (<50 fields)
1. Quick scan with auto-detection
2. Show only fields with warnings
3. One-page summary review
4. Generate

### For Medium Forms (50-200 fields)
1. Group similar fields for bulk review
2. Flag unusual patterns
3. Page-by-page review option
4. Test complex logic
5. Generate with detailed report

### For Large Forms (200-1000 fields)
1. Use smart defaults aggressively
2. Review by exception (warnings only)
3. Sample testing of logic
4. Progressive review (can pause/resume)
5. Generate with comprehensive audit trail

## Local Storage Strategy

What we save locally (browser storage):
```javascript
localStorage.setItem('accessform_preferences', {
  mode: 'simple', // or 'advanced'
  smartDefaults: true,
  customTemplates: [...], // User's saved patterns
  fieldHistory: {
    // Recent field type selections for learning
    'case_number': 'case_number_type',
    'email': 'government_email_type'
  },
  reviewPreferences: {
    skipSimpleFields: false,
    autoApplySimilar: true,
    testConditionalLogic: true
  }
});
```

## What We DON'T Store
- No form data
- No citizen information  
- No completed forms
- No cross-session history

## Summary of Review Approach

1. **Field Detection**: AI suggests, user confirms
2. **Type Assignment**: Smart defaults reduce decisions
3. **Validation Rules**: Context-aware suggestions
4. **Conditional Logic**: Visual representation for clarity
5. **Security Flags**: Special handling for sensitive fields
6. **Final Review**: Exception-based for efficiency

The key is making the review process:
- Fast for simple fields (auto-confirm when confident)
- Clear for complex logic (visual representations)
- Safe for sensitive data (multiple confirmations)
- Learnable (saves patterns locally for next time)

---

*Document Version: 1.0*
*Last Updated: August 8, 2025*
*Status: Complete and Ready for Implementation*