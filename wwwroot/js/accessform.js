// Drag and drop file handling for AccessForm
window.accessForm = {
    // Initialize drag and drop
    initializeDragDrop: function (dotnetHelper, dropZoneId) {
        // Store the dotNetHelper globally so browse button can use it
        window.accessForm.dotNetHelper = dotnetHelper;
        
        const dropZone = document.getElementById(dropZoneId);
        if (!dropZone) return;

        // Prevent default drag behaviors
        ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
            dropZone.addEventListener(eventName, preventDefaults, false);
            document.body.addEventListener(eventName, preventDefaults, false);
        });

        function preventDefaults(e) {
            e.preventDefault();
            e.stopPropagation();
        }

        // Highlight drop zone when dragging over it
        ['dragenter', 'dragover'].forEach(eventName => {
            dropZone.addEventListener(eventName, () => {
                dotnetHelper.invokeMethodAsync('OnDragEnter');
            }, false);
        });

        ['dragleave', 'drop'].forEach(eventName => {
            dropZone.addEventListener(eventName, () => {
                dotnetHelper.invokeMethodAsync('OnDragLeave');
            }, false);
        });

        // Handle dropped files
        dropZone.addEventListener('drop', async (e) => {
            console.log('Drop event triggered');
            const dt = e.dataTransfer;
            const files = dt.files;
            console.log('Files dropped:', files.length);

            if (files.length > 0) {
                const file = files[0];
                console.log('Processing file:', file.name, 'Type:', file.type, 'Size:', file.size);
                
                // Validate file type
                const fileName = file.name.toLowerCase();
                
                if (!fileName.endsWith('.docx') && !fileName.endsWith('.pdf')) {
                    console.log('Invalid file type:', fileName);
                    alert('Please drop a Word (.docx) or PDF (.pdf) file');
                    return;
                }

                console.log('File validation passed');
                
                // Send file directly without base64 conversion
                try {
                    console.log('Calling uploadOriginalFile with original file');
                    
                    // Show processing indicator
                    const overlay = document.createElement('div');
                    overlay.id = 'processing-overlay';
                    overlay.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.5);display:flex;align-items:center;justify-content:center;z-index:9999';
                    
                    // Check if AI mode is enabled
                    const isAiMode = window.accessForm && window.accessForm.isAiModeEnabled && window.accessForm.isAiModeEnabled();
                    let seconds = 0;
                    let timerInterval = null;
                    
                    if (isAiMode) {
                        overlay.innerHTML = `
                            <div style="background:white;padding:30px;border-radius:10px;text-align:center;min-width:400px">
                                <div class="spinner-border text-primary" style="width:3rem;height:3rem" role="status">
                                    <span class="visually-hidden">Processing...</span>
                                </div>
                                <h4 class="mt-3">Processing Your Document</h4>
                                <div class="alert alert-info mt-3">
                                    <p class="mb-2">🤖 <strong>Anthropic Claude AI Analysis</strong></p>
                                    <p class="mb-1">Analyzing document structure and identifying form fields...</p>
                                    <p class="text-muted small">This can take up to 2 minutes for complex documents</p>
                                    <div class="fs-1 fw-bold text-primary mt-3" id="timer-display">
                                        ⏱️ 0:00
                                    </div>
                                </div>
                            </div>
                        `;
                        
                        // Start timer
                        timerInterval = setInterval(() => {
                            seconds++;
                            const minutes = Math.floor(seconds / 60);
                            const secs = seconds % 60;
                            const display = document.getElementById('timer-display');
                            if (display) {
                                display.textContent = `⏱️ ${minutes}:${secs.toString().padStart(2, '0')}`;
                            }
                        }, 1000);
                    } else {
                        overlay.innerHTML = `
                            <div style="background:white;padding:30px;border-radius:10px;text-align:center">
                                <div class="spinner-border text-primary" style="width:3rem;height:3rem" role="status">
                                    <span class="visually-hidden">Processing...</span>
                                </div>
                                <h4 class="mt-3">Processing Your Document</h4>
                                <p class="text-muted">Please wait...</p>
                            </div>
                        `;
                    }
                    document.body.appendChild(overlay);
                    
                    const isWord = fileName.endsWith('.docx');
                    // Determine endpoint based on AI mode and file type
                    let endpoint;
                    if (window.accessForm && window.accessForm.isAiModeEnabled && window.accessForm.isAiModeEnabled()) {
                        // Use AI-enhanced endpoint when AI mode is active
                        endpoint = '/api/convert-with-ai';
                    } else {
                        // Use standard endpoints
                        endpoint = isWord ? '/api/convert' : '/api/remediate-pdf';
                    }
                    
                    const result = await window.accessForm.uploadOriginalFile(endpoint, file);
                    
                    if (result) {
                        console.log('Upload successful, calling Blazor to show results');
                        
                        // Log the result to see if it contains debugId
                        console.log('Result from server:', result);
                        try {
                            const parsedResult = JSON.parse(result);
                            if (parsedResult.debugId) {
                                console.log('🎯 Found debugId in response:', parsedResult.debugId);
                                console.log('Debug URL:', `/debug-ai-simple.html?id=${parsedResult.debugId}`);
                            } else {
                                console.log('No debugId in parsed result');
                            }
                        } catch (e) {
                            console.log('Could not parse result as JSON:', e);
                        }
                        
                        const fileData = {
                            name: file.name,
                            size: file.size,
                            type: file.type,
                            lastModified: file.lastModified,
                            data: '' // No base64 data needed
                        };
                        
                        await dotnetHelper.invokeMethodAsync('OnFileProcessedDirectly', fileData, result);
                    } else {
                        alert('File processing failed. Please try again.');
                    }
                } catch (error) {
                    console.error('Error in file processing:', error);
                    alert('Error processing file: ' + error.message);
                }
            }
        }, false);
    },

    // Upload original file directly without base64 conversion
    uploadOriginalFile: async function (endpoint, file) {
        try {
            console.log(`uploadOriginalFile: ${endpoint}, file: ${file.name}`);
            console.log(`uploadOriginalFile: file size: ${file.size}, type: ${file.type}`);
            
            // Create FormData directly from the original file
            const formData = new FormData();
            formData.append('file', file, file.name);
            
            console.log(`uploadOriginalFile: FormData created`);
            
            // Send request
            const response = await fetch(`http://localhost:5008${endpoint}`, {
                method: 'POST',
                body: formData
            });
            
            console.log(`uploadOriginalFile: Response status: ${response.status}`);
            
            if (response.ok) {
                const result = await response.text();
                console.log(`uploadOriginalFile: Success, response length: ${result.length}`);
                return result;
            } else {
                console.error(`uploadOriginalFile: HTTP error ${response.status}`);
                const errorText = await response.text();
                console.error(`uploadOriginalFile: Error response: ${errorText}`);
                return null;
            }
        } catch (error) {
            console.error('uploadOriginalFile: Exception:', error);
            return null;
        }
    },

    // Upload file from base64 data (for Blazor file input)
    uploadFileDirectly: async function (endpoint, fileName, base64Data, contentType) {
        try {
            console.log(`uploadFileDirectly: ${endpoint}, file: ${fileName}`);
            console.log(`uploadFileDirectly: contentType: ${contentType}`);
            
            // Convert base64 to blob
            const byteCharacters = atob(base64Data);
            const byteNumbers = new Array(byteCharacters.length);
            for (let i = 0; i < byteCharacters.length; i++) {
                byteNumbers[i] = byteCharacters.charCodeAt(i);
            }
            const byteArray = new Uint8Array(byteNumbers);
            const blob = new Blob([byteArray], { type: contentType });
            
            // Create FormData with the blob
            const formData = new FormData();
            formData.append('file', blob, fileName);
            
            console.log(`uploadFileDirectly: FormData created with blob size: ${blob.size}`);
            
            // Send request
            const response = await fetch(`http://localhost:5008${endpoint}`, {
                method: 'POST',
                body: formData
            });
            
            console.log(`uploadFileDirectly: Response status: ${response.status}`);
            
            if (response.ok) {
                const result = await response.text();
                console.log(`uploadFileDirectly: Success, response length: ${result.length}`);
                return result;
            } else {
                console.error(`uploadFileDirectly: HTTP error ${response.status}`);
                const errorText = await response.text();
                console.error(`uploadFileDirectly: Error response: ${errorText}`);
                return null;
            }
        } catch (error) {
            console.error('uploadFileDirectly: Exception:', error);
            return null;
        }
    },

    // Trigger file input click
    triggerFileInput: function (inputId) {
        const input = document.getElementById(inputId);
        if (input) {
            input.click();
        }
    },

    // Trigger file input with handler (for browse button to work like drag-and-drop)
    triggerFileInputWithPassport: function (inputId) {
        console.log('Triggering file input with PassportPDF processing');
        const input = document.getElementById(inputId);
        if (input) {
            input.onchange = async function (event) {
                const files = event.target.files;
                if (files && files.length > 0) {
                    // Process with PassportPDF endpoint
                    try {
                        console.log('Using PassportPDF for processing');
                        
                        // Show processing indicator
                        const overlay = document.createElement('div');
                        overlay.id = 'processing-overlay';
                        overlay.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.5);display:flex;align-items:center;justify-content:center;z-index:9999';
                        overlay.innerHTML = `
                            <div style="background:white;padding:30px;border-radius:10px;text-align:center;min-width:400px">
                                <div class="spinner-border text-success" style="width:3rem;height:3rem" role="status">
                                    <span class="visually-hidden">Processing...</span>
                                </div>
                                <h4 class="mt-3">Processing with PassportPDF</h4>
                                <div class="alert alert-success mt-3">
                                    <p class="mb-2">🎆 <strong>PassportPDF + Anthropic Claude</strong></p>
                                    <p class="mb-1">Creating fully PDF/UA compliant document...</p>
                                    <p class="text-muted small">This ensures maximum accessibility compliance</p>
                                </div>
                            </div>
                        `;
                        document.body.appendChild(overlay);
                        
                        const file = files[0];
                        const endpoint = '/api/process-with-passportpdf-auto';
                        
                        const result = await window.accessForm.uploadOriginalFile(endpoint, file);
                        
                        // Remove processing overlay
                        const overlayToRemove = document.getElementById('processing-overlay');
                        if (overlayToRemove) {
                            overlayToRemove.remove();
                        }
                        
                        if (result) {
                            console.log('PassportPDF processing successful');
                            const fileData = {
                                name: file.name,
                                size: file.size,
                                type: file.type,
                                lastModified: file.lastModified,
                                data: ''
                            };
                            
                            if (window.accessForm.dotNetHelper) {
                                await window.accessForm.dotNetHelper.invokeMethodAsync('OnFileProcessedDirectly', fileData, result);
                            } else {
                                console.error('Blazor helper not found');
                                alert('Error: Unable to process file. Please refresh the page.');
                            }
                        } else {
                            alert('PassportPDF processing failed. Please try again.');
                        }
                    } catch (error) {
                        console.error('Error processing with PassportPDF:', error);
                        
                        // Remove processing overlay on error
                        const overlayToRemove = document.getElementById('processing-overlay');
                        if (overlayToRemove) {
                            overlayToRemove.remove();
                        }
                        
                        alert('Error processing file: ' + error.message);
                    }
                }
            };
            input.click();
        }
    },

    triggerFileInputWithHandler: function (inputId) {
        const input = document.getElementById(inputId);
        if (input) {
            // Remove any existing change listener
            input.removeEventListener('change', window.accessForm._handleFileInput);
            
            // Add change listener that processes like drag-and-drop
            window.accessForm._handleFileInput = async function(event) {
                const files = event.target.files;
                if (files.length > 0) {
                    const file = files[0];
                    console.log('Browse button file selected:', file.name, 'Type:', file.type, 'Size:', file.size);
                    
                    // Validate file type
                    const fileName = file.name.toLowerCase();
                    
                    if (!fileName.endsWith('.docx') && !fileName.endsWith('.pdf')) {
                        console.log('Invalid file type:', fileName);
                        alert('Please select a Word (.docx) or PDF (.pdf) file');
                        return;
                    }

                    console.log('File validation passed');
                    
                    // Process exactly like drag-and-drop - use uploadOriginalFile directly
                    try {
                        console.log('Calling uploadOriginalFile with original file (same as drag-drop)');
                        
                        // Show processing indicator
                        const overlay = document.createElement('div');
                        overlay.id = 'processing-overlay';
                        overlay.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.5);display:flex;align-items:center;justify-content:center;z-index:9999';
                        
                        // Check if AI mode is enabled
                        const isAiMode = window.accessForm && window.accessForm.isAiModeEnabled && window.accessForm.isAiModeEnabled();
                        let seconds = 0;
                        let timerInterval = null;
                        
                        if (isAiMode) {
                            overlay.innerHTML = `
                                <div style="background:white;padding:30px;border-radius:10px;text-align:center;min-width:400px">
                                    <div class="spinner-border text-primary" style="width:3rem;height:3rem" role="status">
                                        <span class="visually-hidden">Processing...</span>
                                    </div>
                                    <h4 class="mt-3">Processing Your Document</h4>
                                    <div class="alert alert-info mt-3">
                                        <p class="mb-2">🤖 <strong>Anthropic Claude AI Analysis</strong></p>
                                        <p class="mb-1">Analyzing document structure and identifying form fields...</p>
                                        <p class="text-muted small">This can take up to 2 minutes for complex documents</p>
                                        <div class="fs-1 fw-bold text-primary mt-3" id="timer-display">
                                            ⏱️ 0:00
                                        </div>
                                    </div>
                                </div>
                            `;
                            
                            // Start timer
                            timerInterval = setInterval(() => {
                                seconds++;
                                const minutes = Math.floor(seconds / 60);
                                const secs = seconds % 60;
                                const display = document.getElementById('timer-display');
                                if (display) {
                                    display.textContent = `⏱️ ${minutes}:${secs.toString().padStart(2, '0')}`;
                                }
                            }, 1000);
                        } else {
                            overlay.innerHTML = `
                                <div style="background:white;padding:30px;border-radius:10px;text-align:center">
                                    <div class="spinner-border text-primary" style="width:3rem;height:3rem" role="status">
                                        <span class="visually-hidden">Processing...</span>
                                    </div>
                                    <h4 class="mt-3">Processing Your Document</h4>
                                    <p class="text-muted">Please wait...</p>
                                </div>
                            `;
                        }
                        document.body.appendChild(overlay);
                        
                        const isWord = fileName.endsWith('.docx');
                        // Determine endpoint based on AI mode and file type
                        let endpoint;
                        if (window.accessForm && window.accessForm.isAiModeEnabled && window.accessForm.isAiModeEnabled()) {
                            // Use AI-enhanced endpoint when AI mode is active
                            endpoint = '/api/convert-with-ai';
                        } else {
                            // Use standard endpoints
                            endpoint = isWord ? '/api/convert' : '/api/remediate-pdf';
                        }
                        
                        const result = await window.accessForm.uploadOriginalFile(endpoint, file);
                        
                        // Remove processing overlay
                        if (timerInterval) {
                            clearInterval(timerInterval);
                        }
                        const overlayToRemove = document.getElementById('processing-overlay');
                        if (overlayToRemove) {
                            overlayToRemove.remove();
                        }
                        
                        if (result) {
                            console.log('Upload successful, calling Blazor to show results');
                            const fileData = {
                                name: file.name,
                                size: file.size,
                                type: file.type,
                                lastModified: file.lastModified,
                                data: '' // No base64 data needed for direct file approach
                            };
                            
                            // Call the same function as drag-and-drop
                            if (window.accessForm.dotNetHelper) {
                                await window.accessForm.dotNetHelper.invokeMethodAsync('OnFileProcessedDirectly', fileData, result);
                            } else {
                                console.error('Blazor helper not found');
                                alert('Error: Unable to process file. Please refresh the page.');
                            }
                        } else {
                            alert('File processing failed. Please try again.');
                        }
                    } catch (error) {
                        console.error('Error processing browse button file:', error);
                        
                        // Remove processing overlay on error
                        const overlayToRemove = document.getElementById('processing-overlay');
                        if (overlayToRemove) {
                            overlayToRemove.remove();
                        }
                        
                        alert('Error processing file: ' + error.message);
                    }
                }
            };
            
            input.addEventListener('change', window.accessForm._handleFileInput);
            input.click();
        }
    },

    // Download file from base64
    downloadFile: function (base64Data, fileName) {
        try {
            // Validate that we have base64 data
            if (!base64Data || base64Data.length === 0) {
                throw new Error('No PDF data provided');
            }
            
            // Remove data URL prefix if present
            let pdfData = base64Data;
            if (pdfData.startsWith('data:')) {
                pdfData = pdfData.split(',')[1];
            }
            
            console.log(`Processing base64 data (${pdfData.length} characters)`);
            
            // Convert base64 to blob
            const byteCharacters = atob(pdfData);
            const byteNumbers = new Array(byteCharacters.length);
            for (let i = 0; i < byteCharacters.length; i++) {
                byteNumbers[i] = byteCharacters.charCodeAt(i);
            }
            const byteArray = new Uint8Array(byteNumbers);
            const blob = new Blob([byteArray], { type: 'application/pdf' });

            // Verify the blob has content
            if (blob.size === 0) {
                throw new Error('PDF blob is empty after conversion');
            }

            // Create download link
            const url = window.URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = fileName || 'document.pdf';
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
            
            // Clean up
            setTimeout(() => {
                window.URL.revokeObjectURL(url);
            }, 100);
            
            console.log(`Successfully downloaded: ${fileName} (${blob.size} bytes)`);
            return true;
        } catch (error) {
            console.error('Download failed:', error);
            console.error('Base64 data type:', typeof base64Data);
            console.error('Base64 data sample:', base64Data ? base64Data.toString().substring(0, 100) : 'null');
            alert(`Download failed: ${error.message}`);
            return false;
        }
    },

    // Show file size in readable format
    formatFileSize: function (bytes) {
        if (bytes === 0) return '0 Bytes';
        const k = 1024;
        const sizes = ['Bytes', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
    },

    // Check if browser supports drag and drop
    checkDragDropSupport: function () {
        const div = document.createElement('div');
        return ('draggable' in div) || ('ondragstart' in div && 'ondrop' in div);
    },

    // Check file API support
    checkFileAPISupport: function () {
        return window.File && window.FileReader && window.FileList && window.Blob;
    }
};

// Auto-initialize on page load
document.addEventListener('DOMContentLoaded', function () {
    // Check browser compatibility
    if (!window.accessForm.checkDragDropSupport() || !window.accessForm.checkFileAPISupport()) {
        console.warn('Browser does not fully support drag and drop file uploads');
    }
});
