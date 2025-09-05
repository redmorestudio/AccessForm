using System;
using System.Collections.Generic;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Validates field type conversions to ensure compatibility
    /// Prevents invalid conversions like text → radio button
    /// </summary>
    public static class FieldTypeCompatibilityChecker
    {
        // Define which field types can be converted to which other types
        private static readonly Dictionary<string, HashSet<string>> CompatibleConversions = new Dictionary<string, HashSet<string>>
        {
            // Text fields can be specialized but not changed to selection fields
            ["text"] = new HashSet<string> 
            { 
                "text", "textarea", "email", "phone", "ssn", "ssn_partial", "ein", 
                "date", "time", "numeric", "currency", "percentage", "url",
                "case_number", "drivers_license", "tin", "name", "address", "signature"
            },
            
            // Text area can only become text types
            ["textarea"] = new HashSet<string> 
            { 
                "textarea", "text", "address"
            },
            
            // Checkbox can only be checkbox or boolean-like fields
            ["checkbox"] = new HashSet<string> 
            { 
                "checkbox", "compliance_acknowledgment"
            },
            
            // Radio buttons must stay as radio buttons
            ["radio"] = new HashSet<string> 
            { 
                "radio", "gender_pronoun", "language_preference"
            },
            
            // Dropdown can be other selection types
            ["dropdown"] = new HashSet<string> 
            { 
                "dropdown", "listbox", "language_preference", "gender_pronoun"
            },
            
            // Date field conversions
            ["date"] = new HashSet<string> 
            { 
                "date", "text"
            },
            
            // Time field conversions
            ["time"] = new HashSet<string> 
            { 
                "time", "text"
            },
            
            // Email can only be email or text
            ["email"] = new HashSet<string> 
            { 
                "email", "text"
            },
            
            // Phone can be phone or text
            ["phone"] = new HashSet<string> 
            { 
                "phone", "text"
            },
            
            // SSN fields
            ["ssn"] = new HashSet<string> 
            { 
                "ssn", "ssn_partial", "text", "tin"
            },
            
            ["ssn_partial"] = new HashSet<string> 
            { 
                "ssn_partial", "text"
            },
            
            // EIN field
            ["ein"] = new HashSet<string> 
            { 
                "ein", "text", "tin"
            },
            
            // Signature fields
            ["signature"] = new HashSet<string> 
            { 
                "signature", "text"
            },
            
            ["initials"] = new HashSet<string> 
            { 
                "initials", "signature"
            },
            
            // Numeric fields
            ["numeric"] = new HashSet<string> 
            { 
                "numeric", "currency", "percentage", "text"
            },
            
            ["currency"] = new HashSet<string> 
            { 
                "currency", "numeric", "text"
            },
            
            ["percentage"] = new HashSet<string> 
            { 
                "percentage", "numeric", "text"
            },
            
            // Special fields
            ["file_upload"] = new HashSet<string> 
            { 
                "file_upload"
            },
            
            ["case_number"] = new HashSet<string> 
            { 
                "case_number", "text"
            },
            
            ["calculated"] = new HashSet<string> 
            { 
                "calculated", "numeric", "currency", "text"
            },
            
            ["protected"] = new HashSet<string> 
            { 
                "protected", "text"
            },
            
            ["conditional"] = new HashSet<string> 
            { 
                "conditional", "text"
            },
            
            ["field_group"] = new HashSet<string> 
            { 
                "field_group"
            },
            
            // Composite fields
            ["name"] = new HashSet<string> 
            { 
                "name", "text"
            },
            
            ["address"] = new HashSet<string> 
            { 
                "address", "textarea", "text"
            },
            
            ["url"] = new HashSet<string> 
            { 
                "url", "text"
            },
            
            ["drivers_license"] = new HashSet<string> 
            { 
                "drivers_license", "text"
            },
            
            ["gender_pronoun"] = new HashSet<string> 
            { 
                "gender_pronoun", "dropdown", "radio"
            },
            
            ["language_preference"] = new HashSet<string> 
            { 
                "language_preference", "dropdown", "radio"
            },
            
            ["tin"] = new HashSet<string> 
            { 
                "tin", "ein", "ssn", "text"
            },
            
            ["repeatable_section"] = new HashSet<string> 
            { 
                "repeatable_section"
            },
            
            ["error_display"] = new HashSet<string> 
            { 
                "error_display"
            },
            
            ["compliance_acknowledgment"] = new HashSet<string> 
            { 
                "compliance_acknowledgment", "checkbox"
            }
        };
        
        /// <summary>
        /// Check if a field type conversion is valid
        /// </summary>
        public static bool IsCompatibleConversion(string fromType, string toType)
        {
            if (string.IsNullOrEmpty(fromType) || string.IsNullOrEmpty(toType))
                return false;
                
            fromType = fromType.ToLower();
            toType = toType.ToLower();
            
            // Same type is always compatible
            if (fromType == toType)
                return true;
                
            // Check if conversion is allowed
            if (CompatibleConversions.TryGetValue(fromType, out var allowedTypes))
            {
                return allowedTypes.Contains(toType);
            }
            
            // If type not in dictionary, only allow conversion to text
            return toType == "text";
        }
        
        /// <summary>
        /// Get the best compatible type when a conversion is not allowed
        /// </summary>
        public static string GetCompatibleType(string originalType, string suggestedType)
        {
            if (IsCompatibleConversion(originalType, suggestedType))
                return suggestedType;
                
            // If suggested type is not compatible, return the original
            return originalType;
        }
        
        /// <summary>
        /// Determine if a type is a selection type (checkbox, radio, dropdown)
        /// </summary>
        public static bool IsSelectionType(string fieldType)
        {
            if (string.IsNullOrEmpty(fieldType))
                return false;
                
            fieldType = fieldType.ToLower();
            return fieldType == "checkbox" || 
                   fieldType == "radio" || 
                   fieldType == "dropdown" || 
                   fieldType == "listbox";
        }
        
        /// <summary>
        /// Determine if a type is a text-based type
        /// </summary>
        public static bool IsTextBasedType(string fieldType)
        {
            if (string.IsNullOrEmpty(fieldType))
                return false;
                
            fieldType = fieldType.ToLower();
            return fieldType == "text" || 
                   fieldType == "textarea" || 
                   fieldType == "email" || 
                   fieldType == "phone" || 
                   fieldType == "ssn" ||
                   fieldType == "ssn_partial" ||
                   fieldType == "ein" ||
                   fieldType == "url" ||
                   fieldType == "case_number" ||
                   fieldType == "drivers_license" ||
                   fieldType == "tin" ||
                   fieldType == "name" ||
                   fieldType == "address";
        }
    }
}