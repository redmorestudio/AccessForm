using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Models.Remediation;

/// <summary>
/// Phase 6b Pipeline Integration: Job-wide shared context for an entire remediation session.
///
/// This context is registered as SCOPED in DI, ensuring all services within a single
/// remediation job receive the same instance. This allows context flags (like
/// StructureRebuildExecuted and McidContentRewriteExecuted) to persist across services,
/// enabling guards to prevent secondary structure rebuilds that would overwrite MCID markers.
///
/// CRITICAL: Do NOT create new instances of StructureRebuildContext anywhere except here.
/// All services must access the shared context through this object.
/// </summary>
public sealed class RemediationJobContext
{
    /// <summary>
    /// Shared structure rebuild context for this remediation job.
    /// All services must use this single instance to ensure flags persist.
    /// </summary>
    public StructureRebuildContext StructureContext { get; } = new();

    /// <summary>
    /// Phase 6E: Remediation options for this job, including MCID settings.
    /// Set by RemediationOrchestrator at job start and accessible to all services.
    /// </summary>
    public RemediationOptions Options { get; set; }
}
