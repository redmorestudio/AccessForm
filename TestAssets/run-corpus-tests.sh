#!/bin/bash

# veraPDF Corpus Test Runner
# Runs validation tests on all PDFs in the veraPDF corpus

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
CORPUS_PATH="$SCRIPT_DIR/veraPDF-corpus/PDF_UA-1"
VERAPDF_PATH="$SCRIPT_DIR/verapdf/verapdf"
JAVA_HOME="$SCRIPT_DIR/jdk-21.0.8.jdk/Contents/Home"

echo "=== veraPDF Corpus Test Runner ==="
echo ""
echo "Project Dir: $PROJECT_DIR"
echo "Corpus Path: $CORPUS_PATH"
echo "veraPDF: $VERAPDF_PATH"
echo "Java Home: $JAVA_HOME"
echo ""

# Check prerequisites
if [ ! -d "$CORPUS_PATH" ]; then
    echo "ERROR: Corpus not found at: $CORPUS_PATH"
    echo "The corpus should have been cloned. Check TestAssets/veraPDF-corpus/"
    exit 1
fi

if [ ! -f "$VERAPDF_PATH" ]; then
    echo "ERROR: veraPDF not found at: $VERAPDF_PATH"
    echo ""
    echo "Please install veraPDF manually - see VERAPDF-INSTALL.md"
    echo ""
    echo "Quick install:"
    echo "  cd $SCRIPT_DIR/verapdf-greenfield-1.26.2"
    echo "  $JAVA_HOME/bin/java -jar verapdf-izpack-installer-1.26.2.jar"
    echo ""
    echo "Then install to: $SCRIPT_DIR/verapdf"
    exit 1
fi

if [ ! -d "$JAVA_HOME" ]; then
    echo "ERROR: Java not found at: $JAVA_HOME"
    exit 1
fi

# Build the test harness (compile C# files)
echo "Building test harness..."
cd "$PROJECT_DIR"

# The test harness will be compiled as part of the main project
# For now, just verify the project builds
dotnet build --nologo --verbosity quiet

if [ $? -ne 0 ]; then
    echo "ERROR: Build failed"
    exit 1
fi

echo "Build complete."
echo ""

# Run the test harness
# Note: This will need to be adapted based on how we integrate it
# For now, provide instructions

echo "Test harness is ready!"
echo ""
echo "To run tests:"
echo "  1. Ensure veraPDF is installed at: $VERAPDF_PATH"
echo "  2. Run: dotnet run --project AccessFormServer.csproj"
echo "  3. Call the validation API endpoint"
echo ""
echo "Or integrate CorpusValidationHarness into your test suite."
echo ""
echo "Test corpus contains:"
find "$CORPUS_PATH" -name "*.pdf" | wc -l | xargs echo "  - PDFs:"
echo ""
