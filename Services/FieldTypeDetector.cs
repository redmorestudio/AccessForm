using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Detects and classifies field types based on AccessForm 33-type specification
    /// </summary>
    public class FieldTypeDetector
    {
        // Field type constants matching AccessForm specification
        public const string TEXT = "text";
        public const string EMAIL = "email";
        public const string PHONE = "phone";
        public const string SSN = "ssn";
        public const string DATE = "date";
        public const string TIME = "time";
        public const string DATETIME = "datetime";
        public const string NUMBER = "number";
        public const string CURRENCY = "currency";
        public const string PERCENTAGE = "percentage";
        public const string CHECKBOX = "checkbox";
        public const string RADIO = "radio";
        public const string DROPDOWN = "dropdown";
        public const string LISTBOX = "listbox";
        public const string TEXTAREA = "textarea";
        public const string SIGNATURE = "signature";
        public const string FILE_UPLOAD = "file_upload";
        public const string IMAGE = "image";
        public const string BARCODE = "barcode";
        public const string QR_CODE = "qr_code";
        public const string RATING = "rating";
        public const string SLIDER = "slider";
        public const string COLOR_PICKER = "color_picker";
        public const string PASSWORD = "password";
        public const string PIN = "pin";
        public const string ZIP = "zip";
        public const string STATE = "state";
        public const string COUNTRY = "country";
        public const string URL = "url";
        public const string EIN = "ein";
        public const string NAME = "name";
        public const string ADDRESS = "address";
        public const string TABLE = "table";

        private static readonly Dictionary<string, List<string>> FieldTypeKeywords = new Dictionary<string, List<string>>
        {
            [EMAIL] = new List<string> { "email", "e-mail", "electronic mail", "contact email" },
            [PHONE] = new List<string> { "phone", "telephone", "tel", "mobile", "cell", "fax", "contact number" },
            [SSN] = new List<string> { "ssn", "social security", "social security number", "ss#", "social security no" },
            [DATE] = new List<string> { "date", "dob", "birth date", "birthdate", "expiration date", "due date", "effective date", "mm/dd/yyyy", "dd/mm/yyyy" },
            [TIME] = new List<string> { "time", "hour", "hours", "am/pm", "hh:mm" },
            [DATETIME] = new List<string> { "date and time", "datetime", "timestamp" },
            [NUMBER] = new List<string> { "number", "quantity", "amount", "count", "total", "age", "years" },
            [CURRENCY] = new List<string> { "price", "cost", "amount", "payment", "salary", "wage", "income", "$", "dollar", "usd", "fee", "charge" },
            [PERCENTAGE] = new List<string> { "percent", "percentage", "%", "rate" },
            [CHECKBOX] = new List<string> { "checkbox", "check", "☐", "□", "select all that apply", "check all", "tick" },
            [RADIO] = new List<string> { "radio", "select one", "choose one", "option", "○", "◯", "choice" },
            [DROPDOWN] = new List<string> { "dropdown", "drop down", "select", "choose", "pick", "menu" },
            [LISTBOX] = new List<string> { "list", "listbox", "multiple select", "multi-select" },
            [TEXTAREA] = new List<string> { "comments", "notes", "description", "message", "feedback", "explain", "describe", "additional information", "remarks" },
            [SIGNATURE] = new List<string> { "signature", "sign", "signed by", "authorized signature", "sign here", "signatory" },
            [FILE_UPLOAD] = new List<string> { "upload", "attach", "attachment", "file", "document", "browse", "choose file" },
            [IMAGE] = new List<string> { "image", "photo", "picture", "photograph", "img", "pic" },
            [BARCODE] = new List<string> { "barcode", "bar code", "upc", "ean" },
            [QR_CODE] = new List<string> { "qr", "qr code", "quick response" },
            [RATING] = new List<string> { "rating", "rate", "stars", "score", "satisfaction" },
            [SLIDER] = new List<string> { "slider", "range", "scale" },
            [COLOR_PICKER] = new List<string> { "color", "colour", "pick color", "choose color" },
            [PASSWORD] = new List<string> { "password", "passcode", "secret", "pwd" },
            [PIN] = new List<string> { "pin", "pin code", "pin number", "security code" },
            [ZIP] = new List<string> { "zip", "zip code", "postal code", "postcode", "zipcode" },
            [STATE] = new List<string> { "state", "province", "region" },
            [COUNTRY] = new List<string> { "country", "nation", "citizenship" },
            [URL] = new List<string> { "url", "website", "web address", "link", "http", "https", "www" },
            [EIN] = new List<string> { "ein", "employer identification", "tax id", "federal tax", "fein", "tin" },
            [NAME] = new List<string> { "name", "full name", "first name", "last name", "surname", "given name", "middle name", "maiden name" },
            [ADDRESS] = new List<string> { "address", "street", "city", "location", "residence", "mailing address", "physical address" }
        };

        /// <summary>
        /// Detect field type based on field name and context
        /// </summary>
        public static string DetectFieldType(string fieldName, string fieldValue = null, string nearbyText = null)
        {
            if (string.IsNullOrEmpty(fieldName))
                return TEXT;

            var nameLower = fieldName.ToLower().Trim();
            var valueLower = fieldValue?.ToLower()?.Trim() ?? "";
            var contextLower = nearbyText?.ToLower()?.Trim() ?? "";
            var combinedText = $"{nameLower} {valueLower} {contextLower}";

            // Check for security fields first (highest priority)
            if (IsSSNField(combinedText))
                return SSN;
            if (IsEINField(combinedText))
                return EIN;
            if (IsPasswordField(nameLower))
                return PASSWORD;
            if (IsPINField(combinedText))
                return PIN;

            // Check for composite fields
            if (IsNameField(combinedText))
                return NAME;
            if (IsAddressField(combinedText))
                return ADDRESS;

            // Check for specific format fields
            if (IsEmailField(combinedText))
                return EMAIL;
            if (IsPhoneField(combinedText))
                return PHONE;
            if (IsDateField(combinedText))
                return DATE;
            if (IsTimeField(combinedText))
                return TIME;
            if (IsDateTimeField(combinedText))
                return DATETIME;

            // Check for location fields
            if (IsZipField(combinedText))
                return ZIP;
            if (IsStateField(combinedText))
                return STATE;
            if (IsCountryField(combinedText))
                return COUNTRY;

            // Check for input control types
            if (IsCheckboxField(combinedText, fieldValue))
                return CHECKBOX;
            if (IsRadioField(combinedText))
                return RADIO;
            if (IsDropdownField(combinedText))
                return DROPDOWN;
            if (IsTextAreaField(combinedText))
                return TEXTAREA;
            if (IsSignatureField(combinedText))
                return SIGNATURE;
            if (IsFileUploadField(combinedText))
                return FILE_UPLOAD;

            // Check for numeric types
            if (IsCurrencyField(combinedText))
                return CURRENCY;
            if (IsPercentageField(combinedText))
                return PERCENTAGE;
            if (IsNumberField(combinedText))
                return NUMBER;

            // Check for media types
            if (IsImageField(combinedText))
                return IMAGE;
            if (IsBarcodeField(combinedText))
                return BARCODE;
            if (IsQRCodeField(combinedText))
                return QR_CODE;

            // Check for interactive types
            if (IsRatingField(combinedText))
                return RATING;
            if (IsSliderField(combinedText))
                return SLIDER;
            if (IsColorPickerField(combinedText))
                return COLOR_PICKER;

            // Check for URL
            if (IsURLField(combinedText))
                return URL;

            // Default to text field
            return TEXT;
        }

        private static bool ContainsKeywords(string text, List<string> keywords)
        {
            return keywords.Any(keyword => text.Contains(keyword));
        }

        private static bool IsSSNField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[SSN]) ||
                   Regex.IsMatch(text, @"\b\d{3}-\d{2}-\d{4}\b") ||
                   Regex.IsMatch(text, @"xxx-xx-\d{4}");
        }

        private static bool IsEINField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[EIN]) ||
                   Regex.IsMatch(text, @"\b\d{2}-\d{7}\b");
        }

        private static bool IsPasswordField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[PASSWORD]);
        }

        private static bool IsPINField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[PIN]) ||
                   Regex.IsMatch(text, @"\b\d{4,6}\b");
        }

        private static bool IsEmailField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[EMAIL]) ||
                   Regex.IsMatch(text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        }

        private static bool IsPhoneField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[PHONE]) ||
                   Regex.IsMatch(text, @"\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}");
        }

        private static bool IsDateField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[DATE]) ||
                   Regex.IsMatch(text, @"\b(0?[1-9]|1[0-2])[/-](0?[1-9]|[12][0-9]|3[01])[/-](\d{2}|\d{4})\b");
        }

        private static bool IsTimeField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[TIME]) && 
                   !text.Contains("date");
        }

        private static bool IsDateTimeField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[DATETIME]) ||
                   (text.Contains("date") && text.Contains("time"));
        }

        private static bool IsZipField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[ZIP]) ||
                   Regex.IsMatch(text, @"\b\d{5}(-\d{4})?\b");
        }

        private static bool IsStateField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[STATE]) && 
                   !text.Contains("statement");
        }

        private static bool IsCountryField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[COUNTRY]);
        }

        private static bool IsNameField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[NAME]) && 
                   !text.Contains("filename") && 
                   !text.Contains("username");
        }

        private static bool IsAddressField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[ADDRESS]) && 
                   !text.Contains("email");
        }

        private static bool IsCheckboxField(string text, string value)
        {
            return ContainsKeywords(text, FieldTypeKeywords[CHECKBOX]) ||
                   text.Contains("☐") || 
                   text.Contains("□") ||
                   text.Contains("☑") ||
                   text.Contains("☒") ||
                   text.Contains("✓") ||
                   text.Contains("✔") ||
                   (value != null && (value == "true" || value == "false"));
        }

        private static bool IsRadioField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[RADIO]) ||
                   text.Contains("○") ||
                   text.Contains("◯") ||
                   text.Contains("●") ||
                   text.Contains("◉");
        }

        private static bool IsDropdownField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[DROPDOWN]);
        }

        private static bool IsTextAreaField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[TEXTAREA]);
        }

        private static bool IsSignatureField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[SIGNATURE]);
        }

        private static bool IsFileUploadField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[FILE_UPLOAD]);
        }

        private static bool IsCurrencyField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[CURRENCY]) ||
                   Regex.IsMatch(text, @"\$[\d,]+\.?\d*");
        }

        private static bool IsPercentageField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[PERCENTAGE]) ||
                   Regex.IsMatch(text, @"\d+\.?\d*\s*%");
        }

        private static bool IsNumberField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[NUMBER]) &&
                   !IsCurrencyField(text) &&
                   !IsPercentageField(text);
        }

        private static bool IsImageField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[IMAGE]);
        }

        private static bool IsBarcodeField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[BARCODE]);
        }

        private static bool IsQRCodeField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[QR_CODE]);
        }

        private static bool IsRatingField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[RATING]);
        }

        private static bool IsSliderField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[SLIDER]);
        }

        private static bool IsColorPickerField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[COLOR_PICKER]);
        }

        private static bool IsURLField(string text)
        {
            return ContainsKeywords(text, FieldTypeKeywords[URL]) ||
                   Regex.IsMatch(text, @"https?://");
        }

        /// <summary>
        /// Get validation pattern for field type
        /// </summary>
        public static string GetValidationPattern(string fieldType)
        {
            switch (fieldType)
            {
                case EMAIL:
                    return @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
                case PHONE:
                    return @"^\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}$";
                case SSN:
                    return @"^\d{3}-\d{2}-\d{4}$";
                case DATE:
                    return @"^(0?[1-9]|1[0-2])/(0?[1-9]|[12][0-9]|3[01])/(\d{2}|\d{4})$";
                case ZIP:
                    return @"^\d{5}(-\d{4})?$";
                case EIN:
                    return @"^\d{2}-\d{7}$";
                case URL:
                    return @"^https?://[^\s]+$";
                case PIN:
                    return @"^\d{4,6}$";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Get field mask for formatting
        /// </summary>
        public static string GetFieldMask(string fieldType)
        {
            switch (fieldType)
            {
                case SSN:
                    return "999-99-9999";
                case PHONE:
                    return "(999) 999-9999";
                case DATE:
                    return "99/99/9999";
                case ZIP:
                    return "99999-9999";
                case EIN:
                    return "99-9999999";
                case PIN:
                    return "999999";
                default:
                    return null;
            }
        }
    }
}