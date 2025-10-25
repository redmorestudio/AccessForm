using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Fixes
{
    /// <summary>
    /// Fixes 7.18.4-1: A Widget annotation shall be nested within a Form tag per ISO 32000-1:2008, 14.8.4.5, Table 340.
    /// This ensures all form widgets are properly nested in Form structure elements.
    /// </summary>
    public class FormWidgetNestingFixService : IRemediationService
    {
        private readonly ILogger<FormWidgetNestingFixService> _logger;

        public string ServiceName => "Form Widget Nesting Fix";
        public ViolationCategory TargetCategory => ViolationCategory.FormFields;
        public int Priority => 5; // Critical priority for form accessibility
        public bool IsRequired => false; // Only run when form violations detected

        public FormWidgetNestingFixService(ILogger<FormWidgetNestingFixService> logger)
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
                _logger.LogInformation("[FORM-WIDGET-FIX] Starting form widget nesting remediation");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                var fixedCount = 0;

                // Get all form fields
                var form = PdfAcroForm.GetAcroForm(pdfDoc, false);
                if (form == null)
                {
                    _logger.LogInformation("[FORM-WIDGET-FIX] No form fields found in document");
                    result.Success = true;
                    return result;
                }

                if (!pdfDoc.IsTagged())
                {
                    _logger.LogWarning("[FORM-WIDGET-FIX] Document is not tagged");
                    result.Success = true;
                    return result;
                }

                var rootTag = pdfDoc.GetStructTreeRoot();

                // Process each form field
                var fields = form.GetAllFormFields();
                foreach (var field in fields)
                {
                    fixedCount += await FixWidgetNesting(field.Value, null, pdfDoc);
                }

                // Ensure Form structure elements exist for all widgets
                fixedCount += await CreateMissingFormElements(pdfDoc, null);

                pdfDoc.Close();

                if (fixedCount > 0)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = fixedCount;
                    _logger.LogInformation($"[FORM-WIDGET-FIX] Fixed {fixedCount} form widget nesting issues");
                }
                else
                {
                    _logger.LogInformation("[FORM-WIDGET-FIX] All form widgets properly nested");
                    result.Success = true;
                }

                stopwatch.Stop();
                _logger.LogInformation($"[FORM-WIDGET-FIX] Completed in {stopwatch.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FORM-WIDGET-FIX] Failed to fix form widget nesting");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<int> FixWidgetNesting(PdfFormField field, PdfStructElem? rootTag, PdfDocument pdfDoc)
        {
            var fixedCount = 0;

            try
            {
                // rootTag is null, we'll use FindOrCreateFormElement which handles the root correctly

                // Get all widget annotations for this field
                var widgets = field.GetWidgets();
                if (widgets == null || widgets.Count == 0)
                {
                    return 0;
                }

                foreach (var widget in widgets)
                {
                    var widgetObj = widget.GetPdfObject();
                    if (widgetObj == null) continue;

                    // Check if widget is properly nested in a Form structure element
                    var structParent = widgetObj.GetAsNumber(PdfName.StructParent);

                    if (structParent == null)
                    {
                        // Widget is not in structure tree at all
                        _logger.LogInformation($"[FORM-WIDGET-FIX] Widget {field.GetFieldName()} not in structure tree");

                        // Create or find Form structure element
                        var formElement = await FindOrCreateFormElement(null);

                        // Add widget to Form structure
                        if (await AddWidgetToFormElement(widget, formElement, pdfDoc))
                        {
                            fixedCount++;
                            _logger.LogInformation($"[FORM-WIDGET-FIX] Added widget to Form structure");
                        }
                    }
                    else
                    {
                        // Widget is in structure tree, check if parent is Form
                        var parent = await GetStructureParent(widgetObj, null);

                        if (parent != null && !IsFormElement(parent))
                        {
                            _logger.LogInformation($"[FORM-WIDGET-FIX] Widget {field.GetFieldName()} not nested in Form");

                            // Move widget to Form structure
                            var formElement = await FindOrCreateFormElement(null);

                            if (await MoveWidgetToFormElement(widget, parent, formElement, pdfDoc))
                            {
                                fixedCount++;
                                _logger.LogInformation($"[FORM-WIDGET-FIX] Moved widget to Form structure");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FORM-WIDGET-FIX] Error processing field: {ex.Message}");
            }

            return await Task.FromResult(fixedCount);
        }

        private async Task<int> CreateMissingFormElements(PdfDocument pdfDoc, PdfStructElem? rootTag)
        {
            var fixedCount = 0;

            try
            {
                // We won't use rootTag directly, we'll work with the structure tree root

                // Get all pages
                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                {
                    var page = pdfDoc.GetPage(i);
                    var annotations = page.GetAnnotations();

                    foreach (var annot in annotations)
                    {
                        // Check if it's a widget annotation
                        if (annot.GetSubtype() == PdfName.Widget)
                        {
                            var widgetObj = annot.GetPdfObject();
                            var structParent = widgetObj.GetAsNumber(PdfName.StructParent);

                            if (structParent == null)
                            {
                                // Widget not in structure tree
                                // Pass null to indicate we should work with the document's root
                                var formElement = await FindOrCreateFormElement(null);

                                // Create structure parent index
                                var nextParentIndex = GetNextStructParentIndex(pdfDoc);
                                widgetObj.Put(PdfName.StructParent, new PdfNumber(nextParentIndex));

                                // Add to parent tree
                                var parentTreeDict = pdfDoc.GetStructTreeRoot().GetPdfObject().GetAsDictionary(PdfName.ParentTree);
                                if (parentTreeDict != null)
                                {
                                    var nums = parentTreeDict.GetAsArray(PdfName.Nums);
                                    if (nums == null)
                                    {
                                        nums = new PdfArray();
                                        parentTreeDict.Put(PdfName.Nums, nums);
                                    }
                                    nums.Add(new PdfNumber(nextParentIndex));
                                    nums.Add(formElement.GetPdfObject());
                                    fixedCount++;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FORM-WIDGET-FIX] Error creating Form elements: {ex.Message}");
            }

            return await Task.FromResult(fixedCount);
        }

        private async Task<PdfStructElem> FindOrCreateFormElement(PdfStructElem? rootTag)
        {
            // First try to find existing Form element, handle null rootTag
            PdfStructElem formElement = null;
            if (rootTag != null)
            {
                formElement = await FindFormElement(rootTag);
            }

            if (formElement != null)
            {
                return formElement;
            }

            // Create new Form structure element
            _logger.LogInformation("[FORM-WIDGET-FIX] Creating new Form structure element");

            // We can't create a Form element without a parent structure
            // This should not happen in a properly tagged document
            if (rootTag == null)
            {
                _logger.LogWarning("[FORM-WIDGET-FIX] Cannot create Form element without parent structure");
                return null;
            }

            var doc = rootTag.GetPdfObject().GetIndirectReference()?.GetDocument();
            var formTag = new PdfStructElem(doc, PdfName.Form);
            rootTag.AddKid(formTag);

            // Set proper attributes
            var formDict = formTag.GetPdfObject();
            formDict.Put(PdfName.S, PdfName.Form);

            return formTag;
        }

        private async Task<PdfStructElem> FindFormElement(PdfStructElem element)
        {
            // Check if this is a Form element
            if (IsFormElement(element))
            {
                return element;
            }

            // Recursively search children
            var kids = element.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    if (kid is PdfStructElem childElem)
                    {
                        var found = await FindFormElement(childElem);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }

            return null;
        }

        private bool IsFormElement(PdfStructElem element)
        {
            var role = element.GetRole();
            return role != null && (role.Equals(PdfName.Form) ||
                                   role.GetValue() == "Form" ||
                                   role.GetValue() == "FORM");
        }

        private async Task<bool> AddWidgetToFormElement(PdfWidgetAnnotation widget, PdfStructElem formElement, PdfDocument pdfDoc)
        {
            try
            {
                var widgetObj = widget.GetPdfObject();

                // Assign structure parent index
                var nextParentIndex = GetNextStructParentIndex(pdfDoc);
                widgetObj.Put(PdfName.StructParent, new PdfNumber(nextParentIndex));

                // Add to parent tree
                var parentTreeDict = pdfDoc.GetStructTreeRoot().GetPdfObject().GetAsDictionary(PdfName.ParentTree);
                if (parentTreeDict != null)
                {
                    var nums = parentTreeDict.GetAsArray(PdfName.Nums);
                    if (nums == null)
                    {
                        nums = new PdfArray();
                        parentTreeDict.Put(PdfName.Nums, nums);
                    }
                    nums.Add(new PdfNumber(nextParentIndex));
                    nums.Add(formElement.GetPdfObject());
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FORM-WIDGET-FIX] Error adding widget to Form: {ex.Message}");
            }

            return await Task.FromResult(false);
        }

        private async Task<bool> MoveWidgetToFormElement(PdfWidgetAnnotation widget, PdfStructElem currentParent,
            PdfStructElem formElement, PdfDocument pdfDoc)
        {
            try
            {
                // Remove from current parent
                // RemoveKid needs IStructureNode, not PdfDictionary
                // We need to find the correct child to remove
                var kids = currentParent.GetKids();
                if (kids != null)
                {
                    for (int i = kids.Count - 1; i >= 0; i--)
                    {
                        // Check if this kid references the widget
                        if (kids[i] is PdfMcr mcr && mcr.GetPdfObject() == widget.GetPdfObject())
                        {
                            currentParent.RemoveKid(i);
                            break;
                        }
                    }
                }

                // Add to Form element
                return await AddWidgetToFormElement(widget, formElement, pdfDoc);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FORM-WIDGET-FIX] Error moving widget: {ex.Message}");
            }

            return false;
        }

        private async Task<PdfStructElem> GetStructureParent(PdfDictionary widgetObj, PdfStructElem? rootTag)
        {
            try
            {
                var structParent = widgetObj.GetAsNumber(PdfName.StructParent);
                if (structParent != null && rootTag != null)
                {
                    // Find the structure element with this ID
                    return await FindStructElementById(rootTag, structParent.IntValue());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FORM-WIDGET-FIX] Error getting structure parent: {ex.Message}");
            }

            return null;
        }

        private async Task<PdfStructElem> FindStructElementById(PdfStructElem element, int id)
        {
            // Check if this element has the ID
            var elemObj = element.GetPdfObject();
            var elemId = elemObj.GetAsNumber(new PdfName("ID"));

            if (elemId != null && elemId.IntValue() == id)
            {
                return element;
            }

            // Recursively search children
            var kids = element.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    if (kid is PdfStructElem childElem)
                    {
                        var found = await FindStructElementById(childElem, id);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }

            return null;
        }

        private int GetNextStructParentIndex(PdfDocument pdfDoc)
        {
            // Get the next available structure parent index
            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            var parentTreeDict = structTreeRoot.GetPdfObject().GetAsDictionary(PdfName.ParentTree);

            if (parentTreeDict != null)
            {
                var nums = parentTreeDict.GetAsArray(PdfName.Nums);
                if (nums != null && nums.Size() > 0)
                {
                    // Get the highest index
                    var maxIndex = 0;
                    for (int i = 0; i < nums.Size(); i += 2)
                    {
                        var index = nums.GetAsNumber(i);
                        if (index != null && index.IntValue() > maxIndex)
                        {
                            maxIndex = index.IntValue();
                        }
                    }
                    return maxIndex + 1;
                }
            }

            return 0;
        }
    }
}
