using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AccessFormServer.Services;
using WordToPdfConverter.Models;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Multi-stage field validation service using Claude Sonnet 4.5 + GPT-5 consensus
    /// Implements 3-stage validation: completeness, spurious detection, label correction
    /// </summary>
    public class MultiStageValidationService
    {
        private readonly ILogger<MultiStageValidationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly OpenAIService _openAIService;
        private readonly System.Net.Http.HttpClient _httpClient;

        // Configuration thresholds
        private readonly float _addMissingFieldThreshold;
        private readonly float _removeSpuriousFieldThreshold;
        private readonly float _correctLabelThreshold;

        public MultiStageValidationService(
            ILogger<MultiStageValidationService> logger,
            IConfiguration configuration,
            OpenAIService openAIService,
            System.Net.Http.HttpClient httpClient)
        {
            _logger = logger;
            _configuration = configuration;
            _openAIService = openAIService;
            _httpClient = httpClient;

            // Load thresholds from config
            _addMissingFieldThreshold = _configuration.GetValue<float>("FieldDetection:MultiStageValidation:ConfidenceThresholds:AddMissingField", 0.8f);
            _removeSpuriousFieldThreshold = _configuration.GetValue<float>("FieldDetection:MultiStageValidation:ConfidenceThresholds:RemoveSpuriousField", 0.7f);
            _correctLabelThreshold = _configuration.GetValue<float>("FieldDetection:MultiStageValidation:ConfidenceThresholds:CorrectLabel", 0.6f);

            _logger.LogInformation("[MULTI-STAGE] Service initialized with thresholds: AddMissing={AddThreshold}, RemoveSpurious={RemoveThreshold}, CorrectLabel={CorrectThreshold}",
                _addMissingFieldThreshold, _removeSpuriousFieldThreshold, _correctLabelThreshold);
        }

        /// <summary>
        /// Main entry point: validate fields using multi-stage pipeline
        /// </summary>
        public async Task<List<FieldDetectionResult>> ValidateFieldsAsync(
            List<FieldDetectionResult> detectedFields,
            byte[] pdfBytes,
            FieldDetectionConfig config)
        {
            _logger.LogInformation("[MULTI-STAGE] ===== Starting Multi-Stage Validation Pipeline =====");
            _logger.LogInformation("[MULTI-STAGE] Input: {FieldCount} detected fields", detectedFields.Count);

            var useClaudeValidation = _configuration.GetValue<bool>("FieldDetection:MultiStageValidation:UseClaudeValidation", true);
            var useOpenAIValidation = _configuration.GetValue<bool>("FieldDetection:MultiStageValidation:UseOpenAIValidation", true);

            if (!useClaudeValidation && !useOpenAIValidation)
            {
                _logger.LogWarning("[MULTI-STAGE] Both Claude and OpenAI validation disabled, returning original fields");
                return detectedFields;
            }

            // Process each page separately
            var fieldsByPage = detectedFields.GroupBy(f => f.PageNumber).OrderBy(g => g.Key).ToList();
            var validatedFields = new List<FieldDetectionResult>();

            foreach (var pageGroup in fieldsByPage)
            {
                var pageNumber = pageGroup.Key;
                var pageFields = pageGroup.ToList();

                _logger.LogInformation("[MULTI-STAGE] Processing page {PageNumber} with {FieldCount} fields", pageNumber, pageFields.Count);

                // Render annotated image for this page
                var annotatedImageBytes = await RenderAnnotatedPageImage(pdfBytes, pageNumber - 1, pageFields);
                if (annotatedImageBytes == null)
                {
                    _logger.LogWarning("[MULTI-STAGE] Failed to render page {PageNumber}, keeping original fields", pageNumber);
                    validatedFields.AddRange(pageFields);
                    continue;
                }

                // Run 3-stage validation
                var stageResults = await RunThreeStageValidation(annotatedImageBytes, pageFields, useClaudeValidation, useOpenAIValidation);

                // Apply consensus resolution
                var updatedPageFields = ApplyConsensusResolution(pageFields, stageResults, pdfBytes, pageNumber);

                validatedFields.AddRange(updatedPageFields);
            }

            _logger.LogInformation("[MULTI-STAGE] ===== Validation Complete: {InputCount} → {OutputCount} fields =====",
                detectedFields.Count, validatedFields.Count);

            return validatedFields;
        }

        /// <summary>
        /// Run all 3 validation stages in parallel for both models
        /// </summary>
        private async Task<ThreeStageResults> RunThreeStageValidation(
            byte[] annotatedImageBytes,
            List<FieldDetectionResult> pageFields,
            bool useClaudeValidation,
            bool useOpenAIValidation)
        {
            var results = new ThreeStageResults();

            // Convert to ValidationFieldInfo for validation APIs
            var fieldInfoList = pageFields.Select(f => new ValidationFieldInfo
            {
                Id = f.ShortId ?? "F?",
                Label = f.FieldName,
                Type = f.FieldType,
                X = f.X,
                Y = f.Y,
                Width = f.Width,
                Height = f.Height
            }).ToList();

            // Stage 1: Completeness Validation
            _logger.LogInformation("[MULTI-STAGE] Stage 1: Completeness Validation");
            var completenessTask = RunCompletenessValidation(annotatedImageBytes, fieldInfoList, useClaudeValidation, useOpenAIValidation);

            // Stage 2: Spurious Field Detection
            _logger.LogInformation("[MULTI-STAGE] Stage 2: Spurious Field Detection");
            var spuriousTask = RunSpuriousValidation(annotatedImageBytes, fieldInfoList, useClaudeValidation, useOpenAIValidation);

            // Stage 3: Label Correction
            _logger.LogInformation("[MULTI-STAGE] Stage 3: Label Correction");
            var labelTask = RunLabelValidation(annotatedImageBytes, fieldInfoList, useClaudeValidation, useOpenAIValidation);

            // Wait for all stages to complete
            await Task.WhenAll(completenessTask, spuriousTask, labelTask);

            results.CompletenessResults = await completenessTask;
            results.SpuriousResults = await spuriousTask;
            results.LabelResults = await labelTask;

            return results;
        }

        private async Task<StageResult> RunCompletenessValidation(
            byte[] imageBytes,
            List<ValidationFieldInfo> fields,
            bool useClaude,
            bool useOpenAI)
        {
            var result = new StageResult { Stage = ValidationStage.Completeness };

            var tasks = new List<Task<ValidationResponse>>();

            if (useClaude)
            {
                tasks.Add(CallClaudeVisionAsync(imageBytes, fields, ValidationStage.Completeness));
            }

            if (useOpenAI)
            {
                tasks.Add(_openAIService.ValidateFieldsWithVision(imageBytes, fields, ValidationStage.Completeness));
            }

            var responses = await Task.WhenAll(tasks);

            result.ClaudeResponse = useClaude ? responses[0] : null;
            result.OpenAIResponse = useOpenAI ? responses[useClaude ? 1 : 0] : null;

            return result;
        }

        private async Task<StageResult> RunSpuriousValidation(
            byte[] imageBytes,
            List<ValidationFieldInfo> fields,
            bool useClaude,
            bool useOpenAI)
        {
            var result = new StageResult { Stage = ValidationStage.Spurious };

            var tasks = new List<Task<ValidationResponse>>();

            if (useClaude)
            {
                tasks.Add(CallClaudeVisionAsync(imageBytes, fields, ValidationStage.Spurious));
            }

            if (useOpenAI)
            {
                tasks.Add(_openAIService.ValidateFieldsWithVision(imageBytes, fields, ValidationStage.Spurious));
            }

            var responses = await Task.WhenAll(tasks);

            result.ClaudeResponse = useClaude ? responses[0] : null;
            result.OpenAIResponse = useOpenAI ? responses[useClaude ? 1 : 0] : null;

            return result;
        }

        private async Task<StageResult> RunLabelValidation(
            byte[] imageBytes,
            List<ValidationFieldInfo> fields,
            bool useClaude,
            bool useOpenAI)
        {
            var result = new StageResult { Stage = ValidationStage.LabelCorrection };

            var tasks = new List<Task<ValidationResponse>>();

            if (useClaude)
            {
                tasks.Add(CallClaudeVisionAsync(imageBytes, fields, ValidationStage.LabelCorrection));
            }

            if (useOpenAI)
            {
                tasks.Add(_openAIService.ValidateFieldsWithVision(imageBytes, fields, ValidationStage.LabelCorrection));
            }

            var responses = await Task.WhenAll(tasks);

            result.ClaudeResponse = useClaude ? responses[0] : null;
            result.OpenAIResponse = useOpenAI ? responses[useClaude ? 1 : 0] : null;

            return result;
        }

        /// <summary>
        /// Call Claude Sonnet 4.5 Vision API (similar to OpenAI but different format)
        /// </summary>
        private async Task<ValidationResponse> CallClaudeVisionAsync(
            byte[] imageBytes,
            List<ValidationFieldInfo> fields,
            ValidationStage stage)
        {
            try
            {
                var base64Image = Convert.ToBase64String(imageBytes);
                var fieldListJson = System.Text.Json.JsonSerializer.Serialize(fields, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });

                var prompt = stage switch
                {
                    ValidationStage.Completeness => BuildClaudeCompletenessPrompt(fieldListJson),
                    ValidationStage.Spurious => BuildClaudeSpuriousPrompt(fieldListJson),
                    ValidationStage.LabelCorrection => BuildClaudeLabelPrompt(fieldListJson),
                    _ => throw new ArgumentException($"Unknown stage: {stage}")
                };

                var requestBody = new
                {
                    model = "claude-sonnet-4-20250514",
                    max_tokens = 4096,
                    temperature = 0.0,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "image", source = new { type = "base64", media_type = "image/png", data = base64Image } },
                                new { type = "text", text = prompt }
                            }
                        }
                    }
                };

                var apiKey = _configuration["ApiKeys:Anthropic"] ?? _configuration["AnthropicApiKey"];
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Content = new System.Net.Http.StringContent(
                    System.Text.Json.JsonSerializer.Serialize(requestBody),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(responseContent);
                    var content = apiResponse.GetProperty("content")[0].GetProperty("text").GetString();

                    return ParseClaudeValidationResponse(content, stage);
                }
                else
                {
                    _logger.LogError("[MULTI-STAGE] Claude API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                    return new ValidationResponse { Stage = stage, Success = false };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MULTI-STAGE] Error calling Claude Vision API for stage {Stage}", stage);
                return new ValidationResponse { Stage = stage, Success = false };
            }
        }

        private string BuildClaudeCompletenessPrompt(string fieldListJson)
        {
            return $@"You are validating form field detection for completeness. I've detected these fields and drawn colored rectangles on the image:

{fieldListJson}

Your task: Look at the ENTIRE image and identify any fillable fields (input boxes, checkboxes, signature areas, date fields) that do NOT have a bounding box drawn on them.

Don't worry about whether labels are correct - just find MISSING fields.

Return ONLY valid JSON (no markdown):
{{
  ""missing_fields"": [
    {{
      ""location_description"": ""Bottom left, next to 'Signature:' text"",
      ""field_type"": ""signature"",
      ""estimated_bounds"": {{""x_percent"": 10, ""y_percent"": 85, ""width_percent"": 15, ""height_percent"": 5}},
      ""confidence"": 0.95,
      ""reasoning"": ""Large 'X' mark indicating signature field, no bounding box present""
    }}
  ]
}}

If no fields are missing, return: {{""missing_fields"": []}}";
        }

        private string BuildClaudeSpuriousPrompt(string fieldListJson)
        {
            return $@"You are reviewing detected form fields for false positives. I've drawn bounding boxes on these detected fields:

{fieldListJson}

Your task: Identify which of these detected fields are NOT actual fillable form fields.

Common false positives:
- Page numbers
- Logos or graphics
- Section headers/titles
- Decorative elements
- Static text labels

Return ONLY valid JSON (no markdown):
{{
  ""spurious_fields"": [
    {{
      ""id"": ""F7"",
      ""reason"": ""This is a page number, not a form field"",
      ""confidence"": 0.99
    }}
  ]
}}

If all fields are valid, return: {{""spurious_fields"": []}}";
        }

        private string BuildClaudeLabelPrompt(string fieldListJson)
        {
            return $@"You are validating form field labels. I've detected these fields with their current labels:

{fieldListJson}

Your task: For EACH field, verify if the label accurately describes what the form is asking for based on the nearby text. Look at the context around each field.

Common errors:
- Field labeled ""Gender Male"" when nearby text says ""Previous States""
- Field labeled ""Staff"" when it should be ""Date Signed""
- Field labeled ""Birth City"" when it should be ""License State""

Return ONLY valid JSON (no markdown):
{{
  ""corrections"": [
    {{
      ""id"": ""F3"",
      ""current_label"": ""Gender Male"",
      ""corrected_label"": ""Previous States"",
      ""current_type"": ""checkbox"",
      ""corrected_type"": ""text"",
      ""confidence"": 0.98,
      ""reasoning"": ""Field is next to text 'Previous States:' not gender options""
    }}
  ]
}}

If all labels are correct, return: {{""corrections"": []}}";
        }

        private ValidationResponse ParseClaudeValidationResponse(string responseText, ValidationStage stage)
        {
            var response = new ValidationResponse { Stage = stage, Success = true };

            try
            {
                // Clean up response - remove markdown if present
                var jsonStart = responseText.IndexOf('{');
                var jsonEnd = responseText.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    responseText = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                }

                var json = System.Text.Json.JsonDocument.Parse(responseText);
                var root = json.RootElement;

                switch (stage)
                {
                    case ValidationStage.Completeness:
                        if (root.TryGetProperty("missing_fields", out var missingFields))
                        {
                            response.MissingFields = ParseMissingFieldsFromClaude(missingFields);
                        }
                        break;

                    case ValidationStage.Spurious:
                        if (root.TryGetProperty("spurious_fields", out var spuriousFields))
                        {
                            response.SpuriousFields = ParseSpuriousFieldsFromClaude(spuriousFields);
                        }
                        break;

                    case ValidationStage.LabelCorrection:
                        if (root.TryGetProperty("corrections", out var corrections))
                        {
                            response.Corrections = ParseCorrectionsFromClaude(corrections);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MULTI-STAGE] Failed to parse Claude response for stage {Stage}", stage);
                response.Success = false;
            }

            return response;
        }

        private List<MissingField> ParseMissingFieldsFromClaude(System.Text.Json.JsonElement element)
        {
            var fields = new List<MissingField>();
            foreach (var item in element.EnumerateArray())
            {
                fields.Add(new MissingField
                {
                    LocationDescription = item.GetProperty("location_description").GetString(),
                    FieldType = item.GetProperty("field_type").GetString(),
                    Confidence = item.GetProperty("confidence").GetSingle(),
                    Reasoning = item.GetProperty("reasoning").GetString(),
                    EstimatedBounds = item.TryGetProperty("estimated_bounds", out var bounds)
                        ? new EstimatedBounds
                        {
                            XPercent = bounds.GetProperty("x_percent").GetSingle(),
                            YPercent = bounds.GetProperty("y_percent").GetSingle(),
                            WidthPercent = bounds.GetProperty("width_percent").GetSingle(),
                            HeightPercent = bounds.GetProperty("height_percent").GetSingle()
                        }
                        : null
                });
            }
            return fields;
        }

        private List<SpuriousField> ParseSpuriousFieldsFromClaude(System.Text.Json.JsonElement element)
        {
            var fields = new List<SpuriousField>();
            foreach (var item in element.EnumerateArray())
            {
                fields.Add(new SpuriousField
                {
                    Id = item.GetProperty("id").GetString(),
                    Reason = item.GetProperty("reason").GetString(),
                    Confidence = item.GetProperty("confidence").GetSingle()
                });
            }
            return fields;
        }

        private List<ValidationFieldCorrection> ParseCorrectionsFromClaude(System.Text.Json.JsonElement element)
        {
            var corrections = new List<ValidationFieldCorrection>();
            foreach (var item in element.EnumerateArray())
            {
                corrections.Add(new ValidationFieldCorrection
                {
                    Id = item.GetProperty("id").GetString(),
                    CurrentLabel = item.TryGetProperty("current_label", out var cl) ? cl.GetString() : null,
                    CorrectedLabel = item.GetProperty("corrected_label").GetString(),
                    CurrentType = item.TryGetProperty("current_type", out var ct) ? ct.GetString() : null,
                    CorrectedType = item.TryGetProperty("corrected_type", out var crt) ? crt.GetString() : null,
                    Confidence = item.GetProperty("confidence").GetSingle(),
                    Reasoning = item.GetProperty("reasoning").GetString()
                });
            }
            return corrections;
        }

        /// <summary>
        /// Apply consensus resolution using stack-ranked priorities
        /// </summary>
        private List<FieldDetectionResult> ApplyConsensusResolution(
            List<FieldDetectionResult> originalFields,
            ThreeStageResults stageResults,
            byte[] pdfBytes,
            int pageNumber)
        {
            var fields = new List<FieldDetectionResult>(originalFields);

            // PRIORITY 1: Add missing fields (either model finds with >threshold confidence)
            ApplyMissingFieldConsensus(fields, stageResults.CompletenessResults, pdfBytes, pageNumber);

            // PRIORITY 2: Remove spurious fields (both models must agree)
            ApplySpuriousFieldConsensus(fields, stageResults.SpuriousResults);

            // PRIORITY 3: Correct labels (prefer higher confidence, Claude wins ties)
            ApplyLabelCorrectionConsensus(fields, stageResults.LabelResults);

            return fields;
        }

        private void ApplyMissingFieldConsensus(
            List<FieldDetectionResult> fields,
            StageResult completenessResults,
            byte[] pdfBytes,
            int pageNumber)
        {
            var claudeMissing = completenessResults.ClaudeResponse?.MissingFields ?? new List<MissingField>();
            var openAIMissing = completenessResults.OpenAIResponse?.MissingFields ?? new List<MissingField>();

            _logger.LogInformation("[MULTI-STAGE] [COMPLETENESS] Claude found {ClaudeCount} missing, OpenAI found {OpenAICount} missing",
                claudeMissing.Count, openAIMissing.Count);

            // Combine and deduplicate missing fields
            var allMissing = claudeMissing.Concat(openAIMissing).ToList();

            foreach (var missing in allMissing)
            {
                if (missing.Confidence >= _addMissingFieldThreshold)
                {
                    // Get PDF page dimensions for coordinate calculation
                    var (pageWidth, pageHeight) = GetPageDimensions(pdfBytes, pageNumber - 1);

                    // Convert percentage bounds to PDF coordinates
                    var bounds = missing.EstimatedBounds;
                    var newField = new FieldDetectionResult
                    {
                        ShortId = $"M{fields.Count + 1}",
                        FieldName = $"Missing {missing.FieldType}",
                        FieldType = missing.FieldType,
                        X = bounds != null ? (bounds.XPercent / 100f) * pageWidth : 50f,
                        Y = bounds != null ? (bounds.YPercent / 100f) * pageHeight : 50f,
                        Width = bounds != null ? (bounds.WidthPercent / 100f) * pageWidth : 100f,
                        Height = bounds != null ? (bounds.HeightPercent / 100f) * pageHeight : 30f,
                        PageNumber = pageNumber,
                        Source = "MultiStage-Missing",
                        Confidence = missing.Confidence,
                        IsValid = true,
                        HasValidCoordinates = true,
                        Tooltip = $"Added by multi-stage validation: {missing.Reasoning} [PAGE:{pageNumber}]"
                    };

                    fields.Add(newField);
                    _logger.LogInformation("[MULTI-STAGE] [COMPLETENESS] ✅ Added missing field: {FieldType} at ({X:F1}, {Y:F1}) - {Reasoning}",
                        missing.FieldType, newField.X, newField.Y, missing.Reasoning);
                }
            }
        }

        private void ApplySpuriousFieldConsensus(
            List<FieldDetectionResult> fields,
            StageResult spuriousResults)
        {
            var claudeSpurious = spuriousResults.ClaudeResponse?.SpuriousFields ?? new List<SpuriousField>();
            var openAISpurious = spuriousResults.OpenAIResponse?.SpuriousFields ?? new List<SpuriousField>();

            _logger.LogInformation("[MULTI-STAGE] [SPURIOUS] Claude flagged {ClaudeCount}, OpenAI flagged {OpenAICount}",
                claudeSpurious.Count, openAISpurious.Count);

            // Only remove if BOTH models agree
            var claudeIds = new HashSet<string>(claudeSpurious.Where(s => s.Confidence >= _removeSpuriousFieldThreshold).Select(s => s.Id));
            var openAIIds = new HashSet<string>(openAISpurious.Where(s => s.Confidence >= _removeSpuriousFieldThreshold).Select(s => s.Id));
            var agreedSpurious = claudeIds.Intersect(openAIIds).ToList();

            foreach (var spuriousId in agreedSpurious)
            {
                var fieldToRemove = fields.FirstOrDefault(f => f.ShortId == spuriousId);
                if (fieldToRemove != null)
                {
                    var claudeReason = claudeSpurious.FirstOrDefault(s => s.Id == spuriousId)?.Reason ?? "";
                    var openAIReason = openAISpurious.FirstOrDefault(s => s.Id == spuriousId)?.Reason ?? "";

                    fields.Remove(fieldToRemove);
                    _logger.LogInformation("[MULTI-STAGE] [SPURIOUS] 🗑️ Removed spurious field {FieldId}: {FieldName} - Claude: {ClaudeReason}, OpenAI: {OpenAIReason}",
                        spuriousId, fieldToRemove.FieldName, claudeReason, openAIReason);
                }
            }
        }

        private void ApplyLabelCorrectionConsensus(
            List<FieldDetectionResult> fields,
            StageResult labelResults)
        {
            var claudeCorrections = labelResults.ClaudeResponse?.Corrections ?? new List<ValidationFieldCorrection>();
            var openAICorrections = labelResults.OpenAIResponse?.Corrections ?? new List<ValidationFieldCorrection>();

            _logger.LogInformation("[MULTI-STAGE] [LABELS] Claude proposed {ClaudeCount} corrections, OpenAI proposed {OpenAICount}",
                claudeCorrections.Count, openAICorrections.Count);

            // Build correction map by field ID
            var correctionMap = new Dictionary<string, (ValidationFieldCorrection claude, ValidationFieldCorrection openai)>();

            foreach (var correction in claudeCorrections.Where(c => c.Confidence >= _correctLabelThreshold))
            {
                if (!correctionMap.ContainsKey(correction.Id))
                    correctionMap[correction.Id] = (correction, null);
                else
                    correctionMap[correction.Id] = (correction, correctionMap[correction.Id].openai);
            }

            foreach (var correction in openAICorrections.Where(c => c.Confidence >= _correctLabelThreshold))
            {
                if (!correctionMap.ContainsKey(correction.Id))
                    correctionMap[correction.Id] = (null, correction);
                else
                    correctionMap[correction.Id] = (correctionMap[correction.Id].claude, correction);
            }

            // Apply corrections - consensus or high-confidence single model
            foreach (var kvp in correctionMap)
            {
                var fieldId = kvp.Key;
                var (claude, openai) = kvp.Value;
                var field = fields.FirstOrDefault(f => f.ShortId == fieldId);

                if (field == null) continue;

                var oldLabel = field.FieldName;

                // CASE 1: Both models proposed corrections
                if (claude != null && openai != null)
                {
                    // Both models must agree on the new label (using flexible string matching)
                    if (LabelsAgree(claude.CorrectedLabel, openai.CorrectedLabel))
                    {
                        // ONLY change the FieldName - NEVER touch coordinates
                        // Prefer Claude's label if they're similar but not exact
                        field.FieldName = claude.CorrectedLabel;

                        // Only change type if both agree exactly
                        if (!string.IsNullOrEmpty(claude.CorrectedType) && claude.CorrectedType == openai.CorrectedType)
                            field.FieldType = claude.CorrectedType;

                        field.Source += "+MultiStage";

                        _logger.LogInformation("[MULTI-STAGE] [LABELS] ✅ Both models agree: '{OldLabel}' → '{NewLabel}' (Claude: '{ClaudeLabel}', OpenAI: '{OpenAILabel}', conf={ClaudeConf:F2}/{OpenAIConf:F2})",
                            oldLabel, field.FieldName, claude.CorrectedLabel, openai.CorrectedLabel, claude.Confidence, openai.Confidence);
                    }
                    else
                    {
                        // Models disagree - DO NOTHING (don't make the change)
                        _logger.LogWarning("[MULTI-STAGE] [LABELS] ❌ Models disagree, skipping: Claude wants '{ClaudeLabel}' (conf={ClaudeConf:F2}), OpenAI wants '{OpenAILabel}' (conf={OpenAIConf:F2}) - keeping '{OldLabel}'",
                            claude.CorrectedLabel, claude.Confidence, openai.CorrectedLabel, openai.Confidence, oldLabel);
                    }
                }
                // CASE 2: Only one model proposed a correction - allow if confidence ≥ 0.9
                else
                {
                    var singleModel = claude != null ? "Claude" : "OpenAI";
                    var correction = claude ?? openai;
                    var proposedLabel = correction.CorrectedLabel;
                    var confidence = correction.Confidence;

                    if (confidence >= 0.9f)
                    {
                        // High confidence single model - apply the change
                        field.FieldName = proposedLabel;

                        if (!string.IsNullOrEmpty(correction.CorrectedType))
                            field.FieldType = correction.CorrectedType;

                        field.Source += "+MultiStage";

                        _logger.LogInformation("[MULTI-STAGE] [LABELS] ✅ Single model ({Model}) high confidence: '{OldLabel}' → '{NewLabel}' (conf={Conf:F2})",
                            singleModel, oldLabel, field.FieldName, confidence);
                    }
                    else
                    {
                        // Low confidence - skip
                        _logger.LogWarning("[MULTI-STAGE] [LABELS] ❌ Single model ({Model}) low confidence ({Conf:F2}), skipping: '{OldLabel}' → '{ProposedLabel}'",
                            singleModel, confidence, oldLabel, proposedLabel);
                    }
                }
            }
        }

        /// <summary>
        /// Check if two labels "agree" using flexible string matching
        /// Considers labels as agreeing when:
        /// - Exact match (case-insensitive)
        /// - One is a substring of the other
        /// - They have substantial overlap (>= 80% similarity)
        /// </summary>
        private bool LabelsAgree(string label1, string label2)
        {
            if (string.IsNullOrWhiteSpace(label1) || string.IsNullOrWhiteSpace(label2))
                return false;

            // Normalize: trim and lowercase
            var l1 = label1.Trim().ToLowerInvariant();
            var l2 = label2.Trim().ToLowerInvariant();

            // Exact match
            if (l1 == l2)
            {
                _logger.LogDebug("[MULTI-STAGE] Labels agree (exact): '{L1}' == '{L2}'", label1, label2);
                return true;
            }

            // Subset relationship: one contains the other
            if (l1.Contains(l2) || l2.Contains(l1))
            {
                _logger.LogDebug("[MULTI-STAGE] Labels agree (subset): '{L1}' ↔ '{L2}'", label1, label2);
                return true;
            }

            // String similarity using Levenshtein distance
            var similarity = CalculateStringSimilarity(l1, l2);
            if (similarity >= 0.8) // 80% similarity threshold
            {
                _logger.LogDebug("[MULTI-STAGE] Labels agree (similarity={Similarity:F2}): '{L1}' ≈ '{L2}'", similarity, label1, label2);
                return true;
            }

            _logger.LogDebug("[MULTI-STAGE] Labels disagree (similarity={Similarity:F2}): '{L1}' ≠ '{L2}'", similarity, label1, label2);
            return false;
        }

        /// <summary>
        /// Calculate string similarity using normalized Levenshtein distance
        /// Returns a value from 0.0 (completely different) to 1.0 (identical)
        /// </summary>
        private double CalculateStringSimilarity(string s1, string s2)
        {
            var distance = LevenshteinDistance(s1, s2);
            var maxLength = Math.Max(s1.Length, s2.Length);
            if (maxLength == 0) return 1.0;

            return 1.0 - ((double)distance / maxLength);
        }

        /// <summary>
        /// Calculate Levenshtein distance (edit distance) between two strings
        /// </summary>
        private int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            var distance = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                distance[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++)
                distance[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    var cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost
                    );
                }
            }

            return distance[s1.Length, s2.Length];
        }

        /// <summary>
        /// Render annotated PDF page with field boxes and labels
        /// Reuses existing logic from ConfigurableFieldDetectionService
        /// </summary>
        private async Task<byte[]> RenderAnnotatedPageImage(byte[] pdfBytes, int pageIndex, List<FieldDetectionResult> fields)
        {
            try
            {
                using var bitmap = PDFtoImage.Conversion.ToImage(pdfBytes, pageIndex,
                    options: new PDFtoImage.RenderOptions
                    {
                        Dpi = 150,
                        AntiAliasing = PDFtoImage.PdfAntiAliasing.All
                    });

                if (bitmap == null) return null;

                using var skBitmap = SkiaSharp.SKBitmap.FromImage(SkiaSharp.SKImage.FromBitmap(bitmap));
                using var canvas = new SkiaSharp.SKCanvas(skBitmap);

                // Draw field annotations
                int fieldIndex = 0;
                foreach (var field in fields)
                {
                    fieldIndex++;
                    var fieldId = field.ShortId ?? $"F{fieldIndex}";

                    var scale = 150f / 72f;
                    var rect = new SkiaSharp.SKRect(
                        field.X * scale,
                        field.Y * scale,
                        (field.X + field.Width) * scale,
                        (field.Y + field.Height) * scale
                    );

                    using var fieldPaint = new SkiaSharp.SKPaint
                    {
                        Color = SkiaSharp.SKColors.Yellow.WithAlpha(100),
                        Style = SkiaSharp.SKPaintStyle.Fill
                    };
                    canvas.DrawRect(rect, fieldPaint);

                    using var borderPaint = new SkiaSharp.SKPaint
                    {
                        Color = SkiaSharp.SKColors.Red,
                        Style = SkiaSharp.SKPaintStyle.Stroke,
                        StrokeWidth = 2
                    };
                    canvas.DrawRect(rect, borderPaint);

                    var label = $"{fieldId}: {field.FieldName}";
                    using var textPaint = new SkiaSharp.SKPaint
                    {
                        Color = SkiaSharp.SKColors.Black,
                        TextSize = 12,
                        IsAntialias = true,
                        Typeface = SkiaSharp.SKTypeface.FromFamilyName("Arial", SkiaSharp.SKFontStyle.Bold)
                    };

                    var textBounds = new SkiaSharp.SKRect();
                    textPaint.MeasureText(label, ref textBounds);
                    var textX = rect.Left;
                    var textY = rect.Top - 5;

                    using var bgPaint = new SkiaSharp.SKPaint
                    {
                        Color = SkiaSharp.SKColors.White.WithAlpha(200),
                        Style = SkiaSharp.SKPaintStyle.Fill
                    };
                    canvas.DrawRect(new SkiaSharp.SKRect(textX, textY - textBounds.Height - 2,
                                                   textX + textBounds.Width + 4, textY + 2), bgPaint);

                    canvas.DrawText(label, textX + 2, textY, textPaint);
                }

                using var image = SkiaSharp.SKImage.FromBitmap(skBitmap);
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
                return data.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MULTI-STAGE] Error rendering annotated image for page {PageIndex}", pageIndex);
                return null;
            }
        }

        private (float width, float height) GetPageDimensions(byte[] pdfBytes, int pageIndex)
        {
            try
            {
                using var stream = new System.IO.MemoryStream(pdfBytes);
                using var pdfDoc = new Syncfusion.Pdf.Parsing.PdfLoadedDocument(stream);

                if (pageIndex >= 0 && pageIndex < pdfDoc.Pages.Count)
                {
                    var page = pdfDoc.Pages[pageIndex];
                    return (page.Size.Width, page.Size.Height);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MULTI-STAGE] Error getting page dimensions");
            }

            return (612f, 792f); // Default letter size
        }
    }

    // Result container classes
    public class ThreeStageResults
    {
        public StageResult CompletenessResults { get; set; }
        public StageResult SpuriousResults { get; set; }
        public StageResult LabelResults { get; set; }
    }

    public class StageResult
    {
        public ValidationStage Stage { get; set; }
        public ValidationResponse ClaudeResponse { get; set; }
        public ValidationResponse OpenAIResponse { get; set; }
    }
}
