using System;
using System.Collections.Concurrent;
using System.Threading;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Simple in-memory service to track processing progress for real-time UI updates
    /// </summary>
    public class ProcessingProgressService
    {
        private readonly ConcurrentDictionary<string, ProcessingProgress> _progressMap = new();
        private int _sessionCounter = 0;

        public class ProcessingProgress
        {
            public string SessionId { get; set; }
            public string CurrentStep { get; set; } = "Initializing...";
            public string DetailedStatus { get; set; } = "";
            public int PercentComplete { get; set; } = 0;
            public DateTime StartTime { get; set; } = DateTime.UtcNow;
            public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
            public bool IsComplete { get; set; } = false;
            public bool HasError { get; set; } = false;
            public string ErrorMessage { get; set; }

            // Remediation-specific fields
            public int? RemediationIteration { get; set; }
            public int? ViolationsFound { get; set; }
            public int? ViolationsFixed { get; set; }
            public string RemediationPhase { get; set; }
        }

        /// <summary>
        /// Start a new processing session
        /// </summary>
        public string StartSession(string initialStep = "Starting processing...")
        {
            var sessionId = $"session_{Interlocked.Increment(ref _sessionCounter)}_{Guid.NewGuid():N}";
            var progress = new ProcessingProgress
            {
                SessionId = sessionId,
                CurrentStep = initialStep
            };
            _progressMap[sessionId] = progress;

            // Clean up old sessions (older than 10 minutes)
            CleanupOldSessions();

            return sessionId;
        }

        /// <summary>
        /// Update the progress for a session
        /// </summary>
        public void UpdateProgress(string sessionId, string step, string details = null, int? percentComplete = null)
        {
            if (_progressMap.TryGetValue(sessionId, out var progress))
            {
                progress.CurrentStep = step;
                progress.DetailedStatus = details ?? "";
                if (percentComplete.HasValue)
                {
                    progress.PercentComplete = Math.Min(100, Math.Max(0, percentComplete.Value));
                }
                progress.LastUpdate = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Update remediation-specific progress
        /// </summary>
        public void UpdateRemediationProgress(string sessionId, int iteration, int violationsFound,
            int violationsFixed, string phase)
        {
            if (_progressMap.TryGetValue(sessionId, out var progress))
            {
                progress.RemediationIteration = iteration;
                progress.ViolationsFound = violationsFound;
                progress.ViolationsFixed = violationsFixed;
                progress.RemediationPhase = phase;
                progress.CurrentStep = $"Remediation Iteration {iteration}: {phase}";
                progress.DetailedStatus = $"{violationsFixed}/{violationsFound} violations fixed";
                progress.LastUpdate = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Mark a session as complete
        /// </summary>
        public void CompleteSession(string sessionId, bool success = true, string message = null)
        {
            if (_progressMap.TryGetValue(sessionId, out var progress))
            {
                progress.IsComplete = true;
                progress.HasError = !success;
                progress.ErrorMessage = message;
                progress.PercentComplete = success ? 100 : progress.PercentComplete;
                progress.CurrentStep = success ? "Processing complete!" : "Processing failed";
                progress.LastUpdate = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Get the current progress for a session
        /// </summary>
        public ProcessingProgress GetProgress(string sessionId)
        {
            return _progressMap.TryGetValue(sessionId, out var progress) ? progress : null;
        }

        /// <summary>
        /// Clean up sessions older than 10 minutes
        /// </summary>
        private void CleanupOldSessions()
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-10);
            var oldSessions = _progressMap
                .Where(kvp => kvp.Value.LastUpdate < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var sessionId in oldSessions)
            {
                _progressMap.TryRemove(sessionId, out _);
            }
        }
    }
}