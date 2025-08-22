import re

with open('Services/AzureFormRecognizerService.cs', 'r') as f:
    lines = f.readlines()

# Find the start and end of the GetFallbackFieldDetection method
start_idx = None
end_idx = None
brace_count = 0
in_method = False

for i, line in enumerate(lines):
    if 'private FormFieldDetectionResult GetFallbackFieldDetection()' in line:
        start_idx = i
        in_method = True
        brace_count = 0
    elif in_method:
        brace_count += line.count('{') - line.count('}')
        if brace_count == 0 and '}' in line:
            end_idx = i
            break

if start_idx is not None and end_idx is not None:
    # Replace the method
    new_method = '''        private FormFieldDetectionResult GetFallbackFieldDetection()
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
        }
'''
    
    # Replace the lines
    new_lines = lines[:start_idx] + [new_method + '\n'] + lines[end_idx+1:]
    
    with open('Services/AzureFormRecognizerService.cs', 'w') as f:
        f.writelines(new_lines)
    
    print(f"Replaced method from line {start_idx+1} to {end_idx+1}")
else:
    print("Method not found!")
