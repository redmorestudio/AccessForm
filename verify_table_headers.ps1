#!/usr/bin/env pwsh
# Quick validation script for table header scope attributes

param(
    [string]$PdfPath = "StateAssets/Wisconsin/Green Bay/green-bay-remote-work-form.pdf"
)

Write-Host "Checking table header accessibility in: $PdfPath" -ForegroundColor Cyan

# Run VeraPDF check specifically for table violations
$verapdfPath = "verapdf"
$profile = "PDFUA-1"

Write-Host "`nRunning VeraPDF validation..." -ForegroundColor Yellow
$verapdfResult = & $verapdfPath --format mrr --profile $profile $PdfPath 2>&1

# Check for 7.5-1 violations (table header scope)
$tableViolations = $verapdfResult | Select-String "7.5-1"

if ($tableViolations) {
    Write-Host "`n❌ Table header violations still present:" -ForegroundColor Red
    $tableViolations | ForEach-Object { Write-Host "  $_" }
} else {
    Write-Host "`n✅ No table header scope violations detected!" -ForegroundColor Green
}

# Check for 7.2 violations (table structure)
$structureViolations = $verapdfResult | Select-String "7.2"

if ($structureViolations) {
    Write-Host "`n⚠️ Table structure issues:" -ForegroundColor Yellow
    $structureViolations | ForEach-Object { Write-Host "  $_" }
}

Write-Host "`nAdobe Acrobat Quick Fixes:" -ForegroundColor Cyan
Write-Host "1. Open PDF in Acrobat Pro"
Write-Host "2. View → Navigation Panes → Tags"
Write-Host "3. Find <Table> tag and expand"
Write-Host "4. Right-click first row cells → Properties"
Write-Host "5. Change to 'Table Header Cell'"
Write-Host "6. Add Scope attribute = 'Column'"
Write-Host "7. Save and re-run this script"