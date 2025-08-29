---
title: PDF JavaScript Implementation
type: note
permalink: access-form/fields/pdf-java-script-implementation-1
tags:
- '["pdf"'
- '"javascript"'
- '"acrojs"'
- '"implementation"'
- '"form-scripting"]'
---

# PDF JavaScript Implementation

## Overview
PDF documents may contain embedded JavaScript (AcroJS) that runs in response to events on form fields. This JavaScript executes in a special environment with predefined functions for interacting with fields and the document.

## Core JavaScript Events

### 1. Field-Level Events
JavaScript can be used to automate formatting, calculations, and data validation. You can develop customized actions assigned to user events.

```javascript
class PDFFieldEvents {
  // Keystroke event - fires as user types
  generateKeystrokeScript(field) {
    return `
      // Keystroke validation for ${field.id}
      function validateKeystroke(event) {
        var value = event.value;
        var change = event.change;
        var willCommit = event.willCommit;
        
        if (!willCommit) {
          // Real-time validation as user types
          switch('${field.type}') {
            case 'numeric':
              // Only allow numbers
              if (!/^[0-9.-]$/.test(change)) {
                event.rc = false; // Reject the keystroke
              }
              break;
              
            case 'phone':
              // Format as user types
              if (!/^[0-9()-\\s]$/.test(change)) {
                event.rc = false;
              }
              break;
              
            case 'ssn_partial':
              // Only allow 4 digits
              if (!/^[0-9]$/.test(change) || value.length >= 4) {
                event.rc = false;
              }
              break;
          }
        }
      }
    `;
  }
  
  // Format event - fires when field loses focus
  generateFormatScript(field) {
    return `
      // Format script for ${field.id}
      function formatField(event) {
        var value = event.value;
        
        switch('${field.type}') {
          case 'phone':
            // Format as (XXX) XXX-XXXX
            if (value.length === 10) {
              event.value = '(' + value.substr(0,3) + ') ' + 
                           value.substr(3,3) + '-' + value.substr(6,4);
            }
            break;
            
          case 'currency':
            // Format as currency
            event.value = util.printf("$%,0.2f", value);
            break;
            
          case 'percentage':
            // Format as percentage
            event.value = util.printf("%0.2f%%", value);
            break;
            
          case 'date':
            // Format date
            if (value) {
              var d = util.scand("mm/dd/yyyy", value);
              event.value = util.printd("${field.format || 'mm/dd/yyyy'}", d);
            }
            break;
        }
      }
    `;
  }
  
  // Validation event - fires before field value is committed
  generateValidationScript(field) {
    return `
      // Validation script for ${field.id}
      function validateField(event) {
        var value = event.value;
        
        // Required field check
        if (${field.required} && (!value || value === '')) {
          app.alert('${field.label} is required');
          event.rc = false;
          return;
        }
        
        // Type-specific validation
        switch('${field.type}') {
          case 'email':
            var emailRegex = /^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$/;
            if (value && !emailRegex.test(value)) {
              app.alert('Please enter a valid email address');
              event.rc = false;
            }
            break;
            
          case 'ein':
            var einRegex = /^\\d{2}-\\d{7}$/;
            if (value && !einRegex.test(value)) {
              app.alert('Please enter a valid EIN (XX-XXXXXXX)');
              event.rc = false;
            }
            break;
            
          case 'date':
            if (value) {
              var date = util.scand("mm/dd/yyyy", value);
              if (!date) {
                app.alert('Please enter a valid date');
                event.rc = false;
              }
            }
            break;
        }
      }
    `;
  }
  
  // Calculate event - for calculated fields
  generateCalculateScript(field) {
    return `
      // Calculation script for ${field.id}
      function calculateField(event) {
        var doc = event.target;
        
        switch('${field.calculation.type}') {
          case 'sum':
            var sum = 0;
            var fields = ${JSON.stringify(field.calculation.fields)};
            for (var i = 0; i < fields.length; i++) {
              var f = doc.getField(fields[i]);
              if (f && f.value) {
                sum += parseFloat(f.value) || 0;
              }
            }
            event.value = sum;
            break;
            
          case 'average':
            var total = 0, count = 0;
            var fields = ${JSON.stringify(field.calculation.fields)};
            for (var i = 0; i < fields.length; i++) {
              var f = doc.getField(fields[i]);
              if (f && f.value) {
                total += parseFloat(f.value) || 0;
                count++;
              }
            }
            event.value = count > 0 ? total / count : 0;
            break;
            
          case 'custom':
            // Custom formula
            ${field.calculation.formula}
            break;
        }
      }
    `;
  }
}
```

### 2. Conditional Field Behavior
JavaScript can control field visibility and behavior based on conditions.

```javascript
class PDFConditionalLogic {
  generateConditionalScript(rules) {
    return `
      // Conditional logic for form
      function applyConditionalLogic() {
        var doc = this;
        
        ${rules.map(rule => this.generateRuleScript(rule)).join('\n')}
      }
      
      // Attach to relevant field changes
      ${this.generateEventAttachments(rules)}
    `;
  }
  
  generateRuleScript(rule) {
    return `
        // Rule: ${rule.id}
        (function() {
          var triggerField = doc.getField('${rule.trigger.fieldId}');
          var triggerValue = triggerField ? triggerField.value : null;
          
          // Evaluate condition
          var conditionMet = false;
          
          switch('${rule.trigger.operator}') {
            case 'equals':
              conditionMet = (triggerValue === '${rule.trigger.value}');
              break;
            case 'not_equals':
              conditionMet = (triggerValue !== '${rule.trigger.value}');
              break;
            case 'greater_than':
              conditionMet = (parseFloat(triggerValue) > ${rule.trigger.value});
              break;
            case 'less_than':
              conditionMet = (parseFloat(triggerValue) < ${rule.trigger.value});
              break;
            case 'checked':
              conditionMet = (triggerValue === 'Yes' || triggerValue === true);
              break;
            case 'not_checked':
              conditionMet = (triggerValue === 'Off' || triggerValue === false);
              break;
          }
          
          // Apply action to target fields
          var targetFields = ${JSON.stringify(rule.target.fieldIds)};
          for (var i = 0; i < targetFields.length; i++) {
            var targetField = doc.getField(targetFields[i]);
            if (targetField) {
              switch('${rule.target.action}') {
                case 'show':
                  targetField.display = conditionMet ? 
                    display.visible : display.hidden;
                  break;
                case 'hide':
                  targetField.display = conditionMet ? 
                    display.hidden : display.visible;
                  break;
                case 'enable':
                  targetField.readonly = !conditionMet;
                  break;
                case 'disable':
                  targetField.readonly = conditionMet;
                  break;
                case 'require':
                  targetField.required = conditionMet;
                  break;
              }
            }
          }
        })();
    `;
  }
  
  generateEventAttachments(rules) {
    const triggerFields = new Set()
    rules.forEach(rule => triggerFields.add(rule.trigger.fieldId))
    
    return Array.from(triggerFields).map(fieldId => `
      var field_${fieldId} = this.getField('${fieldId}');
      if (field_${fieldId}) {
        field_${fieldId}.setAction('OnChange', 'applyConditionalLogic()');
      }
    `).join('\n')
  }
}
```

### 3. Document-Level Scripts
Scripts that run when the document opens or on specific document events.

```javascript
class PDFDocumentScripts {
  generateDocumentOpenScript() {
    return `
      // Document open script
      function documentOpen() {
        // Initialize form
        console.println("Initializing form...");
        
        // Set default values
        var today = new Date();
        var dateField = this.getField('currentDate');
        if (dateField) {
          dateField.value = util.printd("mm/dd/yyyy", today);
        }
        
        // Apply initial conditional logic
        applyConditionalLogic();
        
        // Set focus to first field
        var firstField = this.getField('${this.firstFieldId}');
        if (firstField) {
          firstField.setFocus();
        }
        
        // Check for required plugins or readers
        if (app.viewerVersion < 11) {
          app.alert('This form requires Adobe Reader 11 or higher');
        }
      }
      
      // Run on document open
      documentOpen.call(this);
    `;
  }
  
  generateDocumentWillSaveScript() {
    return `
      // Before save validation
      function documentWillSave() {
        // Validate all required fields
        var requiredFields = ${JSON.stringify(this.requiredFields)};
        var missingFields = [];
        
        for (var i = 0; i < requiredFields.length; i++) {
          var field = this.getField(requiredFields[i].id);
          if (field && (!field.value || field.value === '')) {
            missingFields.push(requiredFields[i].label);
          }
        }
        
        if (missingFields.length > 0) {
          var response = app.alert({
            cMsg: 'The following required fields are empty:\\n' + 
                  missingFields.join('\\n') + 
                  '\\n\\nDo you want to save anyway?',
            cTitle: 'Missing Required Fields',
            nIcon: 2, // Warning icon
            nType: 2  // Yes/No buttons
          });
          
          if (response === 4) { // No button
            event.rc = false; // Cancel save
          }
        }
      }
    `;
  }
}
```

### 4. Advanced Calculations and Formulas

```javascript
class PDFCalculations {
  generateComplexCalculation(field) {
    const calc = field.calculation
    
    switch (calc.type) {
      case 'conditional_sum':
        return `
          // Conditional sum calculation
          var sum = 0;
          var condition = this.getField('${calc.conditionField}').value;
          
          if (condition === '${calc.conditionValue}') {
            var fields = ${JSON.stringify(calc.sumFields)};
            for (var i = 0; i < fields.length; i++) {
              var f = this.getField(fields[i]);
              if (f) sum += (parseFloat(f.value) || 0);
            }
          }
          
          event.value = sum;
        `;
        
      case 'percentage_of_total':
        return `
          // Calculate percentage of total
          var value = parseFloat(this.getField('${calc.valueField}').value) || 0;
          var total = parseFloat(this.getField('${calc.totalField}').value) || 0;
          
          if (total > 0) {
            event.value = (value / total * 100).toFixed(2) + '%';
          } else {
            event.value = '0%';
          }
        `;
        
      case 'date_difference':
        return `
          // Calculate difference between dates
          var startDate = util.scand("mm/dd/yyyy", 
            this.getField('${calc.startDateField}').value);
          var endDate = util.scand("mm/dd/yyyy", 
            this.getField('${calc.endDateField}').value);
          
          if (startDate && endDate) {
            var diff = endDate.getTime() - startDate.getTime();
            var days = Math.floor(diff / (1000 * 60 * 60 * 24));
            event.value = days + ' days';
          }
        `;
        
      case 'lookup_table':
        return `
          // Lookup table calculation
          var lookupValue = this.getField('${calc.lookupField}').value;
          var lookupTable = ${JSON.stringify(calc.lookupTable)};
          
          event.value = lookupTable[lookupValue] || '${calc.defaultValue}';
        `;
    }
  }
}
```

### 5. Form Submission and Data Handling

```javascript
class PDFFormSubmission {
  generateSubmissionScript(config) {
    return `
      // Form submission handler
      function submitForm() {
        // Validate before submission
        var errors = [];
        
        // Check required fields
        var requiredFields = ${JSON.stringify(config.requiredFields)};
        for (var i = 0; i < requiredFields.length; i++) {
          var field = this.getField(requiredFields[i]);
          if (!field || !field.value) {
            errors.push('Missing required field: ' + requiredFields[i]);
          }
        }
        
        if (errors.length > 0) {
          app.alert('Please correct the following errors:\\n' + 
                    errors.join('\\n'));
          return;
        }
        
        // Prepare submission
        var submitFields = ${JSON.stringify(config.submitFields)};
        
        try {
          this.submitForm({
            cURL: '${config.submitUrl}',
            aFields: submitFields,
            cSubmitAs: '${config.submitFormat || 'HTML'}', // HTML, FDF, XFDF, PDF
            bEmpty: ${config.includeEmpty || false},
            bGetMethod: ${config.useGet || false}
          });
          
          app.alert('Form submitted successfully!');
          
        } catch (e) {
          app.alert('Error submitting form: ' + e.message);
        }
      }
    `;
  }
  
  generatePrintScript() {
    return `
      // Custom print handler
      function beforePrint() {
        // Hide buttons and non-printable elements
        var noPrintFields = ['submitButton', 'resetButton', 'printButton'];
        
        for (var i = 0; i < noPrintFields.length; i++) {
          var field = this.getField(noPrintFields[i]);
          if (field) {
            field.display = display.noPrint;
          }
        }
        
        // Add print timestamp
        var printDate = this.getField('printDate');
        if (printDate) {
          printDate.value = util.printd("mm/dd/yyyy h:MM tt", new Date());
        }
      }
    `;
  }
}
```

## JavaScript API Compatibility

```javascript
class PDFJavaScriptAPI {
  // Supported Acrobat JavaScript APIs
  static supportedAPIs = {
    app: {
      alert: true,
      beep: true,
      viewerVersion: true,
      platform: true,
      language: true
    },
    document: {
      getField: true,
      resetForm: true,
      submitForm: true,
      print: true,
      saveAs: true,
      calculateNow: true,
      getPageNum: true,
      info: true
    },
    field: {
      value: true,
      defaultValue: true,
      readonly: true,
      required: true,
      display: true,
      fillColor: true,
      textColor: true,
      borderColor: true,
      setFocus: true,
      setAction: true
    },
    util: {
      printf: true,
      printd: true,
      scand: true,
      stringFromStream: true
    },
    event: {
      value: true,
      change: true,
      changeEx: true,
      willCommit: true,
      rc: true,
      target: true,
      targetName: true
    }
  }
  
  validateScript(script) {
    // Check for unsupported APIs
    const warnings = []
    
    // Check for security-restricted operations
    if (script.includes('app.openDoc')) {
      warnings.push('app.openDoc is restricted for security')
    }
    
    if (script.includes('app.execMenuItem')) {
      warnings.push('app.execMenuItem may not work in all viewers')
    }
    
    if (script.includes('Net.HTTP')) {
      warnings.push('Network operations require special privileges')
    }
    
    return warnings
  }
}
```

## Implementation Considerations

### Security Restrictions
1. **Sandboxed Environment** - AcroJS runs in a restricted environment
2. **No Network Access** - Direct HTTP requests are blocked by default
3. **File System Limited** - Cannot access local files without user permission
4. **Cross-Domain Restrictions** - Cannot access data from other PDFs

### Browser Compatibility
1. **Adobe Reader/Acrobat** - Full JavaScript support
2. **Chrome PDF Viewer** - Limited or no JavaScript support
3. **Firefox PDF.js** - Very limited JavaScript support
4. **Mobile Viewers** - Generally no JavaScript support

### Fallback Strategies
```javascript
class PDFFallbackStrategy {
  generateProgressiveEnhancement() {
    return `
      // Check if JavaScript is supported
      function checkJavaScriptSupport() {
        try {
          // Test basic functionality
          var testField = this.getField('jsTest');
          if (testField) {
            testField.value = 'JavaScript Enabled';
            return true;
          }
        } catch (e) {
          // JavaScript not supported or restricted
        }
        return false;
      }
      
      // Provide fallback instructions
      if (!checkJavaScriptSupport()) {
        app.alert('This form works best with Adobe Reader. ' +
                  'Some features may not be available in your PDF viewer.');
      }
    `;
  }
}
```

### Best Practices

1. **Keep Scripts Simple** - Complex logic may not work in all viewers
2. **Test Thoroughly** - Test in multiple PDF viewers
3. **Provide Fallbacks** - Ensure form is usable without JavaScript
4. **Document Dependencies** - List required PDF viewer versions
5. **Handle Errors Gracefully** - Use try-catch blocks
6. **Optimize Performance** - Minimize script execution time
7. **Use Standard APIs** - Stick to widely supported functions
8. **Validate Server-Side** - Never rely solely on client-side validation

### Debugging Techniques

```javascript
class PDFDebugging {
  static enableDebugging() {
    return `
      // Enable console for debugging
      var debugMode = true;
      
      function debug(message) {
        if (debugMode) {
          console.println('[DEBUG] ' + message);
        }
      }
      
      // Log field changes
      function logFieldChange(fieldName, oldValue, newValue) {
        debug('Field ' + fieldName + ' changed from ' + 
              oldValue + ' to ' + newValue);
      }
      
      // Catch and log errors
      function safeExecute(func, context) {
        try {
          return func.call(context);
        } catch (e) {
          debug('Error: ' + e.message);
          return null;
        }
      }
    `;
  }
}
```

---

*Document Version: 1.0*
*Last Updated: August 8, 2025*
*Status: Complete and Ready for Implementation*