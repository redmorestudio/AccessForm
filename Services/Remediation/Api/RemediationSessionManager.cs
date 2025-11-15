using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Api
{
    /// <summary>
    /// Manages active remediation sessions for the REST API
    /// </summary>
    public class RemediationSessionManager
    {
        private readonly ILogger<RemediationSessionManager> _logger;
        private readonly ConcurrentDictionary<string, ActiveRemediationSession> _sessions;

        public RemediationSessionManager(ILogger<RemediationSessionManager> logger)
        {
            _logger = logger;
            _sessions = new ConcurrentDictionary<string, ActiveRemediationSession>();
        }

        public class ActiveRemediationSession
        {
            public string SessionId { get; set; }
            public RemediationSession Session { get; set; }
            public Task<RemediationResult> Task { get; set; }
            public CancellationTokenSource CancellationToken { get; set; }
            public DateTime StartTime { get; set; }
            public string FileName { get; set; }
            public RemediationStatus Status { get; set; }
            public RemediationResult Result { get; set; }
        }

        public enum RemediationStatus
        {
            Starting,
            Running,
            Completed,
            Failed,
            Cancelled
        }

        public string CreateSession(string fileName, RemediationOptions options)
        {
            var sessionId = Guid.NewGuid().ToString("N");
            var session = new ActiveRemediationSession
            {
                SessionId = sessionId,
                FileName = fileName,
                StartTime = DateTime.UtcNow,
                Status = RemediationStatus.Starting,
                CancellationToken = new CancellationTokenSource()
            };

            _sessions.TryAdd(sessionId, session);
            _logger.LogInformation($"[SESSION-MANAGER] Created session {sessionId} for {fileName}");

            return sessionId;
        }

        public void UpdateSession(string sessionId, RemediationSession remediationSession, Task<RemediationResult> task)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Session = remediationSession;
                session.Task = task;
                session.Status = RemediationStatus.Running;
                _logger.LogInformation($"[SESSION-MANAGER] Updated session {sessionId} to Running");
            }
        }

        public void CompleteSession(string sessionId, RemediationResult result)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Result = result;
                // Mark as completed if we have an output PDF, regardless of full compliance
                // ThresholdMet is a successful completion even if not 100% compliant
                session.Status = (result.OutputPdf != null) ? RemediationStatus.Completed : RemediationStatus.Failed;
                _logger.LogInformation($"[SESSION-MANAGER] Session {sessionId} completed: {session.Status}, Exit: {result.ExitReason}");
            }
        }

        public void FailSession(string sessionId, string errorMessage)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Status = RemediationStatus.Failed;
                _logger.LogError($"[SESSION-MANAGER] Session {sessionId} failed: {errorMessage}");
            }
        }

        public ActiveRemediationSession GetSession(string sessionId)
        {
            _sessions.TryGetValue(sessionId, out var session);
            return session;
        }

        public bool CancelSession(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.CancellationToken?.Cancel();
                session.Status = RemediationStatus.Cancelled;
                _logger.LogWarning($"[SESSION-MANAGER] Session {sessionId} cancelled");
                return true;
            }
            return false;
        }

        public void CleanupOldSessions(TimeSpan maxAge)
        {
            var cutoffTime = DateTime.UtcNow - maxAge;
            var toRemove = new List<string>();

            foreach (var kvp in _sessions)
            {
                if (kvp.Value.StartTime < cutoffTime &&
                    (kvp.Value.Status == RemediationStatus.Completed ||
                     kvp.Value.Status == RemediationStatus.Failed ||
                     kvp.Value.Status == RemediationStatus.Cancelled))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var sessionId in toRemove)
            {
                if (_sessions.TryRemove(sessionId, out var session))
                {
                    session.CancellationToken?.Dispose();
                    _logger.LogInformation($"[SESSION-MANAGER] Cleaned up session {sessionId}");
                }
            }
        }

        public int GetActiveSessionCount()
        {
            return _sessions.Count(s => s.Value.Status == RemediationStatus.Running || s.Value.Status == RemediationStatus.Starting);
        }
    }
}
