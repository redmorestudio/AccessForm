using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Adobe.PDFServicesSDK;
using Adobe.PDFServicesSDK.auth;
using Adobe.PDFServicesSDK.io;
using Adobe.PDFServicesSDK.pdfjobs.jobs;
using Adobe.PDFServicesSDK.pdfjobs.results;
using Adobe.PDFServicesSDK.exception;
using Adobe.PDFServicesSDK.pdfjobs.parameters.autotag;
using System.Text.Json;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Service that uses Adobe Acrobat Services API to autotag PDFs for accessibility
    /// </summary>
    public class AdobeAutotagService
    {
        private readonly ILogger<AdobeAutotagService> _logger;
        private readonly ICredentials _credentials;
        private readonly string _credentialsPath;

        public AdobeAutotagService(ILogger<AdobeAutotagService> logger, string? credentialsPath = null)
        {
            _logger = logger;
            
            // Default to looking for credentials in the project directory
            _credentialsPath = credentialsPath ?? Path.Combine(
                Directory.GetCurrentDirectory(), 
                "../adobe/pdfservices-api-credentials.json"
            );

            // Load credentials from JSON file
            if (File.Exists(_credentialsPath))
            {
                _logger.LogInformation($"Loading Adobe credentials from: {_credentialsPath}");
                var credJson = File.ReadAllText(_credentialsPath);
                var creds = JsonDocument.Parse(credJson);
                
                var clientId = creds.RootElement
                    .GetProperty("client_credentials")
                    .GetProperty("client_id")
                    .GetString();
                
                var clientSecret = creds.RootElement
                    .GetProperty("client_credentials")
                    .GetProperty("client_secret")
                    .GetString();

                _credentials = new ServicePrincipalCredentials(clientId, clientSecret);
                _logger.LogInformation("Adobe credentials loaded successfully");
            }
            else
            {
                // Fall back to environment variables
                _logger.LogInformation("Credentials file not found, using environment variables");
                _credentials = new ServicePrincipalCredentials(
                    Environment.GetEnvironmentVariable("PDF_SERVICES_CLIENT_ID") ?? "",
                    Environment.GetEnvironmentVariable("PDF_SERVICES_CLIENT_SECRET") ?? ""
                );
            }
        }

        /// <summary>
        /// Apply Adobe autotag to make PDF fully accessible
        /// </summary>
        public async Task<byte[]> AutotagPdfAsync(byte[] pdfBytes, bool generateReport = false)
        {
            try
            {
                _logger.LogInformation($"Starting Adobe autotag for {pdfBytes.Length} byte PDF");

                // Create PDF Services instance
                PDFServices pdfServices = new PDFServices(_credentials);

                // Upload the PDF
                using var inputStream = new MemoryStream(pdfBytes);
                IAsset asset = pdfServices.Upload(inputStream, PDFServicesMediaType.PDF.GetMIMETypeValue());

                // Create autotag job with options
                AutotagPDFJob autotagJob;
                
                if (generateReport)
                {
                    // Generate accessibility report along with tagged PDF
                    var parameters = AutotagPDFParams.AutotagPDFParamsBuilder()
                        .GenerateReport()
                        .Build();
                    autotagJob = new AutotagPDFJob(asset).SetParams(parameters);
                }
                else
                {
                    // Just generate tagged PDF
                    autotagJob = new AutotagPDFJob(asset);
                }

                // Submit the job
                _logger.LogInformation("Submitting autotag job to Adobe API");
                string location = pdfServices.Submit(autotagJob);
                
                // Wait for and get the result
                PDFServicesResponse<AutotagPDFResult> response = 
                    pdfServices.GetJobResult<AutotagPDFResult>(location, typeof(AutotagPDFResult));

                // Get the tagged PDF
                IAsset taggedAsset = response.Result.TaggedPDF;
                StreamAsset streamAsset = pdfServices.GetContent(taggedAsset);

                // Read the result into a byte array
                using var resultStream = new MemoryStream();
                await streamAsset.Stream.CopyToAsync(resultStream);
                var taggedPdfBytes = resultStream.ToArray();

                _logger.LogInformation($"Adobe autotag successful, result is {taggedPdfBytes.Length} bytes");

                // Optionally save the report if requested
                if (generateReport && response.Result.Report != null)
                {
                    try
                    {
                        IAsset reportAsset = response.Result.Report;
                        StreamAsset reportStream = pdfServices.GetContent(reportAsset);
                        
                        var reportPath = Path.Combine(Path.GetTempPath(), $"accessibility_report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                        using var reportFile = File.OpenWrite(reportPath);
                        await reportStream.Stream.CopyToAsync(reportFile);
                        
                        _logger.LogInformation($"Accessibility report saved to: {reportPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to save accessibility report");
                    }
                }

                return taggedPdfBytes;
            }
            catch (ServiceUsageException ex)
            {
                _logger.LogError(ex, "Adobe API service usage error");
                throw new InvalidOperationException($"Adobe API usage error: {ex.Message}", ex);
            }
            catch (ServiceApiException ex)
            {
                _logger.LogError(ex, "Adobe API service error");
                throw new InvalidOperationException($"Adobe API error: {ex.Message}", ex);
            }
            catch (SDKException ex)
            {
                _logger.LogError(ex, "Adobe SDK error");
                throw new InvalidOperationException($"Adobe SDK error: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to autotag PDF");
                throw;
            }
        }

        /// <summary>
        /// Check if Adobe credentials are properly configured
        /// </summary>
        public bool IsConfigured()
        {
            try
            {
                // Try to create a PDF Services instance to validate credentials
                var pdfServices = new PDFServices(_credentials);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}