---
title: Screen Designs and Wireframes
type: note
permalink: access-form/ui-ux/screen-designs-and-wireframes
tags:
- '["design"'
- '"wireframes"'
- '"ui"'
- '"screens"'
- '"visual-design"]'
---

# AccessForm Screen Designs and Wireframes

## Screen Inventory

### Core Screens by Mode

#### Simple Mode Screens (Janet)
1. Welcome/Upload Screen
2. Processing Progress Screen
3. Field Review Screen
4. Settings Selection Screen
5. Download Success Screen
6. Error Recovery Screen
7. Help/Support Screen

#### Advanced Mode Screens (Alex)
1. Dashboard Home
2. File Management
3. Field Editor
4. Conditional Logic Builder
5. Template Manager
6. Batch Processing (V2)
7. Settings & Configuration
8. Reports & Analytics

## Detailed Screen Designs

### 1. Welcome Screen (Simple Mode)

#### Visual Hierarchy
```
┌────────────────────────────────────────────────────────────┐
│                                                            │
│   [Logo]              AccessForm                 [Help ?] │
│                                                            │
├────────────────────────────────────────────────────────────┤
│                                                            │
│                                                            │
│            Convert Word to Accessible PDF Forms            │
│                    Simple • Secure • Fast                  │
│                                                            │
│   ╔════════════════════════════════════════════════════╗  │
│   ║                                                    ║  │
│   ║                    📄                              ║  │
│   ║                                                    ║  │
│   ║         Drag your Word document here              ║  │
│   ║                    or                             ║  │
│   ║                                                    ║  │
│   ║            [ Choose File ]                        ║  │
│   ║                                                    ║  │
│   ╚════════════════════════════════════════════════════╝  │
│                                                            │
│     Supported formats: .doc, .docx                        │
│     Maximum size: 50MB                                    │
│                                                            │
│                                                            │
│   Recent Files:                    Need Advanced Features?│
│   ○ No recent files                    [Switch Mode →]    │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

#### Component Specifications
- **Drop Zone**: 600x300px, dashed border, hover state
- **Button**: 160x48px, primary color, large text (16px)
- **Help Icon**: 32x32px, always visible, tooltip on hover
- **Mode Switch**: Text link, bottom right, subtle

### 2. Processing Progress Screen

#### Layout Structure
```
┌────────────────────────────────────────────────────────────┐
│  [←] Step 2 of 5                                [Help ?]  │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  Finding Your Form Fields                                 │
│                                                            │
│  ┌──────────────────────────────────────────────────────┐ │
│  │                                                      │ │
│  │  Current Activity                                   │ │
│  │  ─────────────────                                  │ │
│  │  📍 Page 3 of 12                                   │ │
│  │  🔍 Analyzing table structure...                    │ │
│  │                                                      │ │
│  │  ┌────────────────────────────────────────────┐    │ │
│  │  │                                            │    │ │
│  │  │  [███████████████░░░░░░░░░░] 67%         │    │ │
│  │  │                                            │    │ │
│  │  └────────────────────────────────────────────┘    │ │
│  │                                                      │ │
│  │  Fields Discovered                                  │ │
│  │  ──────────────────                                  │ │
│  │  ✓ Text Fields.............. 23                     │ │
│  │  ✓ Checkboxes............... 18                     │ │
│  │  ✓ Date Fields.............. 8                      │ │
│  │  ✓ Signatures............... 4                      │ │
│  │  ⏳ Tables................... Processing             │ │
│  │                                                      │ │
│  │  Time: 0:47 elapsed • ~1:23 remaining               │ │
│  │                                                      │ │
│  └──────────────────────────────────────────────────────┘ │
│                                                            │
│  💡 Tip: Complex tables may take a bit longer.            │
│     This is normal for government forms!                  │
│                                                            │
│                              [Pause]    [Run in Background]│
└────────────────────────────────────────────────────────────┘
```

### 3. Field Review Screen (Simple Mode)

```
┌────────────────────────────────────────────────────────────┐
│  [←] Step 3 of 5                                [Help ?]  │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  Review Your Form Fields                                  │
│                                                            │
│  ┌─────────────┬──────────────────────────────────────┐  │
│  │ Page View   │ Field Details                        │  │
│  ├─────────────┼──────────────────────────────────────┤  │
│  │             │ 73 fields found                      │  │
│  │ [Page 1]    │ 3 need attention ⚠️                  │  │
│  │   ▼         │                                      │  │
│  │ ┌─────────┐ │ ┌──────────────────────────────────┐│  │
│  │ │         │ │ │ ⚠️ Email Field (Page 1)          ││  │
│  │ │  Form   │ │ │                                  ││  │
│  │ │ Preview │ │ │ Issue: Format unclear            ││  │
│  │ │         │ │ │ Current: "Email________"        ││  │
│  │ │ [Field] │ │ │                                  ││  │
│  │ │ [Field] │ │ │ What type of email?              ││  │
│  │ │ [⚠️]    │ │ │ ○ Any email address              ││  │
│  │ │         │ │ │ ○ Government email only          ││  │
│  │ └─────────┘ │ │ ○ Business email only            ││  │
│  │             │ │                                  ││  │
│  │ [Page 2]    │ │ [Fix This]  [Skip]  [Apply to All]││  │
│  │ [Page 3]    │ └──────────────────────────────────┘│  │
│  │             │                                      │  │
│  └─────────────┴──────────────────────────────────────┘  │
│                                                            │
│                    [← Back]            [Continue →]       │
└────────────────────────────────────────────────────────────┘
```

### 4. Advanced Mode - Dashboard

```
┌────────────────────────────────────────────────────────────┐
│ AccessForm Pro  │ Files │ Templates │ Tools │ Settings    │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  Welcome back, Alex                     [Simple Mode] [?] │
│                                                            │
│  ┌─────────────────────┬─────────────────────────────┐   │
│  │ Quick Stats         │ Recent Activity              │   │
│  ├─────────────────────┼─────────────────────────────┤   │
│  │ Forms Today: 12     │ ┌─────────────────────────┐ │   │
│  │ Fields: 1,847       │ │ VR-3472.pdf           │ │   │
│  │ Success Rate: 98%   │ │ 2:34 PM • 73 fields   │ │   │
│  │ Avg Time: 2.3 min   │ │ [View] [Download]     │ │   │
│  │                     │ └─────────────────────────┘ │   │
│  │ ┌─────────────────┐ │                             │   │
│  │ │  📁 New Upload  │ │ ┌─────────────────────────┐ │   │
│  │ └─────────────────┘ │ │ VR-3455.pdf           │ │   │
│  │                     │ │ 11:20 AM • 156 fields │ │   │
│  │ ┌─────────────────┐ │ │ [View] [Download]     │ │   │
│  │ │ 🔄 Batch Process│ │ └─────────────────────────┘ │   │
│  │ └─────────────────┘ │                             │   │
│  └─────────────────────┴─────────────────────────────┘   │
│                                                            │
│  Active Processing                                        │
│  ┌────────────────────────────────────────────────────┐  │
│  │ No active processes                                │  │
│  └────────────────────────────────────────────────────┘  │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

### 5. Field Editor (Advanced Mode)

```
┌────────────────────────────────────────────────────────────┐
│ Field Editor - VR-3472.docx              [Save] [Close X] │
├────────────────────────────────────────────────────────────┤
│                                                            │
│ ┌──────────────┬────────────────┬───────────────────────┐│
│ │ Field Tree   │ Canvas          │ Properties           ││
│ ├──────────────┼────────────────┼───────────────────────┤│
│ │ ▼ Page 1 (12)│                 │ Field: customer_name ││
│ │   ├ Name     │  ┌────────────┐ │                      ││
│ │   ├ Date     │  │   Form     │ │ Type: Text           ││
│ │   ├ Email    │  │            │ │ Label: Customer Name ││
│ │   └ Phone    │  │  [Name___] │ │                      ││
│ │ ▼ Page 2 (8) │  │  [Date___] │ │ ☑ Required           ││
│ │   ├ Address  │  │  [Email__] │ │ Max Length: [50]     ││
│ │   ├ City     │  │            │ │                      ││
│ │   ├ State    │  └────────────┘ │ Validation           ││
│ │   └ ZIP      │                 │ Pattern: [None    ▼] ││
│ │ ▶ Page 3     │  🔍 Zoom: 100%  │                      ││
│ │              │                 │ Conditional Logic    ││
│ │ ⚠ Issues (3) │                 │ [+ Add Rule]         ││
│ │              │                 │                      ││
│ └──────────────┴────────────────┴───────────────────────┘│
│                                                            │
│ [Validate All]  [Preview PDF]  [Test Form]  [Generate]    │
└────────────────────────────────────────────────────────────┘
```

### 6. Conditional Logic Builder

```
┌────────────────────────────────────────────────────────────┐
│ Conditional Logic Builder                      [Save] [X] │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  Visual Rule Builder                                      │
│  ┌────────────────────────────────────────────────────┐  │
│  │                                                    │  │
│  │    [Checkbox: Has Disability]                     │  │
│  │              │                                     │  │
│  │              ▼ When Checked                       │  │
│  │    ┌─────────────────────┐                       │  │
│  │    │ SHOW:               │                       │  │
│  │    │ • Disability Type   │                       │  │
│  │    │ • Accommodation     │                       │  │
│  │    │ • Special Needs     │                       │  │
│  │    └─────────────────────┘                       │  │
│  │              │                                     │  │
│  │              ▼ When Unchecked                     │  │
│  │    ┌─────────────────────┐                       │  │
│  │    │ HIDE: All above     │                       │  │
│  │    └─────────────────────┘                       │  │
│  │                                                    │  │
│  └────────────────────────────────────────────────────┘  │
│                                                            │
│  Rule Details                                             │
│  ┌────────────────────────────────────────────────────┐  │
│  │ Trigger: has_disability                           │  │
│  │ Operator: [equals        ▼]                       │  │
│  │ Value: [checked]                                  │  │
│  │ Action: [Show/Hide      ▼]                        │  │
│  │ Targets: disability_type, accommodation_needed    │  │
│  └────────────────────────────────────────────────────┘  │
│                                                            │
│  [Add Rule]  [Test Logic]  [Import]  [Export]            │
└────────────────────────────────────────────────────────────┘
```

## Responsive Design Breakpoints

### Desktop (1920px)
- Full feature set
- Multi-column layouts
- Side-by-side panels
- All advanced features visible

### Laptop (1366px - Default)
- Standard layout
- Collapsible sidebars
- Optimized for government workstations

### Tablet (768px - 1024px)
- Single column layout
- Stacked panels
- Touch-optimized buttons (44px minimum)
- Simplified navigation

### Mobile (Not Supported)
- Redirect to message: "Please use a desktop or tablet"

## Color Scheme & Visual Design

### Primary Palette
```css
:root {
  /* Primary - Government Blue */
  --primary-500: #003366;
  --primary-400: #004080;
  --primary-300: #0059b3;
  
  /* Success - Green */
  --success-500: #028A0F;
  --success-400: #03AC13;
  --success-300: #4CBB17;
  
  /* Warning - Amber */
  --warning-500: #F39C12;
  --warning-400: #F5B041;
  --warning-300: #F8C471;
  
  /* Error - Red */
  --error-500: #C0392B;
  --error-400: #E74C3C;
  --error-300: #EC7063;
  
  /* Neutral - Grays */
  --gray-900: #1C1C1C;
  --gray-700: #4A4A4A;
  --gray-500: #767676;
  --gray-300: #ADADAD;
  --gray-100: #F5F5F5;
}
```

### Typography System
```css
/* Headings */
h1 { font-size: 32px; font-weight: 600; }
h2 { font-size: 24px; font-weight: 600; }
h3 { font-size: 20px; font-weight: 500; }

/* Body Text */
.body-large { font-size: 18px; }
.body-regular { font-size: 16px; } /* Default */
.body-small { font-size: 14px; }

/* UI Elements */
.button-text { font-size: 16px; font-weight: 500; }
.label-text { font-size: 14px; font-weight: 500; }
.help-text { font-size: 13px; color: var(--gray-700); }
```

## Component States

### Button States
```
Default:  [─────────────]
Hover:    [═════════════]  (Darker + Shadow)
Active:   [▓▓▓▓▓▓▓▓▓▓▓▓]  (Pressed)
Disabled: [░░░░░░░░░░░░░]  (Grayed)
Loading:  [◉ Loading...]   (Spinner)
```

### Form Field States
```
Default:  ┌─────────────┐
Focus:    ┌═════════════┐  (Blue border)
Valid:    ┌─────────────┐✓ (Green check)
Error:    ┌─────────────┐✗ (Red border + message)
Disabled: ┌░░░░░░░░░░░░░┐  (Grayed)
```

### Progress Indicators
```
Linear:   [████████░░░░] 67%
Circular: ◐ Processing...
Steps:    [1]─[2]─[③]─[4]─[5]
```

## Icon System

### Core Icons (16x16, 24x24, 32x32)
```
📁 Upload       ✓ Success      ⚠️ Warning
📄 Document     ✗ Error        ℹ️ Info
⚙️ Settings     🔍 Search       💬 Help
📥 Download     👁️ Preview      🔄 Refresh
➕ Add          ➖ Remove       ✏️ Edit
```

## Animation Specifications

### Transitions
- Default duration: 200ms
- Easing: cubic-bezier(0.4, 0, 0.2, 1)
- Hover effects: 150ms
- Page transitions: 300ms

### Loading States
```javascript
// Skeleton loading for field lists
@keyframes skeleton {
  0% { background-position: -200px 0; }
  100% { background-position: calc(200px + 100%) 0; }
}

// Progress bar animation
@keyframes progress {
  0% { width: 0%; }
  100% { width: var(--progress); }
}
```

## Accessibility Specifications

### Focus Indicators
- 2px solid outline
- 2px offset
- High contrast color (#0066CC)
- Visible in all color modes

### ARIA Landmarks
```html
<header role="banner">
<nav role="navigation">
<main role="main">
<aside role="complementary">
<footer role="contentinfo">
```

### Keyboard Navigation Map
```
Tab         - Next focusable element
Shift+Tab   - Previous focusable element
Enter       - Activate button/link
Space       - Toggle checkbox/button
Arrow Keys  - Navigate within components
Escape      - Close modal/cancel operation
? (Shift+/) - Open help
Ctrl+S      - Save current work
```

## Error State Designs

### Inline Field Errors
```
┌─────────────────────────────────┐
│ Email Address*                  │
│ ┌───────────────────────────┐   │
│ │ notanemail                │   │
│ └───────────────────────────┘   │
│ ⚠️ Please enter a valid email   │
│    Example: user@agency.gov     │
└─────────────────────────────────┘
```

### Global Error Banner
```
┌────────────────────────────────────────┐
│ ⚠️ 3 issues need your attention       │
│                                        │
│ • Email format is invalid             │
│ • Required field "Name" is empty      │
│ • Date must be in the future          │
│                                        │
│ [Review Issues]  [Dismiss]            │
└────────────────────────────────────────┘
```

## Empty States

### No Files
```
┌────────────────────────────────────────┐
│                                        │
│            📁                          │
│                                        │
│     No files uploaded yet              │
│                                        │
│     Upload your first document         │
│     to get started                     │
│                                        │
│        [Upload Document]               │
│                                        │
└────────────────────────────────────────┘
```

## Success States

### Completion Screen
```
┌────────────────────────────────────────┐
│                                        │
│            ✅                          │
│                                        │
│     Success! Your PDF is ready         │
│                                        │
│     73 fields processed                │
│     100% accessible                    │
│                                        │
│    [Download PDF]  [View Report]       │
│                                        │
│    [Convert Another]                   │
│                                        │
└────────────────────────────────────────┘
```

## Summary

These screen designs provide:
1. Clear visual hierarchy for both modes
2. Consistent component patterns
3. Accessible color schemes and typography
4. Detailed states for all interactions
5. Responsive layouts for different screen sizes
6. Clear error handling and success states

The designs balance simplicity for Janet with power features for Alex, while maintaining a professional, government-appropriate aesthetic.

---

**Document Version:** 1.0
**Last Updated:** August 8, 2025
**Status:** Complete Screen Design Specification