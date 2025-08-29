---
title: Conditional Logic Review Design
type: note
permalink: access-form/conditional-logic/conditional-logic-review-design-1
tags:
- '["conditional-logic"'
- '"ui"'
- '"field-review"'
- '"implementation"'
- '"design"]'
---

# Conditional Logic Review Design

## The Problem
Conditional logic is the hardest part because:
1. It's not visually obvious in Word docs
2. Multiple fields can depend on one trigger
3. Logic can be nested (conditions depending on conditions)
4. Users may not understand what we're asking

## Detection Patterns

### What We Can Detect
```javascript
const detectablePatterns = {
  // Clear indicators
  explicit: [
    "If yes, complete the following:",
    "If checked, provide details:",
    "Complete only if applicable:",
    "Skip to Section X if No"
  ],
  
  // Structural hints
  structural: [
    "Indented fields after checkbox",
    "Fields in a bordered box after radio button",
    "Gray/shaded areas with 'conditional' headers",
    "Arrow symbols (→, ▼) indicating flow"
  ],
  
  // Common patterns
  common: [
    "Other: [specify]",
    "Yes/No followed by details",
    "N/A checkbox that disables section",
    "Primary/Secondary where secondary depends on primary"
  ]
}
```

## Review Interface Approach

### Level 1: Simple Binary Decision
Most conditional logic is binary (show/hide based on checkbox/radio):

```
┌─────────────────────────────────────────────────────────┐
│ Quick Logic Check                                      │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ I found this pattern:                                  │
│                                                         │
│ ☐ Requesting accommodation                             │
│   └─ Type of accommodation: _________                  │
│   └─ Reason: ________________________                 │
│   └─ Date needed: ___/___/_____                       │
│                                                         │
│ Should these fields only show when the box             │
│ is checked?                                           │
│                                                         │
│        [Yes, hide when unchecked]                     │
│        [No, always show them]                         │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### Level 2: Multi-Option Logic
For radio buttons or dropdowns with multiple triggers:

```
┌─────────────────────────────────────────────────────────┐
│ Multi-Option Logic                                     │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ Employment Status:                                     │
│ ○ Employed                                            │
│ ○ Self-employed                                       │
│ ○ Unemployed                                          │
│ ○ Student                                             │
│ ○ Retired                                             │
│                                                         │
│ Fields that appear to be related:                     │
│ • Employer name: _________                            │
│ • Business name: _________                            │
│ • School name: __________                             │
│ • Last employer: ________                             │
│                                                         │
│ Match fields to options:                              │
│ ┌─────────────────────────────────────┐              │
│ │ Employed → [Employer name      ▼]   │              │
│ │ Self-employed → [Business name ▼]   │              │
│ │ Student → [School name        ▼]    │              │
│ │ Unemployed → [Last employer   ▼]    │              │
│ │ Retired → [None              ▼]     │              │
│ └─────────────────────────────────────┘              │
│                                                         │
│ [Test This Logic] [Skip Complex Logic] [Confirm]      │
└─────────────────────────────────────────────────────────┘
```

### Level 3: Complex Nested Logic
When logic is nested or complex:

```
┌─────────────────────────────────────────────────────────┐
│ Complex Logic Pattern                                  │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ This looks complex. Here's what I found:              │
│                                                         │
│ Primary Question:                                      │
│ ☐ Has disability                                      │
│     ↓                                                  │
│ Secondary Questions (if checked):                      │
│ ☐ Needs accommodation                                 │
│     ↓                                                  │
│ Details (if accommodation checked):                    │
│ • Type: _________                                     │
│ • Description: _________                              │
│                                                         │
│ How should this work?                                 │
│                                                         │
│ Option 1: Simple Chain                                │
│ Has disability → Shows accommodation checkbox         │
│ Accommodation → Shows detail fields                   │
│                                                         │
│ Option 2: Direct Jump                                 │
│ Has disability → Shows all fields at once            │
│                                                         │
│ Option 3: No Logic                                    │
│ Show everything always                                │
│                                                         │
│ [Select: Option 1 ▼] [Preview] [Confirm]             │
└─────────────────────────────────────────────────────────┘
```

## Testing Interface

Let users test the logic before confirming:

```
┌─────────────────────────────────────────────────────────┐
│ Test Your Logic                                        │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ Try different selections to see what happens:         │
│                                                         │
│ ☐ Requesting accommodation                            │
│                                                         │
│ (Currently hidden fields - check box to show)         │
│ ┌─────────────────────────────────────┐              │
│ │ These fields are currently hidden:   │              │
│ │ • Type of accommodation              │              │
│ │ • Reason for request                 │              │
│ │ • Date needed                        │              │
│ └─────────────────────────────────────┘              │
│                                                         │
│ ☑ Requesting accommodation                            │
│                                                         │
│ (Now showing conditional fields)                      │
│ Type: [____________________]                         │
│ Reason: [__________________]                         │
│ Date needed: [___/___/_____]                         │
│                                                         │
│ [Logic is Correct] [Need to Change] [Remove Logic]    │
└─────────────────────────────────────────────────────────┘
```

## Smart Grouping for Review

Group similar conditional patterns:

```
┌─────────────────────────────────────────────────────────┐
│ Similar Patterns Found                                 │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ I found 4 similar "if checked, show details" patterns:│
│                                                         │
│ 1. ☐ Has disability → [disability details]            │
│ 2. ☐ Veteran status → [service details]               │
│ 3. ☐ Previous training → [training details]           │
│ 4. ☐ Criminal history → [explanation field]           │
│                                                         │
│ Apply the same logic to all?                          │
│                                                         │
│ ○ Yes - All work the same way                        │
│ ○ No - Let me review each one                        │
│ ○ Mix - Some are different                           │
│                                                         │
│ [Apply to All] [Review Individual] [Select Which]     │
└─────────────────────────────────────────────────────────┘
```

## Fallback for Unclear Logic

When we can't determine the logic:

```
┌─────────────────────────────────────────────────────────┐
│ Unable to Determine Logic                              │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ These fields might be related, but I'm not sure how:  │
│                                                         │
│ • Referral source: ________                           │
│ • Referral details: _______                           │
│ • Contact allowed: Yes/No                             │
│ • Contact information: _____                          │
│                                                         │
│ What would you like to do?                           │
│                                                         │
│ ○ Make them always visible (simplest)                │
│ ○ I'll explain the relationship                      │
│ ○ Skip for now and document in report                │
│                                                         │
│ If you explain:                                       │
│ [Field A ▼] shows when [Field B ▼] equals [___]     │
│                                                         │
│ [Continue Without Logic] [Add Simple Rule]            │
└─────────────────────────────────────────────────────────┘
```

## How We'll Actually Implement This

### Data Structure
```javascript
const conditionalLogic = {
  rules: [
    {
      id: 'rule_1',
      trigger: {
        fieldId: 'has_disability',
        condition: 'checked',
        value: true
      },
      action: 'show',
      targets: ['disability_type', 'accommodation_needed'],
      confidence: 0.85,
      userConfirmed: true
    }
  ]
}
```

### PDF JavaScript Generation
```javascript
function generatePDFLogicScript(rules) {
  return rules.map(rule => `
    // Rule: ${rule.id}
    var trigger = this.getField('${rule.trigger.fieldId}');
    var targets = ${JSON.stringify(rule.targets)};
    
    if (trigger) {
      trigger.setAction('MouseUp', function() {
        var show = trigger.value === '${rule.trigger.value}';
        targets.forEach(function(fieldId) {
          var field = this.getField(fieldId);
          if (field) {
            field.display = show ? display.visible : display.hidden;
            if (!show) field.value = ''; // Clear hidden fields
          }
        });
      });
    }
  `).join('\n');
}
```

## Progressive Complexity Approach

### For Simple Forms
- Auto-detect obvious patterns
- Single confirmation for all logic
- Binary show/hide only

### For Medium Forms  
- Group similar patterns
- Test interface for verification
- Support multi-option logic

### For Complex Forms
- Individual review of complex patterns
- Visual testing interface
- Document uncertain logic in report
- Allow manual rule creation

## What We DON'T Support (MVP)

- Complex calculations between fields
- Multi-level nested conditions (>2 levels)
- Cross-page dependencies
- Dynamic table rows based on conditions
- Complex validation rules (field A > field B)

## Success Metrics

- Correctly identify 80% of simple show/hide patterns
- User can confirm/correct logic in <30 seconds per pattern
- Generated PDF logic works in Adobe Reader
- Clear documentation of what wasn't automated

## The Key Insight

Most government forms use simple patterns:
1. **Checkbox → Show details** (60% of conditional logic)
2. **Radio → Show specific fields** (25%)
3. **"Other" → Text field** (10%)
4. **Complex nested logic** (5%)

We optimize for the common cases and provide clear fallbacks for complex ones.

---

*Document Version: 1.0*
*Last Updated: August 8, 2025*
*Status: Complete Design*