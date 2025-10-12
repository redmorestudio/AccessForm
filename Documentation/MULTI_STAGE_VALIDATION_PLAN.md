# Multi-Stage AI Validation Pipeline Plan

## Document Information
- **Date**: 2025-01-03
- **Status**: Planning Phase
- **Authors**: Claude Code Session

## Executive Summary

This document outlines a comprehensive multi-stage validation pipeline that combines Claude Sonnet 4.5 and OpenAI GPT-5 Vision to dramatically improve form field detection accuracy. The system addresses three critical, stack-ranked goals through specialized validation stages.

---

## Current State

### What's Working
- **Syncfusion** detects basic form fields with reasonable accuracy
- **Claude Vision (Sonnet 4.5)** provides visual field detection with coordinate extraction
- **Sequential Processing** mode merges Syncfusion + Claude results
- **Signature Detection** now preserves visual signature fields (e.g., "Signature: X")

### Current Problems (Identified from User Testing)

#### Problem 1: Missing Signature Fields ❌
**Example**: Large "X" signature placeholders not being detected
- **Root Cause**: Sequential mode was discarding unmatched Claude Vision signature fields
- **Status**: FIXED in ConfigurableFieldDetectionService.cs:2055-2114

#### Problem 2: Wrong Field Labels ❌
**Example**: Field labeled "Gender Male" when it should be "Previous States"
- **Root Cause**: Using older Claude model (Sonnet 3.5) instead of Sonnet 4.5
- **Status**: FIXED - updated to claude-sonnet-4-20250514 in 3 locations

#### Problem 3: No Structured Context ❌
**Current Issue**: Vision models receive image + generic prompt, but NOT the detected field list
- **Impact**: Models can't verify if labels match positions or if fields are spurious
- **Example**: We can show the model an annotated image and ask "what's wrong?" and it spots errors, but it can't do this proactively without the field list

---

## Strategic Goals (Stack Ranked)

### 🎯 Goal #1: Complete Field Coverage
**Ensure every valid form field has a bounding box, even if incorrectly labeled**

**Why This Matters**:
- Users can manually correct labels later
- Missing fields are permanent data loss
- Better to have 100% coverage with some wrong labels than 80% coverage with perfect labels

**Success Metric**: 100% of visible form fields have bounding boxes

---

### 🎯 Goal #2: Eliminate Spurious Fields
**Remove parasitic/false-positive field detections**

**Why This Matters**:
- False positives create clutter and confuse users
- Page numbers, logos, decorative elements aren't form fields
- Reduces manual cleanup work

**Examples of Spurious Fields**:
- Page numbers detected as numeric fields
- Logos detected as image fields
- Section headers detected as text fields
- Decorative lines detected as signature fields

**Success Metric**: <5% false positive rate

---

### 🎯 Goal #3: Accurate Field Labels & Types
**Correctly identify what each field is for**

**Why This Matters**:
- Proper labels improve accessibility (screen readers)
- Correct types enable proper validation
- Better user experience

**Examples of Label Errors**:
- "Gender Male" → should be "Previous States"
- "Staff" → should be "Date Signed"
- "Birth City" → should be "License State"

**Success Metric**: >95% label accuracy

---

## Proposed Multi-Stage Validation Pipeline

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    PHASE 1: Detection                        │
│  Syncfusion + Claude Vision detect fields independently      │
│  Sequential mode merges results                              │
└─────────────────────┬───────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────┐
│            PHASE 2: Structured Validation                    │
│                                                               │
│  ┌──────────────────────┐      ┌──────────────────────┐    │
│  │  Claude Sonnet 4.5   │      │   GPT-5 Vision       │    │
│  │  (Heavyweight)       │      │   (Heavyweight)      │    │
│  ├──────────────────────┤      ├──────────────────────┤    │
│  │ Input:               │      │ Input:               │    │
│  │ • Annotated image    │      │ • Annotated image    │    │
│  │ • Field list (JSON)  │      │ • Field list (JSON)  │    │
│  ├──────────────────────┤      ├──────────────────────┤    │
│  │ Task:                │      │ Task:                │    │
│  │ • Find missing fields│      │ • Find missing fields│    │
│  │ • Mark spurious ones │      │ • Mark spurious ones │    │
│  │ • Correct labels     │      │ • Correct labels     │    │
│  └──────────┬───────────┘      └──────────┬───────────┘    │
│             │                              │                 │
│             └──────────────┬───────────────┘                 │
└────────────────────────────┼─────────────────────────────────┘
                             ▼
┌─────────────────────────────────────────────────────────────┐
│              PHASE 3: Consensus Resolution                   │
│  Merge results from both models using confidence scoring     │
│  Apply stack-ranked priority (coverage > spurious > labels)  │
└─────────────────────────────────────────────────────────────┘
```

---

## Detailed Stage Specifications

### Stage 1: Completeness Validation (Both Models)

**Objective**: Ensure no fields are missing (Goal #1)

**Input**:
```json
{
  "image": "base64_encoded_annotated_pdf_page",
  "detected_fields": [
    {"id": "F1", "label": "Convictions", "type": "text", "x": 68, "y": 120, "width": 365, "height": 25},
    {"id": "F2", "label": "Conviction Dates", "type": "text", "x": 787, "y": 120, "width": 242, "height": 25}
  ]
}
```

**Prompt Template**:
```
You are validating form field detection. I've detected these fields and drawn colored rectangles on the image:

[FIELD_LIST]

Your task: Look at the ENTIRE image and identify any fillable fields (input boxes, checkboxes,
signature areas, date fields) that do NOT have a bounding box drawn on them.

Don't worry about whether labels are correct - just find MISSING fields.

Return JSON:
{
  "missing_fields": [
    {
      "location_description": "Bottom left, next to 'Signature:' text",
      "field_type": "signature",
      "estimated_bounds": {"x_percent": 10, "y_percent": 85, "width_percent": 15, "height_percent": 5},
      "confidence": 0.95,
      "reasoning": "Large 'X' mark indicating signature field, no bounding box present"
    }
  ]
}
```

**Expected Output**: List of missing fields with estimated coordinates

---

### Stage 2: Spurious Field Elimination (Both Models)

**Objective**: Remove false positives (Goal #2)

**Prompt Template**:
```
You are reviewing detected form fields. I've drawn bounding boxes on these detected fields:

[FIELD_LIST]

Your task: Identify which of these detected fields are NOT actual fillable form fields.

Common false positives:
- Page numbers
- Logos or graphics
- Section headers/titles
- Decorative elements
- Static text labels

Return JSON:
{
  "spurious_fields": [
    {
      "id": "F7",
      "reason": "This is a page number, not a form field",
      "confidence": 0.99
    }
  ]
}
```

**Expected Output**: List of field IDs to remove

---

### Stage 3: Label & Type Correction (Both Models)

**Objective**: Fix incorrect labels and field types (Goal #3)

**Prompt Template**:
```
You are validating form field labels. I've detected these fields with their current labels:

[FIELD_LIST]

Your task: For EACH field, verify if the label accurately describes what the form is asking for
based on the nearby text. Look at the context around each field.

Common errors:
- Field labeled "Gender Male" when nearby text says "Previous States"
- Field labeled "Staff" when it should be "Date Signed"
- Field labeled "Birth City" when it should be "License State"

Return JSON:
{
  "corrections": [
    {
      "id": "F3",
      "current_label": "Gender Male",
      "corrected_label": "Previous States",
      "current_type": "checkbox",
      "corrected_type": "text",
      "confidence": 0.98,
      "reasoning": "Field is next to text 'Previous States:' not gender options"
    }
  ]
}
```

**Expected Output**: List of label/type corrections

---

## Consensus Resolution Logic

### Priority Rules (Applied in Order)

1. **Missing Fields (Goal #1)**: If either model finds a missing field with >0.8 confidence, ADD IT
2. **Spurious Fields (Goal #2)**: If both models agree field is spurious with >0.7 confidence, REMOVE IT
3. **Label Corrections (Goal #3)**: If models disagree, use higher confidence; if tie, prefer Claude

### Confidence Scoring

```csharp
public class ValidationConsensus
{
    public float CalculateConsensusConfidence(float claudeConfidence, float gptConfidence)
    {
        // If both agree (within 0.2), boost confidence
        if (Math.Abs(claudeConfidence - gptConfidence) < 0.2)
        {
            return Math.Min(1.0f, (claudeConfidence + gptConfidence) / 2 + 0.1f);
        }

        // If disagree, use higher but don't boost
        return Math.Max(claudeConfidence, gptConfidence);
    }

    public bool ShouldAddMissingField(float consensusConfidence)
    {
        return consensusConfidence >= 0.8f;  // Goal #1 priority
    }

    public bool ShouldRemoveSpuriousField(float claudeConf, float gptConf)
    {
        return claudeConf >= 0.7f && gptConf >= 0.7f;  // Both must agree
    }

    public string ResolveLabelConflict(
        string claudeLabel, float claudeConf,
        string gptLabel, float gptConf)
    {
        if (claudeLabel == gptLabel) return claudeLabel;

        // Prefer higher confidence
        if (Math.Abs(claudeConf - gptConf) > 0.15)
        {
            return claudeConf > gptConf ? claudeLabel : gptLabel;
        }

        // Tie: prefer Claude (Sonnet 4.5 has been more accurate in testing)
        return claudeLabel;
    }
}
```

---

## Implementation Plan

### Phase 1: Infrastructure (Week 1)

**Task 1.1**: Create `OpenAIVisionService.cs`
```csharp
public class OpenAIVisionService
{
    // Mirror LlamaGroqService structure
    // Support vision + structured JSON input
    // Use model: "gpt-5-vision"
    // response_format: "json_object"
}
```

**Task 1.2**: Update `FieldDetectionConfig.cs`
```csharp
public class ServiceSelection
{
    // Existing fields...
    public bool UseOpenAIValidation { get; set; } = false;  // NEW
}
```

**Task 1.3**: Register service in `Program.cs`
```csharp
builder.Services.AddSingleton<OpenAIVisionService>();
```

---

### Phase 2: Validation Pipeline (Week 1-2)

**Task 2.1**: Create `MultiStageValidationService.cs`

Key methods:
- `ValidateCompleteness()` - Stage 1
- `IdentifySpuriousFields()` - Stage 2
- `CorrectLabelsAndTypes()` - Stage 3
- `ResolveConsensus()` - Merge results

**Task 2.2**: Update `ValidateFieldLabelsWithGroq()`

Rename to `ValidateFieldLabelsWithVision()` and:
- Add structured field list to prompt
- Support parallel Claude + GPT calls
- Return structured validation results

**Task 2.3**: Integrate into `DetectFieldsAsync()`

Replace single validation call with:
```csharp
// PHASE 5: Multi-stage validation
if (config.Services.UseClaudeValidation || config.Services.UseOpenAIValidation)
{
    var validation = await _multiStageValidation.ValidateFieldsAsync(
        detectedFields,
        pdfBytes,
        config);

    detectedFields = validation.UpdatedFields;
}
```

---

### Phase 3: Testing & Tuning (Week 2-3)

**Task 3.1**: Create test suite with known problematic forms
- Forms with signature fields
- Forms with wrong labels (documented examples)
- Forms with spurious detections

**Task 3.2**: Measure baseline vs. multi-stage accuracy
- Goal #1: Field coverage %
- Goal #2: False positive rate
- Goal #3: Label accuracy %

**Task 3.3**: Tune confidence thresholds based on results

**Task 3.4**: Add cost tracking for dual-model validation
- Log API costs per document
- Implement cost limits/warnings

---

## API Specifications

### Claude Sonnet 4.5 (Anthropic API)

**Current Usage**: All vision analysis
**Endpoint**: `https://api.anthropic.com/v1/messages`
**Model**: `claude-sonnet-4-20250514`

**Request Format**:
```json
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 4096,
  "temperature": 0.0,
  "messages": [{
    "role": "user",
    "content": [
      {
        "type": "image",
        "source": {
          "type": "base64",
          "media_type": "image/png",
          "data": "..."
        }
      },
      {
        "type": "text",
        "text": "..."
      }
    ]
  }]
}
```

**Files Using Claude**:
- `ConfigurableFieldDetectionService.cs:2777` (validation)
- `ClaudeVisionFieldDetector.cs:479` (field detection)
- `ClaudeBoundingBoxValidator.cs:75` (bbox validation)

**Status**: ✅ All updated to Sonnet 4.5

---

### GPT-5 Vision (OpenAI API)

**Planned Usage**: Parallel validation for consensus
**Endpoint**: `https://api.openai.com/v1/chat/completions`
**Model**: `gpt-5-vision`

**Request Format**:
```json
{
  "model": "gpt-5-vision",
  "messages": [{
    "role": "system",
    "content": "You are an expert at form field validation."
  }, {
    "role": "user",
    "content": [
      {
        "type": "text",
        "text": "..."
      },
      {
        "type": "image_url",
        "image_url": {
          "url": "data:image/png;base64,..."
        }
      }
    ]
  }],
  "max_tokens": 4096,
  "temperature": 0.0,
  "response_format": { "type": "json_object" }
}
```

**Status**: 🔨 To be implemented

---

## Cost Estimates

### Per-Document Processing

**Claude Sonnet 4.5**:
- Input: ~1,000 tokens (image + prompt + field list)
- Output: ~500 tokens (validation results)
- Cost: ~$0.015 per validation call
- Calls per document: 3 (completeness, spurious, labels) = **$0.045**

**GPT-5 Vision**:
- Similar token usage
- Estimated cost: ~$0.020 per validation call
- Calls per document: 3 = **$0.060**

**Total per document**: ~**$0.105** for full multi-stage validation

**Annual volume estimate** (10,000 docs): ~$1,050

---

## Success Criteria

### Quantitative Metrics

| Goal | Current State | Target | Measurement Method |
|------|--------------|--------|-------------------|
| Field Coverage | ~85% | >98% | Manual review of test set |
| False Positive Rate | ~15% | <5% | Count spurious detections |
| Label Accuracy | ~80% | >95% | Compare to ground truth |

### Qualitative Criteria

- ✅ No more "how the hell do you miss a big X" moments
- ✅ Users trust the system enough to use it in production
- ✅ Manual correction time reduced by >80%

---

## Risks & Mitigations

### Risk 1: API Costs Too High
**Mitigation**:
- Add cost tracking per document
- Make dual validation optional (config flag)
- Fall back to single-model validation if budget exceeded

### Risk 2: Models Still Miss Fields
**Mitigation**:
- Add manual review UI for low-confidence results
- Implement feedback loop to improve prompts
- Consider adding third model (DeepSeek) as tiebreaker

### Risk 3: Consensus Logic Flawed
**Mitigation**:
- Extensive testing with known-bad examples
- Log all disagreements for analysis
- Make consensus rules configurable

---

## Configuration

### appsettings.json Updates

```json
{
  "AiServices": {
    "OpenAI": {
      "Enabled": true,
      "ApiKey": "sk-proj-...",
      "Model": "gpt-5-vision",
      "BaseUrl": "https://api.openai.com/v1",
      "MaxTokens": 4096,
      "Temperature": 0.0
    }
  },
  "FieldDetection": {
    "Validation": {
      "UseMultiStage": true,
      "UseClaudeValidation": true,
      "UseOpenAIValidation": true,
      "ConsensusMode": "weighted",  // weighted, unanimous, majority
      "MinConfidenceThresholds": {
        "AddMissingField": 0.8,
        "RemoveSpuriousField": 0.7,
        "CorrectLabel": 0.6
      }
    }
  }
}
```

---

## Timeline

### Week 1 (Jan 6-12, 2025)
- [x] ✅ Update all Claude APIs to Sonnet 4.5
- [x] ✅ Add OpenAI API key to config
- [x] ✅ Write implementation plan (this document)
- [ ] 🔨 Create OpenAIVisionService
- [ ] 🔨 Create MultiStageValidationService skeleton

### Week 2 (Jan 13-19, 2025)
- [ ] Implement completeness validation
- [ ] Implement spurious field detection
- [ ] Implement label correction
- [ ] Add consensus resolution logic

### Week 3 (Jan 20-26, 2025)
- [ ] Integration testing
- [ ] Performance tuning
- [ ] Cost tracking implementation
- [ ] Documentation updates

### Week 4 (Jan 27-Feb 2, 2025)
- [ ] Production deployment
- [ ] Monitor real-world accuracy
- [ ] Gather user feedback
- [ ] Iterate on prompts/thresholds

---

## Open Questions

1. **Should we render field IDs on the image for validation?**
   - Pro: Models can reference specific fields more easily
   - Con: May visually clutter the image
   - **Decision**: YES - helps models give precise feedback

2. **Should we validate all pages simultaneously or one at a time?**
   - Current: One page at a time
   - Pro of batch: More context, fewer API calls
   - Con of batch: Larger images, higher token costs
   - **Decision**: Keep one-at-a-time for now, revisit if accuracy issues

3. **How do we handle fields that span multiple pages?**
   - Current: Not supported
   - **Decision**: Out of scope for v1, document as known limitation

4. **Should validation be synchronous or async with user review?**
   - Current: Synchronous (blocks PDF generation)
   - Alternative: Generate PDF immediately, validate in background, show corrections UI
   - **Decision**: Keep synchronous for v1, async for v2

---

## Maintenance & Evolution

### Logging Requirements
- Log all validation calls with timestamps
- Log consensus conflicts with confidence scores
- Track accuracy metrics over time
- Alert on unusual patterns (sudden accuracy drop)

### Feedback Loop
- Allow users to report incorrect fields
- Store corrections for future training/fine-tuning
- Periodically review logs to identify prompt improvements

### Version History
- v1.0 (Current): Single Claude validation, no structured context
- v1.5 (This Plan): Multi-stage with Claude + GPT, structured field list
- v2.0 (Future): Add user feedback loop, async validation

---

## References

### Related Documents
- `README.md` - Project overview
- `SYSTEM_OVERVIEW.md` - Architecture documentation
- Field detection issues identified in user session (2025-01-03)

### Code Locations
- Detection: `ConfigurableFieldDetectionService.cs`
- Claude Vision: `ClaudeVisionFieldDetector.cs`
- Validation: `ConfigurableFieldDetectionService.cs:2566-2828`
- Config: `Models/FieldDetectionConfig.cs`

### External Resources
- [Claude API Docs](https://docs.anthropic.com/claude/reference/messages_post)
- [OpenAI Vision API](https://platform.openai.com/docs/guides/vision)
- [WCAG 2.1 AA Standards](https://www.w3.org/WAI/WCAG21/quickref/)

---

## Approval & Sign-off

- [ ] Technical review completed
- [ ] Cost analysis approved
- [ ] Implementation plan accepted
- [ ] Ready to begin development

**Next Steps**: Begin Phase 1 implementation - create OpenAIVisionService

---

*Document ends*
