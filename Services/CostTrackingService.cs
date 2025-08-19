using System;
using System.Collections.Generic;
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
}
