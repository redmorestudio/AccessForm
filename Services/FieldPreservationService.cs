using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Service to preserve PDF field definitions across document conversions
    /// </summary>
    public class FieldPreservationService
    {
        private readonly ILogger<FieldPreservationService> _logger;
        private readonly string _storageDirectory;

        public FieldPreservationService(ILogger<FieldPreservationService> logger)
        {
            _logger = logger;
            _storageDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AccessForm",
                "FieldDefinitions"
            );
            
            Directory.CreateDirectory(_storageDirectory);
        }

        public class DocumentFieldSet
        {
            public string DocumentId { get; set; } = "";
            public string DocumentName { get; set; } = "";
            public string ContentHash { get; set; } = "";
            public DateTime SavedAt { get; set; }
            public List<PreservedField> Fields { get; set; } = new();
            public Dictionary<string, object> Metadata { get; set; } = new();
        }

        public class PreservedField
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            public int Page { get; set; }
            public string Tooltip { get; set; } = "";
            public bool IsRequired { get; set; }
            public string DefaultValue { get; set; } = "";
            public Dictionary<string, object> Properties { get; set; } = new();
        }

        /// <summary>
        /// Save field definitions for a document
        /// </summary>
        public async Task<string> SaveFieldDefinitions(
            string documentPath,
            byte[] documentContent,
            List<Dictionary<string, object>> fields)
        {
            try
            {
                // Generate document ID based on path and content
                var documentId = GenerateDocumentId(documentPath);
                var contentHash = ComputeContentHash(documentContent);
                
                // Convert field dictionaries to preserved fields
                var preservedFields = fields.Select(f => new PreservedField
                {
                    Name = f.GetValueOrDefault("name", "")?.ToString() ?? "",
                    Type = f.GetValueOrDefault("fieldType", "text")?.ToString() ?? "text",
                    X = Convert.ToSingle(f.GetValueOrDefault("x", 0f)),
                    Y = Convert.ToSingle(f.GetValueOrDefault("y", 0f)),
                    Width = Convert.ToSingle(f.GetValueOrDefault("width", 100f)),
                    Height = Convert.ToSingle(f.GetValueOrDefault("height", 20f)),
                    Page = Convert.ToInt32(f.GetValueOrDefault("pageNumber", 0)),
                    Tooltip = f.GetValueOrDefault("tooltip", "")?.ToString() ?? "",
                    IsRequired = Convert.ToBoolean(f.GetValueOrDefault("isRequired", false)),
                    Properties = f.Where(kv => !new[] { "name", "fieldType", "x", "y", "width", "height", "pageNumber", "tooltip", "isRequired" }.Contains(kv.Key))
                                 .ToDictionary(kv => kv.Key, kv => kv.Value)
                }).ToList();
                
                var fieldSet = new DocumentFieldSet
                {
                    DocumentId = documentId,
                    DocumentName = Path.GetFileName(documentPath),
                    ContentHash = contentHash,
                    SavedAt = DateTime.UtcNow,
                    Fields = preservedFields,
                    Metadata = new Dictionary<string, object>
                    {
                        ["originalPath"] = documentPath,
                        ["fieldCount"] = preservedFields.Count,
                        ["version"] = "1.0"
                    }
                };
                
                // Save to JSON file
                var filePath = Path.Combine(_storageDirectory, $"{documentId}.json");
                var json = JsonSerializer.Serialize(fieldSet, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await File.WriteAllTextAsync(filePath, json);
                
                _logger.LogInformation($"Saved {preservedFields.Count} field definitions for document {documentId}");
                
                return documentId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save field definitions");
                throw;
            }
        }

        /// <summary>
        /// Retrieve field definitions for a document
        /// </summary>
        public async Task<DocumentFieldSet?> GetFieldDefinitions(string documentIdOrPath)
        {
            try
            {
                string documentId = documentIdOrPath;
                
                // If it's a path, convert to ID
                if (documentIdOrPath.Contains('/') || documentIdOrPath.Contains('\\'))
                {
                    documentId = GenerateDocumentId(documentIdOrPath);
                }
                
                var filePath = Path.Combine(_storageDirectory, $"{documentId}.json");
                
                if (!File.Exists(filePath))
                {
                    _logger.LogInformation($"No saved field definitions found for {documentId}");
                    return null;
                }
                
                var json = await File.ReadAllTextAsync(filePath);
                var fieldSet = JsonSerializer.Deserialize<DocumentFieldSet>(json);
                
                _logger.LogInformation($"Retrieved {fieldSet?.Fields.Count ?? 0} field definitions for document {documentId}");
                
                return fieldSet;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve field definitions");
                return null;
            }
        }

        /// <summary>
        /// Check if content has changed significantly
        /// </summary>
        public async Task<bool> HasContentChanged(string documentIdOrPath, byte[] currentContent)
        {
            var fieldSet = await GetFieldDefinitions(documentIdOrPath);
            if (fieldSet == null) return true;
            
            var currentHash = ComputeContentHash(currentContent);
            return currentHash != fieldSet.ContentHash;
        }

        /// <summary>
        /// Smart merge - adjust field positions based on content changes
        /// </summary>
        public async Task<List<PreservedField>> SmartMergeFields(
            DocumentFieldSet savedFields,
            byte[] newContent,
            float contentShiftY = 0)
        {
            // This is where we could implement intelligent field position adjustment
            // based on content analysis, but for now we'll do simple position adjustment
            
            var adjustedFields = new List<PreservedField>();
            
            foreach (var field in savedFields.Fields)
            {
                var adjusted = new PreservedField
                {
                    Name = field.Name,
                    Type = field.Type,
                    X = field.X,
                    Y = field.Y + contentShiftY, // Adjust Y position if content shifted
                    Width = field.Width,
                    Height = field.Height,
                    Page = field.Page,
                    Tooltip = field.Tooltip,
                    IsRequired = field.IsRequired,
                    DefaultValue = field.DefaultValue,
                    Properties = field.Properties
                };
                
                adjustedFields.Add(adjusted);
            }
            
            _logger.LogInformation($"Smart merged {adjustedFields.Count} fields with Y adjustment of {contentShiftY}");
            
            return adjustedFields;
        }

        /// <summary>
        /// List all saved field definitions
        /// </summary>
        public async Task<List<DocumentFieldSet>> ListSavedDefinitions()
        {
            var definitions = new List<DocumentFieldSet>();
            
            foreach (var file in Directory.GetFiles(_storageDirectory, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var fieldSet = JsonSerializer.Deserialize<DocumentFieldSet>(json);
                    if (fieldSet != null)
                    {
                        definitions.Add(fieldSet);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Could not read field definition file: {file}");
                }
            }
            
            return definitions.OrderByDescending(d => d.SavedAt).ToList();
        }

        private string GenerateDocumentId(string documentPath)
        {
            // Create a stable ID based on the document path
            var normalizedPath = Path.GetFullPath(documentPath).ToLowerInvariant();
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
            return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
        }

        private string ComputeContentHash(byte[] content)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(content);
            return BitConverter.ToString(hash).Replace("-", "").Substring(0, 32);
        }
    }
}