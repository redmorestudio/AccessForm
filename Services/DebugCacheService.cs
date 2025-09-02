using System;
using System.Collections.Concurrent;
using System.Linq;using System.Threading.Tasks;

namespace AccessFormServer.Services
{
    public class DebugCacheService
    {
        private readonly ConcurrentDictionary<string, DebugData> _cache = new();
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(30);
        private ProcessedPdfData? _lastProcessedPdf;

        // Simplified method - only Anthropic, no Azure
        public string StoreDebugData(object debugInfo)
        {
            var debugId = GenerateDebugId();
            var debugData = new DebugData
            {
                Id = debugId,
                Timestamp = DateTime.UtcNow,
                Success = true,
                AnthropicResponse = debugInfo,
                FieldResults = new {},
                ProcessingTime = 0
            };

            _cache[debugId] = debugData;
            
            // Clean up expired entries
            _ = Task.Run(CleanupOldEntries);
            
            return debugId;
        }
        public DebugData GetDebugData(string debugId)
        {
            if (_cache.TryGetValue(debugId, out var data))
            {
                // Check if expired
                if (DateTime.UtcNow - data.Timestamp > _cacheExpiration)
                {
                    _cache.TryRemove(debugId, out _);
                    return null;
                }
                return data;
            }
            return null;
        }

        private string GenerateDebugId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        private void CleanupOldEntries()
        {
            var cutoff = DateTime.UtcNow - _cacheExpiration;
            var keysToRemove = _cache
                .Where(kvp => kvp.Value.Timestamp < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
        }
        
        // Store the last processed PDF for markdown conversion
        public void StoreLastProcessedPdf(byte[] pdfBytes, string fileName)
        {
            _lastProcessedPdf = new ProcessedPdfData
            {
                PdfBytes = pdfBytes,
                FileName = fileName,
                ProcessedAt = DateTime.UtcNow
            };
        }
        
        public ProcessedPdfData? GetLastProcessedPdf()
        {
            return _lastProcessedPdf;
        }
    }
    
    public class ProcessedPdfData
    {
        public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
        public string FileName { get; set; } = "";
        public DateTime ProcessedAt { get; set; }
    }

    public class DebugData
    {
        public string Id { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public object AnthropicResponse { get; set; } = "";
        public object FieldResults { get; set; } = "";
        public long ProcessingTime { get; set; }
    }
}
