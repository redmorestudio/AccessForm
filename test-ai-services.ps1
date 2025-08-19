#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Test script for AccessForm AI Services
.DESCRIPTION
    This script tests the AI-powered form remediation endpoints
.EXAMPLE
    ./test-ai-services.ps1
#>

$baseUrl = "http://localhost:5008"

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "   AccessForm AI Services Test Script" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Function to test endpoint
function Test-Endpoint {
    param(
        [string]$Url,
        [string]$Method = "GET",
        [string]$Description
    )
    
    Write-Host "Testing: $Description" -ForegroundColor Yellow
    Write-Host "URL: $Url" -ForegroundColor Gray
    
    try {
        if ($Method -eq "GET") {
            $response = Invoke-RestMethod -Uri $Url -Method $Method
            Write-Host "✅ Success" -ForegroundColor Green
            return $response
        }
        else {
            Write-Host "⏭️  Skipping POST test (requires file upload)" -ForegroundColor Yellow
            return $null
        }
    }
    catch {
        Write-Host "❌ Failed: $_" -ForegroundColor Red
        return $null
    }
}

# Test 1: Health Check
Write-Host "`n1. HEALTH CHECK" -ForegroundColor Cyan
Write-Host "=================" -ForegroundColor Cyan
$health = Test-Endpoint -Url "$baseUrl/api/health" -Description "Health Check Endpoint"
if ($health) {
    Write-Host "`nHealth Status:" -ForegroundColor White
    Write-Host "  Status: $($health.status)" -ForegroundColor Green
    
    Write-Host "`nAI Services Configuration:" -ForegroundColor White
    Write-Host "  Azure Form Recognizer:" -ForegroundColor Gray
    Write-Host "    - Enabled: $($health.services.azureFormRecognizer.enabled)" -ForegroundColor $(if($health.services.azureFormRecognizer.enabled){"Green"}else{"Yellow"})
    Write-Host "    - Configured: $($health.services.azureFormRecognizer.configured)" -ForegroundColor $(if($health.services.azureFormRecognizer.configured){"Green"}else{"Red"})
    
    Write-Host "  Llama Groq:" -ForegroundColor Gray
    Write-Host "    - Enabled: $($health.services.llamaGroq.enabled)" -ForegroundColor $(if($health.services.llamaGroq.enabled){"Green"}else{"Yellow"})
    Write-Host "    - Configured: $($health.services.llamaGroq.configured)" -ForegroundColor $(if($health.services.llamaGroq.configured){"Green"}else{"Red"})
    
    Write-Host "`nCost Tracking:" -ForegroundColor White
    Write-Host "  Daily Total: `$$($health.costTracking.dailyTotal)" -ForegroundColor Gray
    Write-Host "  Percent of Limit: $($health.costTracking.percentOfLimit)%" -ForegroundColor Gray
    Write-Host "  Requests Today: $($health.costTracking.requestsToday)" -ForegroundColor Gray
}

# Test 2: Cost Summary
Write-Host "`n2. COST SUMMARY" -ForegroundColor Cyan
Write-Host "=================" -ForegroundColor Cyan
$costs = Test-Endpoint -Url "$baseUrl/api/cost-summary" -Description "Cost Summary Endpoint"
if ($costs) {
    Write-Host "`nDaily Summary:" -ForegroundColor White
    Write-Host "  Date: $($costs.daily.date)" -ForegroundColor Gray
    Write-Host "  Total: `$$($costs.daily.total)" -ForegroundColor Gray
    Write-Host "  Requests: $($costs.daily.requestCount)" -ForegroundColor Gray
    Write-Host "  Percent of Limit: $($costs.daily.percentOfLimit)%" -ForegroundColor Gray
    
    Write-Host "`nWeekly Summary:" -ForegroundColor White
    Write-Host "  Total: `$$($costs.weekly.total)" -ForegroundColor Gray
    Write-Host "  Daily Average: `$$($costs.weekly.averageDaily)" -ForegroundColor Gray
    Write-Host "  Projected Monthly: `$$($costs.weekly.projectedMonthly)" -ForegroundColor Gray
}

# Test 3: File conversion endpoints
Write-Host "`n3. CONVERSION ENDPOINTS" -ForegroundColor Cyan
Write-Host "========================" -ForegroundColor Cyan

Write-Host "`nAvailable endpoints:" -ForegroundColor White
Write-Host "  • /api/convert - Standard conversion (algorithmic)" -ForegroundColor Gray
Write-Host "  • /api/convert-with-ai - AI-powered conversion (Azure + Llama)" -ForegroundColor Gray
Write-Host "  • /api/remediate-pdf - PDF remediation" -ForegroundColor Gray

# Create a test file if needed
$testFile = "test-form.docx"
if (Test-Path $testFile) {
    Write-Host "`n📄 Test file found: $testFile" -ForegroundColor Green
    
    Write-Host "`nTo test AI conversion, run:" -ForegroundColor Yellow
    Write-Host "  curl -X POST $baseUrl/api/convert-with-ai -F 'file=@$testFile' -o output_ai.json" -ForegroundColor Gray
    
    Write-Host "`nOr using PowerShell:" -ForegroundColor Yellow
    Write-Host @"
  `$form = @{
      file = Get-Item '$testFile'
  }
  `$response = Invoke-RestMethod -Uri '$baseUrl/api/convert-with-ai' -Method Post -Form `$form
  [System.IO.File]::WriteAllBytes('output_accessible.pdf', [Convert]::FromBase64String(`$response.accessiblePdf.data))
"@ -ForegroundColor Gray
}
else {
    Write-Host "`n⚠️  No test file found. Create a test Word document named '$testFile' to test conversion." -ForegroundColor Yellow
}

# Configuration check
Write-Host "`n4. CONFIGURATION STATUS" -ForegroundColor Cyan
Write-Host "========================" -ForegroundColor Cyan

$configFile = "appsettings.json"
if (Test-Path $configFile) {
    $config = Get-Content $configFile | ConvertFrom-Json
    
    Write-Host "`n✅ Configuration file found" -ForegroundColor Green
    
    # Check for API keys
    $azureKey = $config.AiServices.AzureFormRecognizer.ApiKey
    $groqKey = $config.AiServices.LlamaGroq.ApiKey
    
    if ($azureKey -eq "YOUR-AZURE-KEY-HERE") {
        Write-Host "⚠️  Azure Form Recognizer API key not set" -ForegroundColor Yellow
        Write-Host "   Update the key in appsettings.json" -ForegroundColor Gray
    }
    else {
        Write-Host "✅ Azure Form Recognizer API key configured" -ForegroundColor Green
    }
    
    if ($groqKey -eq "YOUR-GROQ-API-KEY-HERE") {
        Write-Host "⚠️  Groq API key not set" -ForegroundColor Yellow
        Write-Host "   Update the key in appsettings.json" -ForegroundColor Gray
    }
    else {
        Write-Host "✅ Groq API key configured" -ForegroundColor Green
    }
    
    Write-Host "`nCost Limits:" -ForegroundColor White
    Write-Host "  Daily Limit: `$$($config.AiServices.CostLimits.DailyLimit)" -ForegroundColor Gray
    Write-Host "  Alert Threshold: $($config.AiServices.CostLimits.AlertThreshold * 100)%" -ForegroundColor Gray
    Write-Host "  Cost per Azure call: `$$($config.AiServices.CostLimits.CostPerAzureCall)" -ForegroundColor Gray
    Write-Host "  Cost per Llama call: `$$($config.AiServices.CostLimits.CostPerLlamaCall)" -ForegroundColor Gray
}
else {
    Write-Host "❌ Configuration file not found!" -ForegroundColor Red
}

Write-Host "`n================================================" -ForegroundColor Cyan
Write-Host "   Test Complete" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Add your API keys to appsettings.json" -ForegroundColor White
Write-Host "2. Restore NuGet packages: dotnet restore" -ForegroundColor White
Write-Host "3. Run the server: dotnet run" -ForegroundColor White
Write-Host "4. Test with a real form document" -ForegroundColor White
Write-Host ""
