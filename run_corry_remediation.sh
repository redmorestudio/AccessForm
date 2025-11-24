#!/bin/bash
# Direct C# script execution - just call the damn service

cat > /tmp/remediate_corry.csx << 'CSHARP'
#r "nuget: Microsoft.Extensions.DependencyInjection, 8.0.0"
#r "nuget: Microsoft.Extensions.Logging.Console, 8.0.0"
#r "nuget: Microsoft.Extensions.Configuration.Json, 8.0.0"

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var inputPath = "StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0032_105 Corry.pdf";
var outputPath = "corry_FULL_remediation_output.pdf";

Console.WriteLine("Starting full remediation on Corry PDF...");

// Just call it via the web API since that's what's actually working
var pdfBytes = await File.ReadAllBytesAsync(inputPath);
var base64 = Convert.ToBase64String(pdfBytes);

// TODO: Make API call to server
Console.WriteLine($"PDF loaded: {pdfBytes.Length / 1024} KB");
Console.WriteLine("Need to call /api/remediate-pdf endpoint...");
CSHARP

dotnet-script /tmp/remediate_corry.csx
