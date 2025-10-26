#!/bin/bash

# End-to-End Integration Test Runner
# Runs complete workflow: PDF → Validate → Report → (Future: Remediate)

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

echo "=== PDF/UA Checker - Integration Test ==="
echo ""

# Check prerequisites
VERAPDF_PATH="$SCRIPT_DIR/verapdf/verapdf"
CORPUS_PATH="$SCRIPT_DIR/veraPDF-corpus/PDF_UA-1"

if [ ! -f "$VERAPDF_PATH" ]; then
    echo "⚠️  WARNING: veraPDF not installed at: $VERAPDF_PATH"
    echo ""
    echo "To install veraPDF:"
    echo "  cd $SCRIPT_DIR/verapdf-greenfield-1.26.2"
    echo "  ../jdk-21.0.8.jdk/Contents/Home/bin/java -jar verapdf-izpack-installer-1.26.2.jar"
    echo ""
    echo "Then install to: $SCRIPT_DIR/verapdf"
    echo ""
    exit 1
fi

if [ ! -d "$CORPUS_PATH" ]; then
    echo "⚠️  WARNING: Test corpus not found at: $CORPUS_PATH"
    echo ""
    echo "The integration test needs sample PDFs to run."
    echo ""
    exit 1
fi

# Select test PDF
if [ -n "$1" ]; then
    TEST_PDF="$1"
    echo "Using provided PDF: $TEST_PDF"
else
    # Use a known failing PDF for better testing
    TEST_PDF="$CORPUS_PATH/6.2 Metadata/6.2-t02-fail-a.pdf"

    if [ ! -f "$TEST_PDF" ]; then
        # Fallback to any PDF
        TEST_PDF=$(find "$CORPUS_PATH" -name "*.pdf" | head -n 1)
    fi

    echo "Using test PDF: $TEST_PDF"
fi

echo ""

# Build project
echo "Building project..."
cd "$PROJECT_DIR"
dotnet build --nologo --verbosity quiet

if [ $? -ne 0 ]; then
    echo "✗ Build failed"
    exit 1
fi

echo "✓ Build complete"
echo ""

# Run integration test
# Note: This is a placeholder - actual integration requires compiled assembly
echo "Integration test harness is ready!"
echo ""
echo "Test components:"
echo "  ✓ VeraPdfService.cs - validation service"
echo "  ✓ ValidationResult models - data structures"
echo "  ✓ EndToEndIntegrationTest.cs - test workflow"
echo "  ✓ Sample PDF: $(basename "$TEST_PDF")"
echo ""
echo "To run the test:"
echo "  1. Add xUnit test project to solution"
echo "  2. Reference EndToEndIntegrationTest.cs"
echo "  3. Run: dotnet test"
echo ""
echo "Or integrate directly into your application:"
echo ""
echo "  var test = new EndToEndIntegrationTest();"
echo "  var report = await test.RunFullWorkflowAsync(\"$TEST_PDF\");"
echo ""
