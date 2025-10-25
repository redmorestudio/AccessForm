using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.XMP;
using iText.Kernel.XMP.Properties;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Fixes
{
    /// <summary>
    /// Ensures PDF has proper PDF/UA metadata and conformance identifiers.
    /// This service adds or updates the XMP metadata to declare PDF/UA compliance.
    /// </summary>
    public class PdfUaMetadataService : IRemediationService
    {
        private readonly ILogger<PdfUaMetadataService> _logger;

        public string ServiceName => "PDF/UA Metadata Service";
        public ViolationCategory TargetCategory => ViolationCategory.Metadata;
        public int Priority => 1; // High priority - metadata should be set early
        public bool IsRequired => true; // Always ensure proper metadata

        public PdfUaMetadataService(ILogger<PdfUaMetadataService> logger)
        {
            _logger = logger;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ServiceResult
            {
                Success = false,
                OutputPdf = pdfBytes
            };

            try
            {
                _logger.LogInformation("[PDFUA-METADATA] Starting PDF/UA metadata remediation");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                var changesMade = false;

                // Ensure document is tagged
                if (!pdfDoc.IsTagged())
                {
                    _logger.LogInformation("[PDFUA-METADATA] Marking document as tagged");
                    pdfDoc.SetTagged();
                    changesMade = true;
                }

                // Set PDF/UA identifier
                changesMade |= await SetPdfUaIdentifier(pdfDoc);

                // Set document metadata
                changesMade |= await SetDocumentMetadata(pdfDoc);

                // Set catalog entries for PDF/UA
                changesMade |= await SetCatalogEntries(pdfDoc);

                // Set viewer preferences for accessibility
                changesMade |= await SetViewerPreferences(pdfDoc);

                pdfDoc.Close();

                if (changesMade)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = 1;
                    _logger.LogInformation("[PDFUA-METADATA] PDF/UA metadata updated successfully");
                }
                else
                {
                    _logger.LogInformation("[PDFUA-METADATA] PDF/UA metadata already properly configured");
                    result.Success = true;
                }

                stopwatch.Stop();
                _logger.LogInformation($"[PDFUA-METADATA] Completed in {stopwatch.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PDFUA-METADATA] Failed to set PDF/UA metadata");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<bool> SetPdfUaIdentifier(PdfDocument pdfDoc)
        {
            try
            {
                var xmpMeta = XMPMetaFactory.ParseFromBuffer(pdfDoc.GetXmpMetadata());

                if (xmpMeta == null)
                {
                    // Create new XMP metadata
                    _logger.LogInformation("[PDFUA-METADATA] Creating new XMP metadata");
                    xmpMeta = XMPMetaFactory.Create();
                }

                // Check for existing PDF/UA identifier
                var hasUaIdentifier = false;
                try
                {
                    var partValue = xmpMeta.GetPropertyInteger(
                        "http://www.aiim.org/pdfua/ns/id/",
                        "pdfuaid:part");
                    hasUaIdentifier = partValue != null && partValue == 1;
                }
                catch
                {
                    // Property doesn't exist
                }

                if (!hasUaIdentifier)
                {
                    _logger.LogInformation("[PDFUA-METADATA] Adding PDF/UA-1 identifier");

                    // Register namespace if needed
                    try
                    {
                        XMPMetaFactory.GetSchemaRegistry().RegisterNamespace(
                            "http://www.aiim.org/pdfua/ns/id/",
                            "pdfuaid");
                    }
                    catch
                    {
                        // Namespace might already be registered
                    }

                    // Set PDF/UA-1 identifier
                    xmpMeta.SetPropertyInteger(
                        "http://www.aiim.org/pdfua/ns/id/",
                        "pdfuaid:part",
                        1);

                    // Set revision if needed
                    xmpMeta.SetProperty(
                        "http://www.aiim.org/pdfua/ns/id/",
                        "pdfuaid:rev",
                        "2008");

                    // Save updated XMP
                    pdfDoc.SetXmpMetadata(xmpMeta);
                    return true;
                }

                _logger.LogInformation("[PDFUA-METADATA] PDF/UA identifier already present");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PDFUA-METADATA] Error setting PDF/UA identifier: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SetDocumentMetadata(PdfDocument pdfDoc)
        {
            var changesMade = false;

            try
            {
                var info = pdfDoc.GetDocumentInfo();

                // Ensure title is set (required for PDF/UA)
                if (string.IsNullOrWhiteSpace(info.GetTitle()))
                {
                    _logger.LogInformation("[PDFUA-METADATA] Setting document title");
                    info.SetTitle("Accessible PDF Document");
                    changesMade = true;
                }

                // Set language if not present
                var catalog = pdfDoc.GetCatalog();
                var lang = catalog.GetLang();
                if (lang == null || string.IsNullOrWhiteSpace(lang.GetValue()))
                {
                    _logger.LogInformation("[PDFUA-METADATA] Setting document language to en-US");
                    catalog.SetLang(new PdfString("en-US"));
                    changesMade = true;
                }

                // Set creator if not present
                if (string.IsNullOrWhiteSpace(info.GetCreator()))
                {
                    info.SetCreator("AccessForm PDF Converter");
                    changesMade = true;
                }

                // Set producer
                info.SetProducer("AccessForm PDF/UA Remediation Service");

                // Add creation/modification dates if missing - use SetMoreInfo which doesn't need GetPdfObject access
                try
                {
                    // Try to get existing creation date
                    var creationDate = info.GetMoreInfo(PdfName.CreationDate.GetValue());
                    if (string.IsNullOrEmpty(creationDate))
                    {
                        info.SetMoreInfo(PdfName.CreationDate.GetValue(), DateTime.UtcNow.ToString("D:yyyyMMddHHmmss"));
                        changesMade = true;
                    }
                }
                catch
                {
                    // If we can't get it, set it
                    info.SetMoreInfo(PdfName.CreationDate.GetValue(), DateTime.UtcNow.ToString("D:yyyyMMddHHmmss"));
                    changesMade = true;
                }

                info.SetMoreInfo(PdfName.ModDate.GetValue(), DateTime.UtcNow.ToString("D:yyyyMMddHHmmss"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PDFUA-METADATA] Error setting document metadata: {ex.Message}");
            }

            return await Task.FromResult(changesMade);
        }

        private async Task<bool> SetCatalogEntries(PdfDocument pdfDoc)
        {
            var changesMade = false;

            try
            {
                var catalog = pdfDoc.GetCatalog();
                var catalogDict = catalog.GetPdfObject();

                // Ensure MarkInfo dictionary exists and indicates structure
                var markInfo = catalogDict.GetAsDictionary(PdfName.MarkInfo);
                if (markInfo == null)
                {
                    _logger.LogInformation("[PDFUA-METADATA] Creating MarkInfo dictionary");
                    markInfo = new PdfDictionary();
                    catalogDict.Put(PdfName.MarkInfo, markInfo);
                    changesMade = true;
                }

                // Set Marked to true (document contains structure)
                if (!markInfo.GetAsBoolean(PdfName.Marked)?.GetValue() ?? true)
                {
                    _logger.LogInformation("[PDFUA-METADATA] Setting Marked flag to true");
                    markInfo.Put(PdfName.Marked, PdfBoolean.TRUE);
                    changesMade = true;
                }

                // Set UserProperties if needed (for custom tags)
                if (!markInfo.ContainsKey(new PdfName("UserProperties")))
                {
                    markInfo.Put(new PdfName("UserProperties"), PdfBoolean.FALSE);
                    changesMade = true;
                }

                // Set Suspects to false (no suspected issues)
                if (!markInfo.ContainsKey(new PdfName("Suspects")))
                {
                    markInfo.Put(new PdfName("Suspects"), PdfBoolean.FALSE);
                    changesMade = true;
                }

                // Ensure StructTreeRoot exists
                if (!catalogDict.ContainsKey(PdfName.StructTreeRoot))
                {
                    _logger.LogInformation("[PDFUA-METADATA] Creating StructTreeRoot");
                    var structTreeRoot = new PdfDictionary();
                    structTreeRoot.Put(PdfName.Type, PdfName.StructTreeRoot);
                    catalogDict.Put(PdfName.StructTreeRoot, structTreeRoot);
                    changesMade = true;
                }

                // Add OutputIntents for PDF/UA if not present
                var outputIntents = catalogDict.GetAsArray(PdfName.OutputIntents);
                if (outputIntents == null || outputIntents.IsEmpty())
                {
                    _logger.LogInformation("[PDFUA-METADATA] Adding OutputIntent for PDF/UA");

                    var outputIntent = new PdfDictionary();
                    outputIntent.Put(PdfName.Type, PdfName.OutputIntent);
                    outputIntent.Put(PdfName.S, new PdfName("GTS_PDFUA1"));
                    outputIntent.Put(new PdfName("OutputConditionIdentifier"), new PdfString("sRGB"));
                    outputIntent.Put(new PdfName("RegistryName"), new PdfString("http://www.color.org"));
                    outputIntent.Put(new PdfName("Info"), new PdfString("sRGB IEC61966-2.1"));

                    var intentsArray = new PdfArray();
                    intentsArray.Add(outputIntent);
                    catalogDict.Put(PdfName.OutputIntents, intentsArray);
                    changesMade = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PDFUA-METADATA] Error setting catalog entries: {ex.Message}");
            }

            return await Task.FromResult(changesMade);
        }

        private async Task<bool> SetViewerPreferences(PdfDocument pdfDoc)
        {
            var changesMade = false;

            try
            {
                var catalog = pdfDoc.GetCatalog();
                var viewerPrefs = catalog.GetViewerPreferences();

                if (viewerPrefs == null)
                {
                    _logger.LogInformation("[PDFUA-METADATA] Creating ViewerPreferences");
                    viewerPrefs = new PdfViewerPreferences();
                    catalog.SetViewerPreferences(viewerPrefs);
                    changesMade = true;
                }

                // Set DisplayDocTitle to true (show document title instead of filename)
                var prefsDict = viewerPrefs.GetPdfObject();
                if (!prefsDict.ContainsKey(PdfName.DisplayDocTitle))
                {
                    _logger.LogInformation("[PDFUA-METADATA] Setting DisplayDocTitle to true");
                    viewerPrefs.SetDisplayDocTitle(true);
                    changesMade = true;
                }

                // Optionally set other preferences for better accessibility
                // For example, ensure bookmarks panel is shown if bookmarks exist
                var outlines = catalog.GetPdfObject().GetAsDictionary(PdfName.Outlines);
                if (outlines != null && !prefsDict.ContainsKey(PdfName.NonFullScreenPageMode))
                {
                    prefsDict.Put(PdfName.NonFullScreenPageMode, PdfName.UseOutlines);
                    changesMade = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PDFUA-METADATA] Error setting viewer preferences: {ex.Message}");
            }

            return await Task.FromResult(changesMade);
        }
    }
}
