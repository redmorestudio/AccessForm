using WordToPdfConverter.Models.Remediation;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Pdf;

/// <summary>
/// Stub implementation of IPdfStructureWriter that returns the original PDF unchanged.
/// This is used for initial pipeline wiring and testing before the real implementation is ready.
/// Will be replaced with ITextPdfStructureWriter (iText7-based implementation).
/// </summary>
public sealed class StubPdfStructureWriter : IPdfStructureWriter
{
    public byte[] Rewrite(byte[] originalPdf, StructureTree tree, StructureRebuildContext? context = null)
    {
        // TODO: replace with real implementation using iText7 or Python microservice with pikepdf.
        // For now, just return the original PDF unchanged.
        return originalPdf;
    }
}
