using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services
{
    public class CostTrackingService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<CostTrackingService> _logger;
        private decimal _dailyTotal = 0;
        private int _requestCount = 0;
        private readonly decimal _dailyLimit;

        // Session-based tracking for detailed per-document costs
        private readonly ConcurrentDictionary<string, SessionCostData> _sessions = new();

        // Pricing constants (per million tokens)
        public static class Pricing
        {
            // Anthropic Claude Sonnet 4.5
            public const decimal CLAUDE_SONNET_45_INPUT = 3.00m / 1_000_000m;
            public const decimal CLAUDE_SONNET_45_OUTPUT = 15.00m / 1_000_000m;

            // OpenAI GPT-5
            public const decimal GPT5_INPUT = 1.25m / 1_000_000m;
            public const decimal GPT5_OUTPUT = 10.00m / 1_000_000m;

            // OpenAI GPT-4o
            public const decimal GPT4O_INPUT = 2.50m / 1_000_000m;
            public const decimal GPT4O_OUTPUT = 10.00m / 1_000_000m;
        }

        public CostTrackingService(IConfiguration configuration, ILogger<CostTrackingService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _dailyLimit = _configuration.GetValue<decimal>("AiServices:CostLimits:DailyLimit", 10.0m);
        }

        public Task<bool> CanProcessRequestAsync(decimal estimatedCost)
        {
            var canProcess = (_dailyTotal + estimatedCost) <= _dailyLimit;
            if (!canProcess)
            {
                _logger.LogWarning("Daily cost limit would be exceeded. Current: ${Current:F2}, Limit: ${Limit:F2}",
                    _dailyTotal, _dailyLimit);
            }
            return Task.FromResult(canProcess);
        }

        public Task RecordCostAsync(string service, decimal cost)
        {
            _dailyTotal += cost;
            _requestCount++;
            _logger.LogInformation("Recorded cost: {Service} = ${Cost:F4}. Daily total: ${Total:F2}",
                service, cost, _dailyTotal);
            return Task.CompletedTask;
        }

        // Session-based tracking methods
        public void StartSession(string sessionId)
        {
            _sessions[sessionId] = new SessionCostData
            {
                SessionId = sessionId,
                StartTime = DateTime.UtcNow
            };
            _logger.LogInformation("💰 Started cost tracking for session: {SessionId}", sessionId);
        }

        public void RecordSessionCost(string sessionId, string serviceName, int inputTokens, int outputTokens, decimal cost)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                _logger.LogWarning("Session {SessionId} not found, creating new session", sessionId);
                StartSession(sessionId);
                session = _sessions[sessionId];
            }

            var detail = new ServiceCostDetail
            {
                ServiceName = serviceName,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                Cost = cost,
                Timestamp = DateTime.UtcNow
            };

            session.ServiceCosts.Add(detail);
            session.TotalCost += cost;

            // Also record to daily total
            _dailyTotal += cost;
            _requestCount++;

            _logger.LogInformation(
                "💰 {Service}: ${Cost:F4} ({InputTokens:N0} in, {OutputTokens:N0} out) | Session total: ${Total:F4}",
                serviceName, cost, inputTokens, outputTokens, session.TotalCost);
        }

        public SessionCostSummary GetSessionSummary(string sessionId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return null;
            }

            var summary = new SessionCostSummary
            {
                SessionId = sessionId,
                TotalCost = session.TotalCost,
                Duration = DateTime.UtcNow - session.StartTime,
                ServiceBreakdown = new Dictionary<string, ServiceCostSummary>()
            };

            // Group by service and aggregate
            var grouped = session.ServiceCosts.GroupBy(c => c.ServiceName);
            foreach (var group in grouped)
            {
                summary.ServiceBreakdown[group.Key] = new ServiceCostSummary
                {
                    ServiceName = group.Key,
                    TotalCost = group.Sum(c => c.Cost),
                    TotalInputTokens = group.Sum(c => c.InputTokens),
                    TotalOutputTokens = group.Sum(c => c.OutputTokens),
                    CallCount = group.Count()
                };
            }

            return summary;
        }

        public void EndSession(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                _logger.LogInformation(
                    "💰 Session {SessionId} ended. Total cost: ${Cost:F4} ({Count} API calls)",
                    sessionId, session.TotalCost, session.ServiceCosts.Count);
            }
        }

        public DailyCostSummary GetDailySummary()
        {
            return new DailyCostSummary
            {
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Total = _dailyTotal,
                RequestCount = _requestCount,
                PercentOfLimit = (_dailyTotal / _dailyLimit) * 100
            };
        }

        public WeeklyCostSummary GetWeeklySummary()
        {
            return new WeeklyCostSummary
            {
                Total = _dailyTotal * 7,
                AverageDaily = _dailyTotal,
                ProjectedMonthly = _dailyTotal * 30
            };
        }
    }

    public class DailyCostSummary
    {
        public string Date { get; set; } = "";
        public decimal Total { get; set; }
        public Dictionary<string, decimal> ServiceCosts { get; set; } = new();
        public int RequestCount { get; set; }
        public decimal PercentOfLimit { get; set; }
    }

    public class WeeklyCostSummary
    {
        public decimal Total { get; set; }
        public decimal AverageDaily { get; set; }
        public decimal ProjectedMonthly { get; set; }
        public List<DailyCostSummary> DailySummaries { get; set; } = new();
    }

    // Session-based cost tracking models
    public class SessionCostData
    {
        public string SessionId { get; set; }
        public DateTime StartTime { get; set; }
        public decimal TotalCost { get; set; }
        public List<ServiceCostDetail> ServiceCosts { get; set; } = new();
    }

    public class ServiceCostDetail
    {
        public string ServiceName { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public decimal Cost { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class SessionCostSummary
    {
        public string SessionId { get; set; }
        public decimal TotalCost { get; set; }
        public TimeSpan Duration { get; set; }
        public Dictionary<string, ServiceCostSummary> ServiceBreakdown { get; set; } = new();
    }

    public class ServiceCostSummary
    {
        public string ServiceName { get; set; }
        public decimal TotalCost { get; set; }
        public int TotalInputTokens { get; set; }
        public int TotalOutputTokens { get; set; }
        public int CallCount { get; set; }

        public int TotalTokens => TotalInputTokens + TotalOutputTokens;
    }
}
