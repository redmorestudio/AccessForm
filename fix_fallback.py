import re

# Read the file
with open('Services/AzureFormRecognizerService.cs', 'r') as f:
    content = f.read()

# Find and replace the GetFallbackFieldDetection method
pattern = r'private FormFieldDetectionResult GetFallbackFieldDetection\(\)\s*\{[^}]+(?:\{[^}]*\}[^}]*)*\}'

replacement = '''private FormFieldDetectionResult GetFallbackFieldDetection()
        {
            _logger.LogWarning("Using fallback field detection - Azure Form Recognizer unavailable");
            
            // Return EMPTY results - no simulation!
            // This ensures we don't get fake fields when Azure fails
            return new FormFieldDetectionResult
            {
                Success = false,
                DetectedFields = new List<DetectedFormField>(),  // Empty - no simulation
                TotalFields = 0,
                ProcessingTime = DateTime.UtcNow,
                Message = "Azure Form Recognizer unavailable - no fields detected"
            };
        }'''

# Replace the method
content = re.sub(pattern, replacement, content, flags=re.DOTALL)

# Write back
with open('Services/AzureFormRecognizerService.cs', 'w') as f:
    f.write(content)

print("Fixed GetFallbackFieldDetection method")
