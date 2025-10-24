using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Analysis;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Strategy
{
    /// <summary>
    /// Selects appropriate remediation strategies based on violation analysis
    /// </summary>
    public class RemediationStrategySelector
    {
        private readonly ILogger<RemediationStrategySelector> _logger;
        private readonly IServiceProvider _serviceProvider;

        public RemediationStrategySelector(
            ILogger<RemediationStrategySelector> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task<RemediationStrategy> SelectAsync(
            ViolationAnalysis analysis,
            RemediationSession session)
        {
            var strategy = new RemediationStrategy();

            // Build execution phases based on violation categories
            strategy.Phases = BuildExecutionPhases(analysis, session);

            _logger.LogInformation(
                $"Selected strategy with {strategy.Phases.Count} phases");

            return await Task.FromResult(strategy);
        }

        private List<RemediationPhase> BuildExecutionPhases(
            ViolationAnalysis analysis,
            RemediationSession session)
        {
            var phases = new List<RemediationPhase>();

            // Phase 1: Whitespace Cleanup (Order: 1, MaxIter: 2)
            if (analysis.Categories.ContainsKey(ViolationCategory.Whitespace))
            {
                phases.Add(new RemediationPhase
                {
                    Name = "Whitespace Cleanup",
                    Order = 1,
                    TargetCategory = ViolationCategory.Whitespace,
                    MaxIterations = 2,
                    Services = GetServicesForCategory(ViolationCategory.Whitespace)
                });
            }

            // Phase 2: Content Remediation (Order: 2, MaxIter: 3)
            if (analysis.Categories.ContainsKey(ViolationCategory.Content))
            {
                phases.Add(new RemediationPhase
                {
                    Name = "Content Remediation",
                    Order = 2,
                    TargetCategory = ViolationCategory.Content,
                    MaxIterations = 3,
                    Services = GetServicesForCategory(ViolationCategory.Content)
                });
            }

            // Phase 3: Structure Enhancement (Order: 3, MaxIter: 1)
            if (analysis.Categories.ContainsKey(ViolationCategory.Structure))
            {
                phases.Add(new RemediationPhase
                {
                    Name = "Structure Enhancement",
                    Order = 3,
                    TargetCategory = ViolationCategory.Structure,
                    MaxIterations = 1,
                    Services = GetServicesForCategory(ViolationCategory.Structure)
                });
            }

            // Phase 4: Form Field Remediation (Order: 4, MaxIter: 2)
            if (analysis.Categories.ContainsKey(ViolationCategory.FormFields))
            {
                phases.Add(new RemediationPhase
                {
                    Name = "Form Field Remediation",
                    Order = 4,
                    TargetCategory = ViolationCategory.FormFields,
                    MaxIterations = 2,
                    Services = GetServicesForCategory(ViolationCategory.FormFields)
                });
            }

            // Phase 5: Link Structure Fixes (Order: 5, MaxIter: 2)
            if (analysis.Categories.ContainsKey(ViolationCategory.Links))
            {
                phases.Add(new RemediationPhase
                {
                    Name = "Link Structure Fixes",
                    Order = 5,
                    TargetCategory = ViolationCategory.Links,
                    MaxIterations = 2,
                    Services = GetServicesForCategory(ViolationCategory.Links)
                });
            }

            // Phase 6: Font/PDF-A Conversion (Order: 6, MaxIter: 1)
            if (analysis.Categories.ContainsKey(ViolationCategory.Fonts))
            {
                phases.Add(new RemediationPhase
                {
                    Name = "Font Fixes & PDF/A Conversion",
                    Order = 6,
                    TargetCategory = ViolationCategory.Fonts,
                    MaxIterations = 1,
                    Services = GetServicesForCategory(ViolationCategory.Fonts)
                });
            }

            // Phase 7: Metadata Finalization (Order: 99, MaxIter: 1)
            // ALWAYS LAST - Category 1 "Handle Last"
            if (analysis.Categories.ContainsKey(ViolationCategory.Metadata) ||
                session.IsFinalIteration)
            {
                phases.Add(new RemediationPhase
                {
                    Name = "Metadata Finalization",
                    Order = 99,
                    TargetCategory = ViolationCategory.Metadata,
                    MaxIterations = 1,
                    Services = GetServicesForCategory(ViolationCategory.Metadata)
                });
            }

            return phases.OrderBy(p => p.Order).ToList();
        }

        private List<IRemediationService> GetServicesForCategory(
            ViolationCategory category)
        {
            var services = new List<IRemediationService>();

            // Get service from DI container based on category
            switch (category)
            {
                case ViolationCategory.Whitespace:
                    var whitespaceService = _serviceProvider.GetService(
                        typeof(Adapters.WhitespaceServiceAdapter)) as IRemediationService;
                    if (whitespaceService != null)
                        services.Add(whitespaceService);
                    break;

                case ViolationCategory.Content:
                    var contentService = _serviceProvider.GetService(
                        typeof(Adapters.ContentServiceAdapter)) as IRemediationService;
                    if (contentService != null)
                        services.Add(contentService);
                    break;

                case ViolationCategory.Links:
                    var linkService = _serviceProvider.GetService(
                        typeof(Adapters.LinkServiceAdapter)) as IRemediationService;
                    if (linkService != null)
                        services.Add(linkService);
                    break;

                case ViolationCategory.Fonts:
                    var fontService = _serviceProvider.GetService(
                        typeof(Adapters.FontEmbeddingServiceAdapter)) as IRemediationService;
                    if (fontService != null)
                        services.Add(fontService);
                    break;

                case ViolationCategory.Structure:
                    var structureService = _serviceProvider.GetService(
                        typeof(CircularRoleMappingFixService)) as IRemediationService;
                    if (structureService != null)
                        services.Add(structureService);
                    break;

                case ViolationCategory.FormFields:
                    // No specialized service yet, use GPT fallback
                    var formFieldsGptService = _serviceProvider.GetService(
                        typeof(Adapters.GptServiceAdapter)) as IRemediationService;
                    if (formFieldsGptService != null)
                        services.Add(formFieldsGptService);
                    break;

                case ViolationCategory.Metadata:
                    // Use GPT for metadata violations
                    var metadataGptService = _serviceProvider.GetService(
                        typeof(Adapters.GptServiceAdapter)) as IRemediationService;
                    if (metadataGptService != null)
                        services.Add(metadataGptService);
                    break;

                // Add other categories as we build more adapters
                default:
                    _logger.LogWarning($"No service adapter registered for category: {category}");
                    break;
            }

            return services;
        }
    }
}
