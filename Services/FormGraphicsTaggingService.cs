using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using iText.Kernel.Geom;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf.Annot;
using Microsoft.Extensions.Logging;

namespace AccessForm.Services
{
    /// <summary>
    /// Service to fix path object tagging issues for form field graphics,
    /// particularly radio buttons and checkboxes that have untagged graphical elements.
    /// </summary>
    public class FormGraphicsTaggingService
    {
        private readonly ILogger<FormGraphicsTaggingService> _logger;

        public FormGraphicsTaggingService(ILogger<FormGraphicsTaggingService> logger)
        {
            _logger = logger;
        }

        public class TaggingResult
        {
            public bool Success { get; set; }
            public int PathObjectsFound { get; set; }
            public int PathObjectsTagged { get; set; }
            public int ObjectReferencesFixed { get; set; }
            public byte[] FixedPdf { get; set; }
            public string ErrorMessage { get; set; }
        }

        public async Task<TaggingResult> FixFormGraphicsTaggingAsync(byte[] pdfBytes)
        {
            var result = new TaggingResult { Success = true };

            try
            {
                using var inputStream = new MemoryStream(pdfBytes);
                using var outputStream = new MemoryStream();

                using (var reader = new PdfReader(inputStream))
                using (var writer = new PdfWriter(outputStream))
                using (var pdfDoc = new PdfDocument(reader, writer))
                {

                    // Ensure document is tagged
                    if (!pdfDoc.IsTagged())
                    {
                        pdfDoc.SetTagged();
                    }

                    // Get the structure tree root
                    var structTreeRoot = pdfDoc.GetStructTreeRoot();
                    if (structTreeRoot == null)
                    {
                        _logger.LogWarning("No structure tree root found, creating one");
                        structTreeRoot = pdfDoc.GetStructTreeRoot();
                    }

                    // Process each page
                    for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
                    {
                        var page = pdfDoc.GetPage(pageNum);

                        // Find form fields on this page
                        var formFields = GetFormFieldsOnPage(pdfDoc, pageNum);

                        if (formFields.Count > 0)
                        {
                            _logger.LogInformation($"Found {formFields.Count} form fields on page {pageNum}");

                            // Count radio buttons and checkboxes specifically
                            var radioCheckCount = formFields.Count(f => f.IsRadioButton || f.IsCheckBox);
                            if (radioCheckCount > 0)
                            {
                                _logger.LogInformation($"  Including {radioCheckCount} radio buttons/checkboxes");
                            }

                            // Find untagged path objects near form fields (focusing on radio/checkbox)
                            var untaggedPaths = FindUntaggedPathObjects(page, formFields);
                            result.PathObjectsFound += untaggedPaths.Count;

                            if (untaggedPaths.Count > 0)
                            {
                                _logger.LogInformation($"Found {untaggedPaths.Count} untagged path objects on page {pageNum}");

                                // Tag the path objects
                                foreach (var pathInfo in untaggedPaths)
                                {
                                    string fieldType = pathInfo.AssociatedField.IsRadioButton ? "radio" :
                                                      pathInfo.AssociatedField.IsCheckBox ? "checkbox" : "other";
                                    _logger.LogInformation($"Tagging path for {fieldType} field: {pathInfo.AssociatedField.FieldName}");

                                    if (TagPathObject(pdfDoc, page, pathInfo, structTreeRoot))
                                    {
                                        result.PathObjectsTagged++;
                                    }
                                }
                            }
                        }

                        // Fix object references for form fields
                        result.ObjectReferencesFixed += FixObjectReferences(pdfDoc, page, formFields);
                    }

                    // Fix any orphaned form field references
                    FixOrphanedReferences(pdfDoc);

                }

                result.FixedPdf = outputStream.ToArray();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fixing form graphics tagging");
                return new TaggingResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    FixedPdf = pdfBytes // Return original on error
                };
            }
        }

        private List<FormFieldInfo> GetFormFieldsOnPage(PdfDocument pdfDoc, int pageNum)
        {
            var fields = new List<FormFieldInfo>();
            var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);

            if (acroForm == null)
            {
                return fields;
            }

            var page = pdfDoc.GetPage(pageNum);
            var pageRect = page.GetPageSize();

            foreach (var fieldEntry in acroForm.GetAllFormFields())
            {
                var field = fieldEntry.Value;
                var widgets = field.GetWidgets();

                foreach (var widget in widgets)
                {
                    var widgetPage = widget.GetPage();
                    if (widgetPage != null && widgetPage.Equals(page))
                    {
                        var rect = widget.GetRectangle();
                        if (rect != null)
                        {
                            var fieldInfo = new FormFieldInfo
                            {
                                FieldName = fieldEntry.Key,
                                Field = field,
                                Widget = widget,
                                Bounds = rect.ToRectangle(),
                                IsRadioButton = field is PdfButtonFormField button && button.IsRadio(),
                                IsCheckBox = field is PdfButtonFormField checkButton && !checkButton.IsRadio() && !checkButton.IsPushButton()
                            };

                            fields.Add(fieldInfo);
                        }
                    }
                }
            }

            return fields;
        }

        private List<PathObjectInfo> FindUntaggedPathObjects(PdfPage page, List<FormFieldInfo> formFields)
        {
            var untaggedPaths = new List<PathObjectInfo>();
            var contentStream = page.GetContentBytes();

            if (contentStream == null || contentStream.Length == 0)
            {
                return untaggedPaths;
            }

            // Parse the page content to find path objects
            var listener = new PathExtractionListener(formFields);
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);

            untaggedPaths = listener.GetUntaggedPaths();
            return untaggedPaths;
        }

        private bool TagPathObject(PdfDocument pdfDoc, PdfPage page, PathObjectInfo pathInfo, PdfStructTreeRoot structTreeRoot)
        {
            try
            {
                // Create or find the Form structure element for this field
                var formElem = FindOrCreateFormElement(structTreeRoot, pathInfo.AssociatedField);

                if (formElem != null)
                {
                    // Create a marked content reference for the path
                    var tagReference = new TagReference(formElem, structTreeRoot.GetDocument());
                    tagReference.AddProperty(PdfName.Type, PdfName.Form);

                    // Associate the path with the form element
                    if (pathInfo.AssociatedField.IsRadioButton)
                    {
                        tagReference.AddProperty(new PdfName("Subtype"), new PdfName("RadioButton"));
                    }
                    else if (pathInfo.AssociatedField.IsCheckBox)
                    {
                        tagReference.AddProperty(new PdfName("Subtype"), new PdfName("CheckBox"));
                    }

                    _logger.LogInformation($"Tagged path object for field: {pathInfo.AssociatedField.FieldName}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not tag path object: {ex.Message}");
            }

            return false;
        }

        private PdfStructElem FindOrCreateFormElement(PdfStructTreeRoot root, FormFieldInfo fieldInfo)
        {
            // Look for existing Form element for this field
            var kids = root.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    if (kid is PdfStructElem elem)
                    {
                        var formElem = FindFormElementByField(elem, fieldInfo.FieldName);
                        if (formElem != null)
                        {
                            return formElem;
                        }
                    }
                }
            }

            // Create new Form element
            var newFormElem = new PdfStructElem(root.GetDocument(), PdfName.Form);
            newFormElem.Put(PdfName.Alt, new PdfString(fieldInfo.FieldName));
            root.AddKid(newFormElem);

            return newFormElem;
        }

        private PdfStructElem FindFormElementByField(PdfStructElem elem, string fieldName)
        {
            // Check if this element is associated with the field
            var alt = elem.GetAlt();
            if (alt != null && alt.GetValue() == fieldName)
            {
                return elem;
            }

            // Check children
            var kids = elem.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    if (kid is PdfStructElem childElem)
                    {
                        var found = FindFormElementByField(childElem, fieldName);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }

            return null;
        }

        private int FixObjectReferences(PdfDocument pdfDoc, PdfPage page, List<FormFieldInfo> formFields)
        {
            int fixedCount = 0;

            foreach (var fieldInfo in formFields)
            {
                try
                {
                    // Check if the widget has proper appearance streams
                    var widget = fieldInfo.Widget;
                    var ap = widget.GetAppearanceDictionary();

                    if (ap == null || ap.IsEmpty())
                    {
                        _logger.LogInformation($"Creating appearance dictionary for field: {fieldInfo.FieldName}");

                        // Create appearance streams for the field
                        if (fieldInfo.Field is PdfButtonFormField buttonField)
                        {
                            CreateButtonAppearance(buttonField, widget);
                            fixedCount++;
                        }
                        else if (fieldInfo.Field is PdfTextFormField textField)
                        {
                            CreateTextFieldAppearance(textField, widget);
                            fixedCount++;
                        }
                    }

                    // Ensure proper object references
                    if (widget.GetPdfObject().IsIndirect())
                    {
                        var objRef = widget.GetPdfObject().GetIndirectReference();
                        if (objRef != null && objRef.GetObjNumber() > 0)
                        {
                            // Valid reference exists
                            continue;
                        }
                    }

                    // Make the widget indirect if it isn't
                    widget.MakeIndirect(pdfDoc);
                    fixedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not fix references for field {fieldInfo.FieldName}: {ex.Message}");
                }
            }

            return fixedCount;
        }

        private void CreateButtonAppearance(PdfButtonFormField field, PdfAnnotation widget)
        {
            // Ensure the widget has an appearance dictionary
            var ap = widget.GetAppearanceDictionary();
            if (ap == null)
            {
                ap = new PdfDictionary();
                widget.Put(PdfName.AP, ap);
            }
        }

        private void CreateTextFieldAppearance(PdfTextFormField field, PdfAnnotation widget)
        {
            // Ensure the widget has an appearance dictionary
            var ap = widget.GetAppearanceDictionary();
            if (ap == null)
            {
                ap = new PdfDictionary();
                widget.Put(PdfName.AP, ap);
            }
        }

        private void FixOrphanedReferences(PdfDocument pdfDoc)
        {
            try
            {
                // Get all indirect objects
                for (int i = 1; i <= pdfDoc.GetNumberOfPdfObjects(); i++)
                {
                    var pdfObject = pdfDoc.GetPdfObject(i);
                    if (pdfObject != null && pdfObject.IsDictionary())
                    {
                        var dict = (PdfDictionary)pdfObject;

                        // Check if it's a form field annotation
                        var type = dict.GetAsName(PdfName.Type);
                        var subtype = dict.GetAsName(PdfName.Subtype);

                        if (type != null && type.Equals(PdfName.Annot) && subtype != null && subtype.Equals(PdfName.Widget))
                        {
                            // Ensure it has a parent reference
                            var parent = dict.Get(PdfName.Parent);
                            if (parent == null || parent.IsNull())
                            {
                                _logger.LogWarning($"Found orphaned widget annotation at object {i}");
                                // Could attempt to link to appropriate form field here
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error checking for orphaned references: {ex.Message}");
            }
        }

        // Helper classes
        private class FormFieldInfo
        {
            public string FieldName { get; set; }
            public PdfFormField Field { get; set; }
            public PdfAnnotation Widget { get; set; }
            public Rectangle Bounds { get; set; }
            public bool IsRadioButton { get; set; }
            public bool IsCheckBox { get; set; }
        }

        private class PathObjectInfo
        {
            public Rectangle Bounds { get; set; }
            public FormFieldInfo AssociatedField { get; set; }
            public byte[] PathData { get; set; }
        }

        private class PathExtractionListener : IEventListener
        {
            private readonly List<FormFieldInfo> _formFields;
            private readonly List<PathObjectInfo> _untaggedPaths = new();
            private const float PROXIMITY_THRESHOLD = 20f; // Points

            public PathExtractionListener(List<FormFieldInfo> formFields)
            {
                _formFields = formFields;
            }

            public void EventOccurred(IEventData data, EventType type)
            {
                if (type == EventType.RENDER_PATH)
                {
                    var pathData = (PathRenderInfo)data;

                    // Check if this path is near any form field
                    var path = pathData.GetPath();
                    var ctm = pathData.GetCtm();

                    // Get approximate bounds of the path
                    var pathBounds = GetPathBounds(path, ctm);

                    if (pathBounds != null)
                    {
                        // Check if it's near any form field - prioritize radio/checkbox
                        FormFieldInfo bestMatch = null;
                        float bestDistance = float.MaxValue;

                        foreach (var field in _formFields)
                        {
                            if (IsNearField(pathBounds, field.Bounds, PROXIMITY_THRESHOLD))
                            {
                                // Calculate distance
                                float distance = (float)Math.Sqrt(
                                    Math.Pow(pathBounds.GetX() - field.Bounds.GetX(), 2) +
                                    Math.Pow(pathBounds.GetY() - field.Bounds.GetY(), 2));

                                // Prioritize radio buttons and checkboxes for small circular paths
                                bool isSmallCircularPath = pathBounds.GetWidth() < 25 && pathBounds.GetHeight() < 25 &&
                                                          Math.Abs(pathBounds.GetWidth() - pathBounds.GetHeight()) < 2;

                                if (isSmallCircularPath && (field.IsRadioButton || field.IsCheckBox))
                                {
                                    // Strong preference for radio/checkbox if path is small and circular
                                    distance *= 0.1f;
                                }

                                if (distance < bestDistance)
                                {
                                    bestDistance = distance;
                                    bestMatch = field;
                                }
                            }
                        }

                        if (bestMatch != null)
                        {
                            // Check if this path is already tagged
                            if (!IsPathTagged(pathData))
                            {
                                _untaggedPaths.Add(new PathObjectInfo
                                {
                                    Bounds = pathBounds,
                                    AssociatedField = bestMatch
                                });
                            }
                        }
                    }
                }
            }

            public ICollection<EventType> GetSupportedEvents()
            {
                return new HashSet<EventType> { EventType.RENDER_PATH };
            }

            public List<PathObjectInfo> GetUntaggedPaths()
            {
                return _untaggedPaths;
            }

            private Rectangle GetPathBounds(iText.Kernel.Geom.Path path, Matrix ctm)
            {
                try
                {
                    float minX = float.MaxValue, minY = float.MaxValue;
                    float maxX = float.MinValue, maxY = float.MinValue;

                    foreach (var subpath in path.GetSubpaths())
                    {
                        foreach (var segment in subpath.GetSegments())
                        {
                            var points = segment.GetBasePoints();
                            foreach (var point in points)
                            {
                                var transformed = new Vector((float)point.x, (float)point.y, 1).Cross(ctm);
                                minX = Math.Min(minX, (float)transformed.Get(0));
                                minY = Math.Min(minY, (float)transformed.Get(1));
                                maxX = Math.Max(maxX, (float)transformed.Get(0));
                                maxY = Math.Max(maxY, (float)transformed.Get(1));
                            }
                        }
                    }

                    if (minX < float.MaxValue)
                    {
                        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
                    }
                }
                catch
                {
                    // Ignore paths we can't process
                }

                return null;
            }

            private bool IsNearField(Rectangle pathBounds, Rectangle fieldBounds, float threshold)
            {
                // Check if path is within threshold distance of field
                // For small paths (likely radio/checkbox graphics), use tighter bounds
                bool isSmallPath = pathBounds.GetWidth() < 25 && pathBounds.GetHeight() < 25;
                float actualThreshold = isSmallPath ? threshold / 2 : threshold;

                return Math.Abs(pathBounds.GetX() - fieldBounds.GetX()) < actualThreshold &&
                       Math.Abs(pathBounds.GetY() - fieldBounds.GetY()) < actualThreshold;
            }

            private bool IsPathTagged(PathRenderInfo pathData)
            {
                // Check if the path rendering operation is inside marked content
                var mcid = pathData.GetMcid();
                return mcid >= 0; // If MCID is set, it's tagged
            }
        }

        private class TagReference
        {
            private readonly PdfStructElem _elem;
            private readonly PdfDocument _document;
            private readonly Dictionary<PdfName, PdfObject> _properties = new();

            public TagReference(PdfStructElem elem, PdfDocument document)
            {
                _elem = elem;
                _document = document;
            }

            public void AddProperty(PdfName key, PdfObject value)
            {
                _properties[key] = value;
            }
        }
    }
}