#!/bin/bash

# Replace the remediation section
sed -i '' '834,841d' Program.cs
sed -i '' '833a\
        // Apply accessibility remediation\
        logger.LogInformation("Applying accessibility remediation");\
        using var remediationStream = new MemoryStream(normalPdfBytes);\
        using var remediatedDoc = new PdfLoadedDocument(remediationStream);\
        enhancer.EnhanceAccessibility(remediatedDoc, file.FileName);\
        using var remediatedOutputStream = new MemoryStream();\
        remediatedDoc.Save(remediatedOutputStream);\
        remediatedPdfBytes = remediatedOutputStream.ToArray();\
        remediatedDoc.Close(true);
' Program.cs

echo "Fixed enhancer usage"
