/private FormFieldDetectionResult GetFallbackFieldDetection()/,/^        }$/{
    /private FormFieldDetectionResult GetFallbackFieldDetection()/c\
        private FormFieldDetectionResult GetFallbackFieldDetection()\
        {\
            _logger.LogWarning("Using fallback field detection - Azure Form Recognizer unavailable");\
            \
            // Return EMPTY results - no simulation!\
            // This ensures we don't get fake fields when Azure fails\
            return new FormFieldDetectionResult\
            {\
                Success = false,\
                DetectedFields = new List<DetectedFormField>(),  // Empty - no simulation\
                TotalFields = 0,\
                ProcessingTime = DateTime.UtcNow,\
                Message = "Azure Form Recognizer unavailable - no fields detected"\
            };\
        }
    d
}
