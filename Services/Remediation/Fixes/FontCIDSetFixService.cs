using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iText.IO.Font;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Fixes
{
    /// <summary>
    /// Fixes CIDSet streams in embedded CID fonts to include all CIDs present in the font program
    /// Handles 7.21.4.2-2 violations (CIDSet must identify all CIDs in font, not just used ones)
    /// </summary>
    public class FontCIDSetFixService : IRemediationService
    {
        private readonly ILogger<FontCIDSetFixService> _logger;

        public string ServiceName => "Font CIDSet Fix Service";
        public ViolationCategory TargetCategory => ViolationCategory.Fonts;
        public int Priority => 8;
        public bool IsRequired => false;

        public FontCIDSetFixService(ILogger<FontCIDSetFixService> logger)
        {
            _logger = logger;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ServiceResult
            {
                Success = false,
                OutputPdf = pdfBytes
            };

            try
            {
                _logger.LogInformation("[FONT-CIDSET] Starting CIDSet remediation");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                var fixedCount = 0;
                var skippedCount = 0;

                // Process all fonts in all pages
                for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
                {
                    var page = pdfDoc.GetPage(pageNum);
                    var resources = page.GetResources();

                    // Get the Font resource dictionary
                    var fontsDict = resources.GetResource(PdfName.Font);
                    if (fontsDict == null || !(fontsDict is PdfDictionary))
                        continue;

                    var fontsDictionary = (PdfDictionary)fontsDict;
                    var fontNames = fontsDictionary.KeySet();

                    foreach (var fontName in fontNames)
                    {
                        try
                        {
                            var fontDict = fontsDictionary.GetAsDictionary(fontName);
                            if (fontDict == null)
                                continue;

                            // Check if it's a Type0 (CID) font
                            var subtype = fontDict.GetAsName(PdfName.Subtype);
                            if (subtype == null || !subtype.Equals(PdfName.Type0))
                                continue;

                            // Get descendant fonts array
                            var descendantFonts = fontDict.GetAsArray(PdfName.DescendantFonts);
                            if (descendantFonts == null || descendantFonts.Size() == 0)
                                continue;

                            var cidFont = descendantFonts.GetAsDictionary(0);
                            if (cidFont == null)
                                continue;

                            // Get the FontDescriptor
                            var fontDescriptor = cidFont.GetAsDictionary(PdfName.FontDescriptor);
                            if (fontDescriptor == null)
                                continue;

                            // Check if font is embedded (has FontFile, FontFile2, or FontFile3)
                            var fontFile = fontDescriptor.Get(PdfName.FontFile) ??
                                          fontDescriptor.Get(PdfName.FontFile2) ??
                                          fontDescriptor.Get(PdfName.FontFile3);

                            if (fontFile == null)
                            {
                                // Font not embedded, can't fix CIDSet
                                continue;
                            }

                            // Check if CIDSet already exists and is valid
                            var existingCIDSet = fontDescriptor.GetAsStream(PdfName.CIDSet);
                            if (existingCIDSet != null)
                            {
                                // Validate the CIDSet covers all glyphs
                                // For now, we'll regenerate it to be safe
                                _logger.LogInformation($"[FONT-CIDSET] Font '{fontName}' has CIDSet, will regenerate to ensure completeness");
                            }

                            // Generate complete CIDSet
                            var cidSet = GenerateCompleteCIDSet(cidFont, fontDescriptor);
                            if (cidSet != null)
                            {
                                // Create new CIDSet stream
                                var cidSetStream = new PdfStream(cidSet);
                                fontDescriptor.Put(PdfName.CIDSet, cidSetStream);
                                fixedCount++;
                                _logger.LogInformation($"[FONT-CIDSET] Fixed CIDSet for font '{fontName}' on page {pageNum}");
                            }
                            else
                            {
                                skippedCount++;
                                _logger.LogWarning($"[FONT-CIDSET] Could not generate CIDSet for font '{fontName}' on page {pageNum}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[FONT-CIDSET] Error processing font '{fontName}' on page {pageNum}: {ex.Message}");
                            skippedCount++;
                        }
                    }
                }

                pdfDoc.Close();

                stopwatch.Stop();

                if (fixedCount > 0)
                {
                    result.Success = true;
                    result.ChangesMade = true;
                    result.OutputPdf = outputMs.ToArray();
                    result.IssuesFixed = fixedCount;
                    result.IssuesFound = fixedCount + skippedCount;
                    _logger.LogInformation($"[FONT-CIDSET] Completed in {stopwatch.ElapsedMilliseconds}ms: fixed {fixedCount} fonts, skipped {skippedCount}");
                }
                else
                {
                    result.Success = true;
                    result.ChangesMade = false;
                    _logger.LogInformation($"[FONT-CIDSET] No CIDSet issues found ({stopwatch.ElapsedMilliseconds}ms)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FONT-CIDSET] Fatal error during CIDSet remediation");
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Generate a complete CIDSet that includes all CIDs in the font program
        /// Per PDF/UA spec 7.21.4.2-2: CIDSet must identify ALL CIDs present in font program
        /// This extracts the actual glyph count from the embedded font and generates an appropriate CIDSet
        /// </summary>
        private byte[] GenerateCompleteCIDSet(PdfDictionary cidFont, PdfDictionary fontDescriptor)
        {
            try
            {
                // Strategy: Extract max CID from multiple sources and generate CIDSet for that range
                int maxCID = -1;

                // 1. Try to get max CID from the embedded font file itself
                maxCID = GetMaxCIDFromFontFile(fontDescriptor);
                _logger.LogInformation($"[FONT-CIDSET] Max CID from font file: {maxCID}");

                // 2. If that fails, try other sources (CIDToGIDMap, W array, etc.)
                if (maxCID < 0)
                {
                    maxCID = GetMaxCIDFromFontMetadata(cidFont);
                    _logger.LogInformation($"[FONT-CIDSET] Max CID from metadata: {maxCID}");
                }

                // 3. If we still don't have a max CID, use a conservative default
                if (maxCID < 0)
                {
                    maxCID = 255; // Most subsetted fonts have < 256 glyphs
                    _logger.LogWarning($"[FONT-CIDSET] Could not determine max CID, using default: {maxCID}");
                }

                // 4. Generate CIDSet marking CIDs 0 through maxCID as present
                byte[] cidSetBytes = GenerateCIDSetForRange(0, maxCID);

                _logger.LogInformation($"[FONT-CIDSET] Generated CIDSet: {cidSetBytes.Length} bytes for CIDs 0-{maxCID}");

                return cidSetBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FONT-CIDSET] Error generating CIDSet");
                return null;
            }
        }

        /// <summary>
        /// Extract max CID from the embedded font file by parsing the font structure
        /// </summary>
        private int GetMaxCIDFromFontFile(PdfDictionary fontDescriptor)
        {
            try
            {
                // Get the embedded font stream (FontFile2 for TrueType, FontFile3 for CIDFont)
                var fontStream = fontDescriptor.GetAsStream(PdfName.FontFile2) ??
                                fontDescriptor.GetAsStream(PdfName.FontFile3);

                if (fontStream == null)
                {
                    _logger.LogDebug("[FONT-CIDSET] No embedded font file found");
                    return -1;
                }

                byte[] fontData = fontStream.GetBytes();
                if (fontData == null || fontData.Length < 12)
                {
                    _logger.LogDebug("[FONT-CIDSET] Font file too small or empty");
                    return -1;
                }

                // Parse TrueType/OpenType font to get numGlyphs from 'maxp' table
                int numGlyphs = ParseMaxpTable(fontData);

                if (numGlyphs > 0)
                {
                    // CIDs are 0-indexed, so max CID = numGlyphs - 1
                    return numGlyphs - 1;
                }

                return -1;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FONT-CIDSET] Error parsing font file: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Parse TrueType/OpenType font to extract numGlyphs from 'maxp' table
        /// </summary>
        private int ParseMaxpTable(byte[] fontData)
        {
            try
            {
                // Check if this is a TrueType font collection (TTC)
                if (fontData.Length >= 4)
                {
                    string tag = System.Text.Encoding.ASCII.GetString(fontData, 0, 4);
                    if (tag == "ttcf")
                    {
                        // TTC file - use first font
                        if (fontData.Length >= 16)
                        {
                            int offset = ReadUInt32BigEndian(fontData, 12);
                            if (offset < fontData.Length)
                            {
                                return ParseMaxpTableAtOffset(fontData, offset);
                            }
                        }
                        return -1;
                    }
                }

                // Regular TrueType/OpenType font
                return ParseMaxpTableAtOffset(fontData, 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FONT-CIDSET] Error parsing maxp table: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Parse maxp table starting at given offset in font data
        /// </summary>
        private int ParseMaxpTableAtOffset(byte[] fontData, int baseOffset)
        {
            try
            {
                // Read the offset table (first 12 bytes)
                if (fontData.Length < baseOffset + 12)
                    return -1;

                // sfntVersion is at baseOffset (4 bytes)
                // numTables is at baseOffset + 4 (2 bytes)
                int numTables = ReadUInt16BigEndian(fontData, baseOffset + 4);

                // Table directory starts at baseOffset + 12
                int tableDirOffset = baseOffset + 12;

                // Each table directory entry is 16 bytes: tag(4) + checksum(4) + offset(4) + length(4)
                for (int i = 0; i < numTables; i++)
                {
                    int entryOffset = tableDirOffset + (i * 16);

                    if (entryOffset + 16 > fontData.Length)
                        break;

                    string tableTag = System.Text.Encoding.ASCII.GetString(fontData, entryOffset, 4);

                    if (tableTag == "maxp")
                    {
                        // Found maxp table
                        int tableOffset = ReadInt32BigEndian(fontData, entryOffset + 8);

                        if (tableOffset + 6 <= fontData.Length)
                        {
                            // numGlyphs is at offset 4 in maxp table (2 bytes, big-endian)
                            int numGlyphs = ReadUInt16BigEndian(fontData, tableOffset + 4);
                            _logger.LogDebug($"[FONT-CIDSET] Found maxp table: numGlyphs = {numGlyphs}");
                            return numGlyphs;
                        }
                    }
                }

                return -1;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FONT-CIDSET] Error parsing maxp table at offset: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Get max CID from font metadata (CIDToGIDMap, W array, etc.)
        /// </summary>
        private int GetMaxCIDFromFontMetadata(PdfDictionary cidFont)
        {
            int maxCID = -1;

            try
            {
                // 1. Check CIDToGIDMap stream
                var cidToGIDMap = cidFont.Get(PdfName.CIDToGIDMap);
                if (cidToGIDMap is PdfStream stream)
                {
                    try
                    {
                        byte[] data = stream.GetBytes();
                        if (data != null && data.Length >= 2)
                        {
                            // CIDToGIDMap is array of uint16 (2 bytes per entry)
                            // Number of entries = max CID + 1
                            int numEntries = data.Length / 2;
                            int maxFromGIDMap = numEntries - 1;
                            if (maxFromGIDMap > maxCID)
                            {
                                maxCID = maxFromGIDMap;
                                _logger.LogDebug($"[FONT-CIDSET] Max CID from CIDToGIDMap: {maxCID}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[FONT-CIDSET] Error reading CIDToGIDMap: {ex.Message}");
                    }
                }

                // 2. Check W (widths) array
                int maxFromW = GetMaxCIDFromWidths(cidFont);
                if (maxFromW > maxCID)
                {
                    maxCID = maxFromW;
                    _logger.LogDebug($"[FONT-CIDSET] Max CID from W array: {maxCID}");
                }

                return maxCID;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FONT-CIDSET] Error getting max CID from metadata: {ex.Message}");
                return maxCID;
            }
        }

        /// <summary>
        /// Generate CIDSet byte array marking CIDs from minCID to maxCID as present
        /// Uses MSB-first bit ordering as per PDF spec
        /// </summary>
        private byte[] GenerateCIDSetForRange(int minCID, int maxCID)
        {
            // Calculate number of bytes needed
            int numBits = maxCID + 1;
            int numBytes = (numBits + 7) / 8;

            byte[] cidSetBytes = new byte[numBytes];

            // Set bits for each CID in range (MSB-first ordering)
            for (int cid = minCID; cid <= maxCID; cid++)
            {
                int byteIndex = cid / 8;
                int bitIndex = 7 - (cid % 8); // MSB is bit 7, LSB is bit 0

                if (byteIndex < cidSetBytes.Length)
                {
                    cidSetBytes[byteIndex] |= (byte)(1 << bitIndex);
                }
            }

            return cidSetBytes;
        }

        /// <summary>
        /// Read 16-bit unsigned integer in big-endian format
        /// </summary>
        private int ReadUInt16BigEndian(byte[] data, int offset)
        {
            if (offset + 2 > data.Length)
                return 0;

            return (data[offset] << 8) | data[offset + 1];
        }

        /// <summary>
        /// Read 32-bit signed integer in big-endian format
        /// </summary>
        private int ReadInt32BigEndian(byte[] data, int offset)
        {
            if (offset + 4 > data.Length)
                return 0;

            return (data[offset] << 24) | (data[offset + 1] << 16) |
                   (data[offset + 2] << 8) | data[offset + 3];
        }

        /// <summary>
        /// Read 32-bit unsigned integer in big-endian format
        /// </summary>
        private int ReadUInt32BigEndian(byte[] data, int offset)
        {
            return ReadInt32BigEndian(data, offset);
        }

        /// <summary>
        /// Extract maximum CID from the W (widths) array
        /// </summary>
        private int GetMaxCIDFromWidths(PdfDictionary cidFont)
        {
            try
            {
                var wArray = cidFont.GetAsArray(PdfName.W);
                if (wArray == null || wArray.Size() == 0)
                    return -1;

                int maxCID = 0;

                // W array format: [c_first c_last width ...] or [c [w1 w2 ...]]
                // We need to find the highest CID referenced
                for (int i = 0; i < wArray.Size(); i++)
                {
                    var obj = wArray.Get(i);
                    if (obj is PdfNumber number)
                    {
                        int cid = number.IntValue();
                        if (cid > maxCID)
                            maxCID = cid;
                    }
                }

                return maxCID;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FONT-CIDSET] Error parsing W array: {ex.Message}");
                return -1;
            }
        }
    }
}
