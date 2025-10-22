using System.Collections.Generic;

namespace WordToPdfConverter.Services.Remediation.Strategy
{
    /// <summary>
    /// Represents a distinct phase of remediation with specific services
    /// </summary>
    public class RemediationPhase
    {
        public string Name { get; set; }
        public int Order { get; set; }
        public List<IRemediationService> Services { get; set; } = new();
        public int MaxIterations { get; set; } = 1;
        public bool Required { get; set; } = false;
        public ViolationCategory TargetCategory { get; set; }
    }

    /// <summary>
    /// Complete remediation strategy with all phases
    /// </summary>
    public class RemediationStrategy
    {
        public List<RemediationPhase> Phases { get; set; } = new();
    }
}
