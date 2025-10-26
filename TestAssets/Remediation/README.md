# Remediation Testing

Test directory for closed-loop PDF/UA remediation system.

## Quick Start

### 1. Add Your Test PDFs

```bash
# Copy PDFs that you know pass
cp /path/to/passing/*.pdf TestAssets/Remediation/Known-Pass/

# Copy PDFs that you know fail
cp /path/to/failing/*.pdf TestAssets/Remediation/Known-Fail/
```

### 2. Start the Server

```bash
ASPNETCORE_URLS="http://localhost:5001" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet
```

### 3. Run Tests

In a new terminal:

```bash
./TestAssets/Remediation/run-remediation-tests.sh
```

## Results

Test results are saved in `TestAssets/Remediation/Results/YYYY-MM-DD_HH-MM-SS/`

Each test creates:
- `input.pdf` - Original PDF
- `output.pdf` - Remediated PDF
- `api-response.json` - Full API response
- `result.json` - Test summary

## Directory Structure

```
TestAssets/Remediation/
├── README.md                    ← This file
├── Known-Pass/                  ← PDFs that are already compliant
│   └── (your passing PDFs)
├── Known-Fail/                  ← PDFs with violations
│   └── (your failing PDFs)
├── Results/                     ← Test results
│   └── 2025-10-22_14-30-00/
│       ├── summary.json
│       ├── test-001/
│       │   ├── input.pdf
│       │   ├── output.pdf
│       │   ├── api-response.json
│       │   └── result.json
│       └── test-002/
└── run-remediation-tests.sh     ← Test harness script
```

## Manual Testing

For manual testing through the web UI, see [TESTING-GUIDE.md](../../Services/Remediation/TESTING-GUIDE.md)
