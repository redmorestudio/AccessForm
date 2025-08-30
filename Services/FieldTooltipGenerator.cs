using System;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Generates context-aware tooltips for all 33 field types
    /// </summary>
    public static class FieldTooltipGenerator
    {
        public static string GenerateTooltip(string fieldType, string fieldName)
        {
            fieldType = fieldType?.ToLower() ?? "text";
            
            return fieldType switch
            {
                // Basic Input Fields
                "text" => $"Enter {fieldName}",
                "textarea" => $"Enter detailed information for {fieldName}",
                
                // Selection Fields
                "checkbox" => $"Check if {fieldName} applies",
                "radio" => $"Select one option for {fieldName}",
                "dropdown" => $"Select {fieldName} from the list",
                "listbox" => $"Select one or more options for {fieldName}",
                
                // Date/Time Fields
                "date" => "Enter date (MM/DD/YYYY)",
                "time" => "Enter time (HH:MM AM/PM)",
                
                // Contact Fields
                "email" => "Enter email address (example@domain.com)",
                "phone" => "Enter 10-digit phone number (XXX-XXX-XXXX)",
                "url" => "Enter website URL (https://example.com)",
                
                // Government ID Fields
                "ssn" => "Enter 9-digit Social Security Number (XXX-XX-XXXX)",
                "ssn_partial" => "Enter last 4 digits of SSN",
                "ein" => "Enter Employer Identification Number (XX-XXXXXXX)",
                "tin" => "Enter Taxpayer Identification Number",
                "drivers_license" => "Enter driver's license or state ID number",
                
                // Signature Fields
                "signature" => "Click to add your signature",
                "initials" => "Click to add your initials",
                
                // Numeric Fields
                "numeric" => "Enter a number",
                "currency" => "Enter dollar amount (e.g., $100.00)",
                "percentage" => "Enter percentage (0-100)",
                
                // Special Fields
                "file_upload" => "Click to upload a file",
                "case_number" => "Enter case or reference number",
                "calculated" => "This field will be calculated automatically",
                "protected" => "This field is read-only",
                "conditional" => $"This field appears based on other selections",
                "field_group" => "Group of related fields",
                
                // Composite Fields
                "name" => "Enter full name (First Middle Last)",
                "address" => "Enter complete address",
                
                // Inclusive Fields
                "gender_pronoun" => "Select gender identity or pronouns",
                "language_preference" => "Select preferred language",
                
                // Structure Fields
                "repeatable_section" => "Click + to add another section",
                "error_display" => "Validation errors will appear here",
                "compliance_acknowledgment" => "Check to acknowledge you have read and agree",
                
                // Default
                _ => $"Enter {fieldName}"
            };
        }
        
        public static string GetFormatHint(string fieldType)
        {
            return fieldType?.ToLower() switch
            {
                "date" => "MM/DD/YYYY",
                "time" => "HH:MM AM/PM",
                "email" => "name@example.com",
                "phone" => "XXX-XXX-XXXX",
                "ssn" => "XXX-XX-XXXX",
                "ssn_partial" => "XXXX",
                "ein" => "XX-XXXXXXX",
                "currency" => "$X,XXX.XX",
                "percentage" => "0-100%",
                "url" => "https://...",
                _ => ""
            };
        }
        
        public static string GetValidationMessage(string fieldType)
        {
            return fieldType?.ToLower() switch
            {
                "email" => "Please enter a valid email address",
                "phone" => "Please enter a valid 10-digit phone number",
                "ssn" => "Please enter a valid Social Security Number",
                "ein" => "Please enter a valid EIN",
                "date" => "Please enter a valid date",
                "url" => "Please enter a valid URL",
                "numeric" => "Please enter a valid number",
                "currency" => "Please enter a valid dollar amount",
                "percentage" => "Please enter a value between 0 and 100",
                _ => "Please enter a valid value"
            };
        }
    }
}