---
title: Conditional Logic MVP Clarification
type: note
permalink: access-form/conditional-logic/conditional-logic-mvp-clarification-1
tags:
- '["conditional-logic"'
- '"mvp"'
- '"requirements"'
- '"clarification"'
- '"critical"]'
---

# Conditional Logic MVP Clarification

## The Problem with My Previous Definition

I incorrectly suggested we could only do "simple" conditional logic for MVP. But you're right - if a form HAS conditional logic, the PDF needs to work correctly, period. We can't ship partially functional forms.

## What MUST Work in MVP

### All Basic Conditional Patterns
These must ALL work, not just "simple" ones:

1. **Show/Hide Based on Checkbox**
   ```
   ☐ Has disability
      → Show: Disability type, Accommodation needed, Description
   ```

2. **Show/Hide Based on Radio Selection**
   ```
   Employment Status: ○ Employed ○ Unemployed ○ Student
      → Employed shows: Employer name, Position
      → Unemployed shows: Last employer, Date ended
      → Student shows: School name, Graduation date
   ```

3. **Cascading/Nested Conditions**
   ```
   ☐ Has disability
      → Shows: ☐ Needs accommodation
         → Shows: Accommodation type, Description
   ```

4. **Multiple Triggers for Same Target**
   ```
   Either of these checked:
   ☐ Veteran
   ☐ Active military
      → Shows: Service branch, Service dates
   ```

5. **Inverse Logic (Hide When Checked)**
   ```
   ☐ Not applicable
      → Hides/Disables: Entire section below
   ```

## What I SHOULD Have Said

The difference isn't about what conditional logic WORKS (it all must work), but about:

### What We Can DETECT Automatically vs What Needs Manual Configuration

**High-Confidence Auto-Detection:**
- "If yes, complete the following:" → Clear show/hide pattern
- Indented fields after checkbox → Likely conditional
- "N/A" checkbox → Likely hides section

**Needs User Confirmation:**
- Which specific fields are affected
- Whether it's show vs hide
- What the trigger values are

**Needs Manual Configuration:**
- Complex business rules ("If income > $50k AND household size > 4")
- Cross-field validation ("End date must be after start date")
- Calculated conditions ("If total of fields A+B+C > 100")

## The Real MVP Requirement

### What We MUST Deliver:
1. **Detection**: Identify potential conditional patterns with AI
2. **Review Interface**: Let user confirm/correct the logic
3. **PDF Implementation**: Generate working JavaScript that handles ALL confirmed logic
4. **Testing**: Let user verify logic works before finalizing

### The PDF Must Handle:
```javascript
// ALL of these must work in the generated PDF:

// Simple show/hide
if (checkbox.checked) { 
  field.display = display.visible;
}

// Multi-option
switch(radioGroup.value) {
  case 'employed': 
    showFields(['employer', 'position']);
    hideFields(['school', 'lastEmployer']);
    break;
  case 'student':
    showFields(['school', 'graduationDate']);
    hideFields(['employer', 'lastEmployer']);
    break;
}

// Nested
if (hasDisability.checked) {
  accommodationSection.display = display.visible;
  if (needsAccommodation.checked) {
    accommodationDetails.display = display.visible;
  }
}

// Multiple triggers
if (veteran.checked || activeMilitary.checked) {
  militarySection.display = display.visible;
}
```

## What We DON'T Support (Even Post-MVP)

These are genuinely complex and might never be supported:

1. **Dynamic Table Rows**
   - "Add another employer" button that creates new row
   - This requires complex PDF JavaScript beyond basic show/hide

2. **Complex Calculations in Conditions**
   - "If sum of income fields > threshold, show additional forms"
   - This mixes calculation logic with conditional logic

3. **External Data Dependencies**
   - "If case number exists in system, pre-fill these fields"
   - PDFs can't make external API calls

4. **Page-Level Conditionals**
   - "Skip to page 10 if married"
   - PDF page navigation is limited

## The Correct MVP Scope

### We WILL Support:
- ✅ ALL show/hide patterns
- ✅ ALL radio/dropdown-based branching
- ✅ ALL nested conditions (up to reasonable depth)
- ✅ ALL multiple trigger conditions
- ✅ ALL inverse logic (hide when checked)

### Our Limitation Is Detection, Not Implementation:
- We might not DETECT all patterns automatically
- User might need to CONFIRM the logic
- User might need to manually SPECIFY complex relationships
- But once specified, it MUST work in the PDF

## Updated Review Interface Approach

Instead of "simple" vs "complex", think of it as confidence levels:

### High Confidence (Auto-Apply)
"I'm 95% sure this checkbox shows these 3 fields"
→ Auto-apply, user can review in summary

### Medium Confidence (Quick Confirm)
"This looks like conditional logic. Is this correct?"
[Yes] [No] [Modify]

### Low Confidence (Manual Setup)
"These fields might be related. Please explain the logic:"
[Trigger field ▼] [shows/hides ▼] [Target fields ▼]

### No Detection (User Initiated)
"Add conditional logic manually"
User specifies from scratch

## The Bottom Line

**For MVP:**
- ALL conditional logic patterns must WORK in the generated PDF
- We might not DETECT them all automatically
- User might need to HELP us understand the logic
- But the final PDF must handle whatever logic exists correctly

**The real challenge isn't making complex logic work in PDFs** (that's just JavaScript generation), **it's detecting and understanding the logic from the Word document** (that's where AI and user review come in).

So you're absolutely right - we can't ship "partial" conditional logic support. If it's in the form, it needs to work in the PDF.

---

*Document Version: 1.0*
*Last Updated: August 8, 2025*
*Status: Critical MVP Requirement*