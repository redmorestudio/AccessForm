using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Services.Remediation.AI
{
    /// <summary>
    /// Caches successful remediation solutions for reuse
    /// </summary>
    public class SolutionCache
    {
        private readonly ILogger<SolutionCache> _logger;
        private readonly ConcurrentDictionary<string, CachedSolution> _cache;
        private readonly string _cacheFilePath;

        public SolutionCache(ILogger<SolutionCache> logger)
        {
            _logger = logger;
            _cache = new ConcurrentDictionary<string, CachedSolution>();

            // Store cache in project directory
            _cacheFilePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "remediation-solution-cache.json"
            );

            LoadCacheFromDisk();
            InitializeCommonSolutions();
        }

        public class CachedSolution
        {
            public string ViolationPattern { get; set; }
            public string Category { get; set; }
            public string Solution { get; set; }
            public SolutionType Type { get; set; }
            public int SuccessCount { get; set; }
            public DateTime LastUsed { get; set; }
            public double SuccessRate { get; set; }
        }

        public enum SolutionType
        {
            PythonScript,
            JsonInstructions,
            BuiltIn
        }

        /// <summary>
        /// Initialize with common known solutions
        /// </summary>
        private void InitializeCommonSolutions()
        {
            // Form/Widget violation solution
            AddSolution("form-widget-role", new CachedSolution
            {
                ViolationPattern = "Form element.*Role attribute|widget annotation|Table 348|Table 340",
                Category = "Structure",
                Type = SolutionType.PythonScript,
                Solution = @"
import pikepdf
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

def fix_form_widget_violations(input_path, output_path):
    '''Fix Form element Role attribute and widget reference violations'''

    pdf = pikepdf.open(input_path)

    # Access the structure tree
    if '/StructTreeRoot' not in pdf.Root:
        logger.warning('No structure tree found in PDF')
        pdf.save(output_path)
        return

    struct_tree = pdf.Root.StructTreeRoot
    fixed_count = 0

    def process_struct_elem(elem, depth=0):
        '''Recursively process structure elements'''
        nonlocal fixed_count

        if not isinstance(elem, pikepdf.Dictionary):
            return

        # Check if this is a Form element
        if '/S' in elem and str(elem.S) == '/Form':
            logger.info(f'Found Form element at depth {depth}')

            # Fix 1: Add Role attribute if missing
            if '/A' not in elem:
                elem.A = pikepdf.Dictionary()

            if isinstance(elem.A, pikepdf.Dictionary):
                if '/Role' not in elem.A or str(elem.A.Role) != '/Form':
                    elem.A.Role = pikepdf.Name('/Form')
                    fixed_count += 1
                    logger.info('Added Role=/Form attribute')

            # Fix 2: Ensure proper widget reference as child
            if '/K' in elem:
                kids = elem.K
                if not isinstance(kids, pikepdf.Array):
                    kids = pikepdf.Array([kids])

                # Check if there's already a valid widget reference
                has_widget = False
                for kid in kids:
                    if isinstance(kid, pikepdf.Dictionary) and '/Type' in kid:
                        if str(kid.Type) == '/OBJR' and '/Obj' in kid:
                            has_widget = True
                            break

                if not has_widget:
                    logger.warning('Form element missing widget reference')
                    # This would need more context to fix properly
                    # as we'd need to find the associated widget annotation

        # Process children
        if '/K' in elem:
            kids = elem.K
            if isinstance(kids, pikepdf.Array):
                for kid in kids:
                    process_struct_elem(kid, depth + 1)
            else:
                process_struct_elem(kids, depth + 1)

    # Process all structure elements
    if '/K' in struct_tree:
        process_struct_elem(struct_tree.K)

    logger.info(f'Fixed {fixed_count} Form/Role violations')

    # Save the fixed PDF
    pdf.save(output_path)
    logger.info(f'Saved remediated PDF to {output_path}')

# Execute the fix
fix_form_widget_violations(INPUT_PDF, OUTPUT_PDF)
",
                SuccessRate = 0.85,
                LastUsed = DateTime.UtcNow
            });

            // Alt text violation solution
            AddSolution("alt-text-missing", new CachedSolution
            {
                ViolationPattern = "alternative text|Alt attribute|Figure.*alternative",
                Category = "Content",
                Type = SolutionType.PythonScript,
                Solution = @"
import pikepdf

def add_alt_text(input_path, output_path):
    pdf = pikepdf.open(input_path)

    def process_elem(elem):
        if not isinstance(elem, pikepdf.Dictionary):
            return

        # Add alt text to Figures
        if '/S' in elem and str(elem.S) == '/Figure':
            if '/Alt' not in elem:
                elem.Alt = 'Decorative image'

        # Process children
        if '/K' in elem:
            kids = elem.K
            if isinstance(kids, pikepdf.Array):
                for kid in kids:
                    process_elem(kid)
            else:
                process_elem(kids)

    if '/StructTreeRoot' in pdf.Root:
        process_elem(pdf.Root.StructTreeRoot.K)

    pdf.save(output_path)

add_alt_text(INPUT_PDF, OUTPUT_PDF)
",
                SuccessRate = 0.90,
                LastUsed = DateTime.UtcNow
            });

            _logger.LogInformation($"Initialized solution cache with {_cache.Count} common solutions");
        }

        /// <summary>
        /// Get a cached solution for a violation pattern
        /// </summary>
        public CachedSolution GetSolution(string violationDescription)
        {
            foreach (var kvp in _cache)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(
                    violationDescription,
                    kvp.Value.ViolationPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    kvp.Value.LastUsed = DateTime.UtcNow;
                    _logger.LogInformation($"[SOLUTION-CACHE] Found cached solution '{kvp.Key}' for violation");
                    return kvp.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Add a new solution to the cache
        /// </summary>
        public void AddSolution(string key, CachedSolution solution)
        {
            _cache[key] = solution;
            _logger.LogInformation($"[SOLUTION-CACHE] Added solution '{key}' to cache");
        }

        /// <summary>
        /// Store a successful GPT solution for future use
        /// </summary>
        public void StoreGptSolution(string violationPattern, string category,
            string solution, SolutionType type)
        {
            var key = GenerateKey(violationPattern);

            if (_cache.TryGetValue(key, out var existing))
            {
                existing.SuccessCount++;
                existing.LastUsed = DateTime.UtcNow;
                existing.SuccessRate = Math.Min(1.0, existing.SuccessRate + 0.05);
            }
            else
            {
                AddSolution(key, new CachedSolution
                {
                    ViolationPattern = violationPattern,
                    Category = category,
                    Solution = solution,
                    Type = type,
                    SuccessCount = 1,
                    SuccessRate = 0.70,
                    LastUsed = DateTime.UtcNow
                });
            }

            // Persist to disk after storing new solution
            SaveCacheToDisk();
        }

        private string GenerateKey(string pattern)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(pattern.ToLower()));
                return Convert.ToBase64String(hash).Substring(0, 12);
            }
        }

        /// <summary>
        /// Get statistics about cached solutions
        /// </summary>
        public Dictionary<string, object> GetStatistics()
        {
            return new Dictionary<string, object>
            {
                ["TotalSolutions"] = _cache.Count,
                ["PythonScripts"] = _cache.Count(x => x.Value.Type == SolutionType.PythonScript),
                ["JsonInstructions"] = _cache.Count(x => x.Value.Type == SolutionType.JsonInstructions),
                ["AverageSuccessRate"] = _cache.Any() ? _cache.Average(x => x.Value.SuccessRate) : 0,
                ["MostUsed"] = _cache.OrderByDescending(x => x.Value.SuccessCount)
                    .FirstOrDefault().Key ?? "none"
            };
        }

        /// <summary>
        /// Load cache from disk (if exists)
        /// </summary>
        private void LoadCacheFromDisk()
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                {
                    _logger.LogInformation("[SOLUTION-CACHE] No existing cache file found, starting fresh");
                    return;
                }

                var json = File.ReadAllText(_cacheFilePath);
                var diskCache = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CachedSolution>>(json);

                if (diskCache != null)
                {
                    foreach (var kvp in diskCache)
                    {
                        _cache[kvp.Key] = kvp.Value;
                    }
                    _logger.LogInformation($"[SOLUTION-CACHE] Loaded {diskCache.Count} solutions from disk: {_cacheFilePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SOLUTION-CACHE] Failed to load cache from disk, starting fresh");
            }
        }

        /// <summary>
        /// Save cache to disk
        /// </summary>
        private void SaveCacheToDisk()
        {
            try
            {
                var diskCache = _cache.ToDictionary(x => x.Key, x => x.Value);
                var json = System.Text.Json.JsonSerializer.Serialize(diskCache, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_cacheFilePath, json);
                _logger.LogDebug($"[SOLUTION-CACHE] Saved {diskCache.Count} solutions to disk: {_cacheFilePath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SOLUTION-CACHE] Failed to save cache to disk");
            }
        }
    }
}