using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Interactive cascade correction service for fixing field name misalignments
    /// </summary>
    public class InteractiveCascadeCorrector
    {
        private readonly ILogger<InteractiveCascadeCorrector> _logger;
        private Dictionary<string, CascadeSession> _sessions = new();

        public InteractiveCascadeCorrector(ILogger<InteractiveCascadeCorrector> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Create a new cascade correction session
        /// </summary>
        public string CreateSession(List<FieldDetectionResult> fields)
        {
            var sessionId = Guid.NewGuid().ToString("N");
            var session = new CascadeSession
            {
                Id = sessionId,
                OriginalFields = fields.Select(f => f.Clone()).ToList(),
                WorkingFields = fields.Select(f => f.Clone()).ToList(),
                Commands = new List<CascadeCommand>()
            };

            _sessions[sessionId] = session;
            _logger.LogInformation($"Created cascade session {sessionId} with {fields.Count} fields");

            return sessionId;
        }

        /// <summary>
        /// Get current field table for display
        /// </summary>
        public CascadeTableResponse GetFieldTable(string sessionId, int? pageNumber = null)
        {
            if (!_sessions.ContainsKey(sessionId))
                throw new ArgumentException($"Session {sessionId} not found");

            var session = _sessions[sessionId];
            var fields = session.WorkingFields;

            if (pageNumber.HasValue)
                fields = fields.Where(f => f.PageNumber == pageNumber.Value).ToList();

            // Sort spatially for natural reading order (top-to-bottom, left-to-right)
            // In PDF coordinates, Y=0 is at bottom, so higher Y = top of page
            // Therefore: OrderByDescending for top-to-bottom, ThenBy for left-to-right
            fields = fields
                .OrderBy(f => f.PageNumber)            // Multi-page support: page 1 first, then page 2, etc.
                .ThenByDescending(f => f.Y)             // Top to bottom (higher Y = top in PDF coords)
                .ThenBy(f => f.X)                      // Left to right
                .ToList();

            var tableRows = new List<CascadeFieldRow>();
            var duplicateNames = fields.GroupBy(f => f.FieldName)
                                      .Where(g => g.Count() > 1)
                                      .Select(g => g.Key)
                                      .ToHashSet();

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var status = DetermineFieldStatus(field, duplicateNames);

                tableRows.Add(new CascadeFieldRow
                {
                    Index = i + 1,
                    FieldId = field.ShortId,
                    Y = Math.Round(field.Y, 1),
                    X = Math.Round(field.X, 1),
                    CurrentName = field.FieldName,
                    FieldType = field.FieldType,
                    Status = status,
                    StatusIcon = GetStatusIcon(status),
                    NearText = ExtractNearText(field),
                    IsPhantom = session.PhantomIndices.Contains(i + 1),
                    IsCascadeAffected = session.CascadeRanges.Any(r => i + 1 >= r.Start && i + 1 <= r.End)
                });
            }

            return new CascadeTableResponse
            {
                SessionId = sessionId,
                Rows = tableRows,
                DetectedPatterns = DetectPatterns(fields),
                CommandHistory = session.Commands.Select(c => c.ToString()).ToList()
            };
        }

        /// <summary>
        /// Execute a command on the session
        /// </summary>
        public CascadeCommandResult ExecuteCommand(string sessionId, string command, string[] args)
        {
            if (!_sessions.ContainsKey(sessionId))
                throw new ArgumentException($"Session {sessionId} not found");

            var session = _sessions[sessionId];
            var result = new CascadeCommandResult { Success = false };

            try
            {
                switch (command.ToLower())
                {
                    case "phantom":
                        result = MarkPhantom(session, args);
                        break;

                    case "cascade":
                        result = ApplyCascade(session, args);
                        break;

                    case "swap":
                        result = SwapFields(session, args);
                        break;

                    case "rename":
                        result = RenameField(session, args);
                        break;

                    case "auto":
                        result = AutoDetectAndFix(session);
                        break;

                    case "preview":
                        result = PreviewCorrections(session);
                        break;

                    case "apply":
                        result = ApplyCorrections(session);
                        break;

                    case "undo":
                        result = UndoLastCommand(session);
                        break;

                    case "reset":
                        result = ResetSession(session);
                        break;

                    default:
                        result.Message = $"Unknown command: {command}";
                        break;
                }

                if (result.Success)
                {
                    session.Commands.Add(new CascadeCommand
                    {
                        Name = command,
                        Args = args,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing command {command} in session {sessionId}");
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        private CascadeCommandResult MarkPhantom(CascadeSession session, string[] args)
        {
            if (args.Length == 0)
                return new CascadeCommandResult { Success = false, Message = "Usage: phantom <index>" };

            if (!int.TryParse(args[0], out int index))
                return new CascadeCommandResult { Success = false, Message = "Invalid index" };

            session.PhantomIndices.Add(index);

            // Automatically cascade from next field
            var cascadeStart = index + 1;
            session.CascadeRanges.Add(new CascadeRange
            {
                Start = cascadeStart,
                End = session.WorkingFields.Count
            });

            return new CascadeCommandResult
            {
                Success = true,
                Message = $"Marked field {index} as phantom, will cascade from {cascadeStart}",
                AffectedFields = session.WorkingFields.Count - index
            };
        }

        private CascadeCommandResult ApplyCascade(CascadeSession session, string[] args)
        {
            if (args.Length == 0)
                return new CascadeCommandResult { Success = false, Message = "Usage: cascade <start> or cascade <start-end>" };

            // Parse range
            if (args[0].Contains("-"))
            {
                var parts = args[0].Split('-');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int start) &&
                    int.TryParse(parts[1], out int end))
                {
                    session.CascadeRanges.Add(new CascadeRange { Start = start, End = end });
                    return new CascadeCommandResult
                    {
                        Success = true,
                        Message = $"Cascade will be applied to fields {start}-{end}",
                        AffectedFields = end - start + 1
                    };
                }
            }
            else if (int.TryParse(args[0], out int start))
            {
                session.CascadeRanges.Add(new CascadeRange
                {
                    Start = start,
                    End = session.WorkingFields.Count
                });
                return new CascadeCommandResult
                {
                    Success = true,
                    Message = $"Cascade will be applied from field {start} to end",
                    AffectedFields = session.WorkingFields.Count - start + 1
                };
            }

            return new CascadeCommandResult { Success = false, Message = "Invalid cascade range" };
        }

        private CascadeCommandResult SwapFields(CascadeSession session, string[] args)
        {
            if (args.Length < 2)
                return new CascadeCommandResult { Success = false, Message = "Usage: swap <index1> <index2>" };

            if (!int.TryParse(args[0], out int index1) || !int.TryParse(args[1], out int index2))
                return new CascadeCommandResult { Success = false, Message = "Invalid indices" };

            index1--; // Convert to 0-based
            index2--;

            if (index1 < 0 || index1 >= session.WorkingFields.Count ||
                index2 < 0 || index2 >= session.WorkingFields.Count)
                return new CascadeCommandResult { Success = false, Message = "Index out of range" };

            // Swap the field names
            var temp = session.WorkingFields[index1].FieldName;
            session.WorkingFields[index1].FieldName = session.WorkingFields[index2].FieldName;
            session.WorkingFields[index2].FieldName = temp;

            return new CascadeCommandResult
            {
                Success = true,
                Message = $"Swapped names of fields {index1 + 1} and {index2 + 1}",
                AffectedFields = 2
            };
        }

        private CascadeCommandResult RenameField(CascadeSession session, string[] args)
        {
            if (args.Length < 2)
                return new CascadeCommandResult { Success = false, Message = "Usage: rename <index> \"new name\"" };

            if (!int.TryParse(args[0], out int index))
                return new CascadeCommandResult { Success = false, Message = "Invalid index" };

            index--; // Convert to 0-based
            if (index < 0 || index >= session.WorkingFields.Count)
                return new CascadeCommandResult { Success = false, Message = "Index out of range" };

            var newName = string.Join(" ", args.Skip(1));
            session.WorkingFields[index].FieldName = newName.Trim('"');

            return new CascadeCommandResult
            {
                Success = true,
                Message = $"Renamed field {index + 1} to \"{newName}\"",
                AffectedFields = 1
            };
        }

        private CascadeCommandResult AutoDetectAndFix(CascadeSession session)
        {
            var phantoms = new List<int>();
            var cascadeRanges = new List<CascadeRange>();

            // Detect Syncfusion phantom fields (text###abc pattern)
            var syncfusionPattern = new Regex(@"^text\d+[a-z]+$", RegexOptions.IgnoreCase);

            for (int i = 0; i < session.WorkingFields.Count; i++)
            {
                var field = session.WorkingFields[i];

                // Check for Syncfusion-style phantom
                if (syncfusionPattern.IsMatch(field.FieldName))
                {
                    phantoms.Add(i + 1);

                    // Start cascade from next field
                    if (i + 1 < session.WorkingFields.Count)
                    {
                        cascadeRanges.Add(new CascadeRange
                        {
                            Start = i + 2,
                            End = session.WorkingFields.Count
                        });
                    }
                }
            }

            session.PhantomIndices.UnionWith(phantoms);
            session.CascadeRanges.AddRange(cascadeRanges);

            return new CascadeCommandResult
            {
                Success = true,
                Message = $"Auto-detected {phantoms.Count} phantom fields and {cascadeRanges.Count} cascade zones",
                AffectedFields = session.WorkingFields.Count,
                Details = new
                {
                    Phantoms = phantoms,
                    CascadeRanges = cascadeRanges.Select(r => $"{r.Start}-{r.End}")
                }
            };
        }

        private CascadeCommandResult PreviewCorrections(CascadeSession session)
        {
            var preview = new List<FieldCorrection>();
            var sortedFields = session.WorkingFields.OrderBy(f => f.Y).ThenBy(f => f.X).ToList();

            // Remove phantoms and apply cascades
            var correctedFields = new List<FieldDetectionResult>();
            var nameQueue = new Queue<string>();

            for (int i = 0; i < sortedFields.Count; i++)
            {
                if (session.PhantomIndices.Contains(i + 1))
                {
                    // Skip phantom but save its name
                    continue;
                }

                var field = sortedFields[i].Clone();

                // Check if this field is in a cascade range
                var cascadeRange = session.CascadeRanges.FirstOrDefault(r => i + 1 >= r.Start && i + 1 <= r.End);
                if (cascadeRange != null)
                {
                    // Find the next non-phantom field's name
                    for (int j = i + 1; j < sortedFields.Count; j++)
                    {
                        if (!session.PhantomIndices.Contains(j + 1))
                        {
                            var oldName = field.FieldName;
                            field.FieldName = sortedFields[j].FieldName;

                            preview.Add(new FieldCorrection
                            {
                                Index = i + 1,
                                OldName = oldName,
                                NewName = field.FieldName,
                                Reason = "Cascade correction"
                            });
                            break;
                        }
                    }
                }

                correctedFields.Add(field);
            }

            return new CascadeCommandResult
            {
                Success = true,
                Message = "Preview of corrections",
                Details = preview,
                AffectedFields = preview.Count
            };
        }

        private CascadeCommandResult ApplyCorrections(CascadeSession session)
        {
            // Apply phantom removals and cascades
            var correctedFields = new List<FieldDetectionResult>();
            var sortedFields = session.WorkingFields.OrderBy(f => f.Y).ThenBy(f => f.X).ToList();

            // Build list of names from non-phantom fields
            var names = new List<string>();
            for (int i = 0; i < sortedFields.Count; i++)
            {
                if (!session.PhantomIndices.Contains(i + 1))
                {
                    names.Add(sortedFields[i].FieldName);
                }
            }

            // Apply cascades
            int nameIndex = 0;
            for (int i = 0; i < sortedFields.Count; i++)
            {
                if (session.PhantomIndices.Contains(i + 1))
                {
                    // Skip phantom
                    continue;
                }

                var field = sortedFields[i].Clone();

                // Check if in cascade range
                var cascadeRange = session.CascadeRanges.FirstOrDefault(r => i + 1 >= r.Start && i + 1 <= r.End);
                if (cascadeRange != null && nameIndex + 1 < names.Count)
                {
                    // Shift name forward
                    field.FieldName = names[Math.Min(nameIndex + 1, names.Count - 1)];
                }

                correctedFields.Add(field);
                nameIndex++;
            }

            session.WorkingFields = correctedFields;
            session.CorrectionApplied = true;

            return new CascadeCommandResult
            {
                Success = true,
                Message = $"Applied corrections: removed {session.PhantomIndices.Count} phantoms, corrected {session.CascadeRanges.Sum(r => r.End - r.Start + 1)} fields",
                AffectedFields = session.WorkingFields.Count
            };
        }

        private CascadeCommandResult UndoLastCommand(CascadeSession session)
        {
            if (session.Commands.Count == 0)
                return new CascadeCommandResult { Success = false, Message = "No commands to undo" };

            // Reset to original and replay all but last command
            session.WorkingFields = session.OriginalFields.Select(f => f.Clone()).ToList();
            session.PhantomIndices.Clear();
            session.CascadeRanges.Clear();

            var commandsToReplay = session.Commands.Take(session.Commands.Count - 1).ToList();
            session.Commands.Clear();

            foreach (var cmd in commandsToReplay)
            {
                ExecuteCommand(session.Id, cmd.Name, cmd.Args);
            }

            return new CascadeCommandResult
            {
                Success = true,
                Message = "Undid last command"
            };
        }

        private CascadeCommandResult ResetSession(CascadeSession session)
        {
            session.WorkingFields = session.OriginalFields.Select(f => f.Clone()).ToList();
            session.PhantomIndices.Clear();
            session.CascadeRanges.Clear();
            session.Commands.Clear();
            session.CorrectionApplied = false;

            return new CascadeCommandResult
            {
                Success = true,
                Message = "Session reset to original state"
            };
        }

        private string DetermineFieldStatus(FieldDetectionResult field, HashSet<string> duplicateNames)
        {
            if (duplicateNames.Contains(field.FieldName))
                return "DUPLICATE";

            if (Regex.IsMatch(field.FieldName, @"^text\d+[a-z]+$", RegexOptions.IgnoreCase))
                return "PHANTOM";

            if (!field.IsValid)
                return "INVALID";

            return "OK";
        }

        private string GetStatusIcon(string status)
        {
            return status switch
            {
                "OK" => "✓",
                "DUPLICATE" => "⚠",
                "PHANTOM" => "🚫",
                "CASCADE" => "⚡",
                "INVALID" => "❌",
                _ => "?"
            };
        }

        private string ExtractNearText(FieldDetectionResult field)
        {
            // This would ideally extract text from the PDF near the field
            // For now, return a placeholder or use field metadata
            return field.ValidationNotes ?? "";
        }

        private List<string> DetectPatterns(List<FieldDetectionResult> fields)
        {
            var patterns = new List<string>();

            // Check for duplicate names
            var duplicates = fields.GroupBy(f => f.FieldName)
                                  .Where(g => g.Count() > 1);
            foreach (var dup in duplicates)
            {
                patterns.Add($"Duplicate name '{dup.Key}' appears {dup.Count()} times");
            }

            // Check for Syncfusion phantoms
            var syncfusionPattern = new Regex(@"^text\d+[a-z]+$", RegexOptions.IgnoreCase);
            var phantoms = fields.Where(f => syncfusionPattern.IsMatch(f.FieldName)).ToList();
            if (phantoms.Any())
            {
                patterns.Add($"Found {phantoms.Count} likely phantom fields (Syncfusion pattern)");
            }

            // Check for off-by-one patterns
            for (int i = 0; i < fields.Count - 1; i++)
            {
                // If current field name seems to belong to next field position
                // This is a heuristic - would be better with actual text analysis
                if (fields[i].FieldName.Contains("signature") && fields[i + 1].Y > fields[i].Y + 50)
                {
                    patterns.Add($"Possible off-by-one cascade starting at field {i + 1}");
                }
            }

            return patterns;
        }

        /// <summary>
        /// Get corrected fields after applying all corrections
        /// </summary>
        public List<FieldDetectionResult> GetCorrectedFields(string sessionId)
        {
            if (!_sessions.ContainsKey(sessionId))
                throw new ArgumentException($"Session {sessionId} not found");

            return _sessions[sessionId].WorkingFields;
        }
    }

    // Supporting classes
    public class CascadeSession
    {
        public string Id { get; set; }
        public List<FieldDetectionResult> OriginalFields { get; set; }
        public List<FieldDetectionResult> WorkingFields { get; set; }
        public HashSet<int> PhantomIndices { get; set; } = new();
        public List<CascadeRange> CascadeRanges { get; set; } = new();
        public List<CascadeCommand> Commands { get; set; } = new();
        public bool CorrectionApplied { get; set; }
    }

    public class CascadeRange
    {
        public int Start { get; set; }
        public int End { get; set; }
    }

    public class CascadeCommand
    {
        public string Name { get; set; }
        public string[] Args { get; set; }
        public DateTime Timestamp { get; set; }

        public override string ToString() => $"{Name} {string.Join(" ", Args)}";
    }

    public class CascadeTableResponse
    {
        public string SessionId { get; set; }
        public List<CascadeFieldRow> Rows { get; set; }
        public List<string> DetectedPatterns { get; set; }
        public List<string> CommandHistory { get; set; }
    }

    public class CascadeFieldRow
    {
        public int Index { get; set; }
        public string FieldId { get; set; }
        public double Y { get; set; }
        public double X { get; set; }
        public string CurrentName { get; set; }
        public string FieldType { get; set; }
        public string Status { get; set; }
        public string StatusIcon { get; set; }
        public string NearText { get; set; }
        public bool IsPhantom { get; set; }
        public bool IsCascadeAffected { get; set; }
    }

    public class CascadeCommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int AffectedFields { get; set; }
        public object Details { get; set; }
    }

    public class FieldCorrection
    {
        public int Index { get; set; }
        public string OldName { get; set; }
        public string NewName { get; set; }
        public string Reason { get; set; }
    }
}

// Extension method for cloning
public static class FieldDetectionResultExtensions
{
    public static FieldDetectionResult Clone(this FieldDetectionResult original)
    {
        return new FieldDetectionResult
        {
            FieldName = original.FieldName,
            FieldType = original.FieldType,
            X = original.X,
            Y = original.Y,
            Width = original.Width,
            Height = original.Height,
            PageNumber = original.PageNumber,
            ShortId = original.ShortId,
            IsValid = original.IsValid,
            ValidationNotes = original.ValidationNotes,
            Source = original.Source,
            Confidence = original.Confidence
        };
    }
}