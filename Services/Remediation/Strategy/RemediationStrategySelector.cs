using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
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
        private readonly IConfiguration _configuration;

        public RemediationStrategySelector(
            ILogger<RemediationStrategySelector> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
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

            // Phase 0: AI Structure Rebuild (Order: 0, MaxIter: 1) - RUNS FIRST if enabled
            // This is a foundational phase that rebuilds the PDF structure from scratch using AI analysis
            // Only runs when RemediationPipeline:EnableStructureRebuild config is true
            var enableStructureRebuild = _configuration.GetValue<bool>("RemediationPipeline:EnableStructureRebuild", false);
            if (enableStructureRebuild && session.IterationCount == 1) // Only run on first iteration
            {
                _logger.LogInformation("[PHASE-0] Structure rebuild enabled - will run AI-powered structure rebuild");
                phases.Add(new RemediationPhase
                {
                    Name = "AI Structure Rebuild",
                    Order = 0,
                    TargetCategory = ViolationCategory.StructureRebuild,
                    MaxIterations = 1,
                    Services = GetServicesForCategory(ViolationCategory.StructureRebuild)
                });
            }
            else if (enableStructureRebuild)
            {
                _logger.LogInformation("[PHASE-0] Structure rebuild skipped - only runs on iteration 1");
            }

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

            // Phase 1.5: Structure Enhancement (Order: 2, MaxIter: 2) - Moved earlier to fix 7.1-3 violations
            if (analysis.Categories.ContainsKey(ViolationCategory.Structure))
            {
                phases.Add(new RemediationPhase
                {
                    Name = "Structure Enhancement",
                    Order = 2, // Changed from 3 - run earlier to fix content marking issues (same as Content)
                    TargetCategory = ViolationCategory.Structure,
                    MaxIterations = 2, // Increased from 1 to handle edge cases
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

            // Phase 5b: Annotation Fixes (Order: 5, MaxIter: 2)
            if (analysis.Categories.ContainsKey(ViolationCategory.Annotations))
            {
                phases.Add(new RemediationPhase
                {
                    Name = "Annotation Content Fixes",
                    Order = 5,
                    TargetCategory = ViolationCategory.Annotations,
                    MaxIterations = 2,
                    Services = GetServicesForCategory(ViolationCategory.Annotations)
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

            // Phase 7: Table and List Structure (Order: 7, MaxIter: 2)
            if (analysis.Categories.ContainsKey(ViolationCategory.TableAndList))
            {
                phases.Add(new RemediationPhase
                {
                    Name = "Table and List Structure Fixes",
                    Order = 7,
                    TargetCategory = ViolationCategory.TableAndList,
                    MaxIterations = 2,
                    Services = GetServicesForCategory(ViolationCategory.TableAndList)
                });
            }

            // Phase 8: Alternative Text (Order: 8, MaxIter: 1)
            if (analysis.Categories.ContainsKey(ViolationCategory.AlternateText))
            {
                phases.Add(new RemediationPhase
                {
                    Name = "Alternative Text for Images",
                    Order = 8,
                    TargetCategory = ViolationCategory.AlternateText,
                    MaxIterations = 1,
                    Services = GetServicesForCategory(ViolationCategory.AlternateText)
                });
            }

            // Phase 9: Metadata Finalization (Order: 99, MaxIter: 1)
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

        /// <summary>
        /// Builds a special cleanup strategy that runs all fix services
        /// This is used for post-remediation cleanup to fix issues introduced during remediation
        /// </summary>
        public RemediationStrategy BuildCleanupStrategy()
        {
            var strategy = new RemediationStrategy();
            var phases = new List<RemediationPhase>();

            // Create a single cleanup phase with all fix services
            var cleanupPhase = new RemediationPhase
            {
                Name = "Post-Remediation Structural Cleanup",
                Order = 1,
                MaxIterations = 1,
                Services = new List<IRemediationService>()
            };

            // Add all the fix services that clean up common structural issues
            // These are the services that fix problems often introduced during remediation

            // PHASE 6D: Artifact fix moved to preflight - no longer runs here
            // Artifact/tagged content conflicts (7.1 violations) are now handled before MCID work

            // 1. Fix content by specific index (7.1-3 violations for specific content[33] etc)
            var contentIndexService = _serviceProvider.GetService(
                typeof(Fixes.ContentIndexArtifactFixService)) as IRemediationService;
            if (contentIndexService != null)
                cleanupPhase.Services.Add(contentIndexService);

            // 2. Fix unmarked XObject content (7.1-3 violations)
            var unmarkedXObjectService = _serviceProvider.GetService(
                typeof(Fixes.UnmarkedXObjectContentFixService)) as IRemediationService;
            if (unmarkedXObjectService != null)
                cleanupPhase.Services.Add(unmarkedXObjectService);

            // 3. Fix form widget nesting (7.18.4 violations)
            var formWidgetService = _serviceProvider.GetService(
                typeof(Fixes.FormWidgetNestingFixService)) as IRemediationService;
            if (formWidgetService != null)
                cleanupPhase.Services.Add(formWidgetService);

            // 4. Fix form role attributes (7.18.4-2 violations)
            var formRoleService = _serviceProvider.GetService(
                typeof(Fixes.FormRoleAttributeFixService)) as IRemediationService;
            if (formRoleService != null)
                cleanupPhase.Services.Add(formRoleService);

            // 5. Remove empty form elements (7.18.4-2 violations)
            var emptyFormService = _serviceProvider.GetService(
                typeof(Fixes.EmptyFormElementRemovalService)) as IRemediationService;
            if (emptyFormService != null)
                cleanupPhase.Services.Add(emptyFormService);

            // 6. Fix table structure (7.2 violations)
            var tableStructureService = _serviceProvider.GetService(
                typeof(Fixes.TableStructureValidationService)) as IRemediationService;
            if (tableStructureService != null)
                cleanupPhase.Services.Add(tableStructureService);

            // 7. Add table scope attributes
            var tableScopeService = _serviceProvider.GetService(
                typeof(Fixes.TableScopeAttributeFixService)) as IRemediationService;
            if (tableScopeService != null)
                cleanupPhase.Services.Add(tableScopeService);

            // 8. Fix figure alt text
            var figureAltTextService = _serviceProvider.GetService(
                typeof(Fixes.FigureAltTextService)) as IRemediationService;
            if (figureAltTextService != null)
                cleanupPhase.Services.Add(figureAltTextService);

            // 9. Fix PDF/UA metadata (always last in cleanup)
            var pdfUaMetadataService = _serviceProvider.GetService(
                typeof(Fixes.PdfUaMetadataService)) as IRemediationService;
            if (pdfUaMetadataService != null)
                cleanupPhase.Services.Add(pdfUaMetadataService);

            // Only add phase if we have services
            if (cleanupPhase.Services.Any())
            {
                phases.Add(cleanupPhase);
            }

            strategy.Phases = phases;
            return strategy;
        }

        private List<IRemediationService> GetServicesForCategory(
            ViolationCategory category)
        {
            var services = new List<IRemediationService>();

            // Get service from DI container based on category
            switch (category)
            {
                case ViolationCategory.StructureRebuild:
                    // Phase 0: AI-powered complete structure rebuild
                    var structureRebuildService = _serviceProvider.GetService(
                        typeof(Adapters.StructureRebuildServiceAdapter)) as IRemediationService;
                    if (structureRebuildService != null)
                        services.Add(structureRebuildService);
                    break;

                case ViolationCategory.Whitespace:
                    var whitespaceService = _serviceProvider.GetService(
                        typeof(Adapters.WhitespaceServiceAdapter)) as IRemediationService;
                    if (whitespaceService != null)
                        services.Add(whitespaceService);
                    break;

                case ViolationCategory.Content:
                    // Add unmarked XObject content fix (for 7.1-3 violations)
                    var unmarkedXObjectService = _serviceProvider.GetService(
                        typeof(Fixes.UnmarkedXObjectContentFixService)) as IRemediationService;
                    if (unmarkedXObjectService != null)
                        services.Add(unmarkedXObjectService);

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

                case ViolationCategory.Annotations:
                    // Annotations (7.18.1) use the same link service adapter
                    var annotationService = _serviceProvider.GetService(
                        typeof(Adapters.LinkServiceAdapter)) as IRemediationService;
                    if (annotationService != null)
                        services.Add(annotationService);
                    break;

                case ViolationCategory.Fonts:
                    // Add CIDSet fix service first (fixes 7.21.4.2-2 violations)
                    var cidSetService = _serviceProvider.GetService(
                        typeof(Fixes.FontCIDSetFixService)) as IRemediationService;
                    if (cidSetService != null)
                        services.Add(cidSetService);

                    // Then add font embedding service (handles general font embedding)
                    var fontService = _serviceProvider.GetService(
                        typeof(Adapters.FontEmbeddingServiceAdapter)) as IRemediationService;
                    if (fontService != null)
                        services.Add(fontService);
                    break;

                case ViolationCategory.Structure:
                    // Add AI structure rebuild FIRST - rebuilds entire tag tree based on visual layout
                    var aiStructureRebuildService = _serviceProvider.GetService(
                        typeof(StructureRebuildService)) as IRemediationService;
                    if (aiStructureRebuildService != null)
                        services.Add(aiStructureRebuildService);

                    // Then add circular role mapping fix
                    var structureService = _serviceProvider.GetService(
                        typeof(CircularRoleMappingFixService)) as IRemediationService;
                    if (structureService != null)
                        services.Add(structureService);

                    // Add content index artifact fix (for specific content[33] violations)
                    var contentIndexService = _serviceProvider.GetService(
                        typeof(Fixes.ContentIndexArtifactFixService)) as IRemediationService;
                    if (contentIndexService != null)
                        services.Add(contentIndexService);

                    // PHASE 6D: Artifact fix moved to preflight - no longer runs here
                    // Artifact/tagged content conflicts (7.1 violations) are now handled before MCID work
                    break;

                case ViolationCategory.FormFields:
                    // Add form widget nesting fix
                    var formWidgetService = _serviceProvider.GetService(
                        typeof(Fixes.FormWidgetNestingFixService)) as IRemediationService;
                    if (formWidgetService != null)
                        services.Add(formWidgetService);

                    // Add form role attribute fix
                    var formRoleService = _serviceProvider.GetService(
                        typeof(Fixes.FormRoleAttributeFixService)) as IRemediationService;
                    if (formRoleService != null)
                        services.Add(formRoleService);

                    // Add empty form element removal fix
                    var emptyFormService = _serviceProvider.GetService(
                        typeof(Fixes.EmptyFormElementRemovalService)) as IRemediationService;
                    if (emptyFormService != null)
                        services.Add(emptyFormService);

                    // GPT as fallback
                    var formFieldsGptService = _serviceProvider.GetService(
                        typeof(Adapters.GptServiceAdapter)) as IRemediationService;
                    if (formFieldsGptService != null)
                        services.Add(formFieldsGptService);
                    break;

                case ViolationCategory.Metadata:
                    // Add PDF/UA metadata service
                    var pdfUaMetadataService = _serviceProvider.GetService(
                        typeof(Fixes.PdfUaMetadataService)) as IRemediationService;
                    if (pdfUaMetadataService != null)
                        services.Add(pdfUaMetadataService);

                    // GPT as fallback
                    var metadataGptService = _serviceProvider.GetService(
                        typeof(Adapters.GptServiceAdapter)) as IRemediationService;
                    if (metadataGptService != null)
                        services.Add(metadataGptService);
                    break;

                case ViolationCategory.TableAndList:
                    // Add table scope attribute fix
                    var tableScopeService = _serviceProvider.GetService(
                        typeof(Fixes.TableScopeAttributeFixService)) as IRemediationService;
                    if (tableScopeService != null)
                        services.Add(tableScopeService);

                    // Add table structure validation
                    var tableStructureService = _serviceProvider.GetService(
                        typeof(Fixes.TableStructureValidationService)) as IRemediationService;
                    if (tableStructureService != null)
                        services.Add(tableStructureService);
                    break;

                case ViolationCategory.AlternateText:
                    // Add figure alt text service
                    var figureAltTextService = _serviceProvider.GetService(
                        typeof(Fixes.FigureAltTextService)) as IRemediationService;
                    if (figureAltTextService != null)
                        services.Add(figureAltTextService);
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
