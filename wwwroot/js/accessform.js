// Drag and drop file handling for AccessForm
window.accessForm = {
    // Track AI mode state
    _aiModeEnabled: false,

    // Set AI mode state
    setAiMode: function(enabled) {
        console.log("Setting AI mode to:", enabled);
        this._aiModeEnabled = enabled;
    },

    // Check if AI mode is enabled
    isAiModeEnabled: function() {
        console.log("Checking AI mode:", this._aiModeEnabled);
        return this._aiModeEnabled;
    },

    // Progress polling removed - no longer used
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
                        // AI Processing stages
                        const aiStages = [
                            { name: 'Uploading document', time: 2 },
                            { name: 'Initial document analysis', time: 3 },
                            { name: 'Claude AI field detection', time: 15 },
                            { name: 'Field validation and optimization', time: 8 },
                            { name: 'Creating interactive form fields', time: 5 },
                            { name: 'Final quality check', time: 2 }
                        ];

                        let currentStage = 0;
                        let stageProgress = 0;
                        const totalTime = aiStages.reduce((sum, s) => sum + s.time, 0);

                        overlay.innerHTML = `
                            <div style="background:white;padding:30px;border-radius:10px;text-align:center;min-width:500px;max-width:600px">
                                <div class="spinner-border text-primary mb-3" style="width:3rem;height:3rem" role="status">
                                    <span class="visually-hidden">Processing...</span>
                                </div>
                                <h4>AI Document Processing</h4>

                                <div class="fs-1 fw-bold text-primary mt-3" id="timer-display">0:00</div>

                                <div class="alert alert-info mt-3 mb-3">
                                    <p class="mb-2">🤖 <strong>Anthropic Claude AI Analysis</strong></p>
                                    <p class="mb-1" id="stage-name">Initializing...</p>
                                    <p class="text-muted small mb-0">Advanced form field detection and optimization</p>
                                </div>

                                <!-- Progress bar disabled - using checkboxes in Blazor UI instead
                                <div class="progress mb-3" style="height: 25px;">
                                    <div id="progress-bar" class="progress-bar progress-bar-striped progress-bar-animated bg-primary"
                                         role="progressbar" style="width: 0%" aria-valuenow="0" aria-valuemin="0" aria-valuemax="100">
                                        <span id="progress-text">0%</span>
                                    </div>
                                </div> -->

                                <div class="small text-muted">
                                    Stage <span id="stage-num">1</span> of ${aiStages.length}
                                </div>
                            </div>
                        `;

                        // Start timer
                        const startTime = Date.now();
                        timerInterval = setInterval(() => {
                            const elapsed = Math.floor((Date.now() - startTime) / 1000);
                            const minutes = Math.floor(elapsed / 60);
                            const seconds = elapsed % 60;
                            const display = document.getElementById('timer-display');
                            if (display) {
                                display.textContent = `${minutes}:${seconds.toString().padStart(2, '0')}`;
                            }
                        }, 100);

                        // Simulate progress through stages
                        let progressInterval = setInterval(() => {
                            if (currentStage < aiStages.length) {
                                stageProgress += 100 / (aiStages[currentStage].time * 10); // Update every 100ms

                                if (stageProgress >= 100) {
                                    currentStage++;
                                    stageProgress = 0;
                                }

                                if (currentStage < aiStages.length) {
                                    // Update stage display
                                    const stageEl = document.getElementById('stage-name');
                                    if (stageEl) stageEl.textContent = aiStages[currentStage].name + '...';

                                    const stageNumEl = document.getElementById('stage-num');
                                    if (stageNumEl) stageNumEl.textContent = (currentStage + 1).toString();

                                    // Calculate overall progress
                                    const completedTime = aiStages.slice(0, currentStage).reduce((sum, s) => sum + s.time, 0);
                                    const currentTime = aiStages[currentStage].time * (stageProgress / 100);
                                    const overallProgress = ((completedTime + currentTime) / totalTime) * 100;

                                    // Progress bar updates disabled - using checkboxes in Blazor UI instead
                                    /*
                                    const progressBar = document.getElementById('progress-bar');
                                    if (progressBar) {
                                        progressBar.style.width = overallProgress + '%';
                                        progressBar.setAttribute('aria-valuenow', overallProgress.toString());
                                    }

                                    const progressText = document.getElementById('progress-text');
                                    if (progressText) {
                                        progressText.textContent = Math.round(overallProgress) + '%';
                                    }
                                    */
                                }
                            }
                        }, 100);

                        // Store progress interval for cleanup
                        window.aiProgressInterval = progressInterval;
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
                    let config = null;
                    if (window.accessForm && window.accessForm.isAiModeEnabled && window.accessForm.isAiModeEnabled()) {
                        // Use configurable endpoint for Word docs in AI mode
                        endpoint = isWord ? '/api/convert-with-config' : '/api/convert-with-ai';
                        // Get configuration from UI if using configurable endpoint
                        if (endpoint === '/api/convert-with-config') {
                            config = {
                                useSyncfusion: document.querySelector('#useSyncfusion')?.checked ?? true,
                                useGoogle: document.querySelector('#useGoogle')?.checked ?? false,
                                useClaudeVision: document.querySelector('#useClaudeVision')?.checked ?? false,
                                useClaudeValidation: document.querySelector('#useClaudeValidation')?.checked ?? false,
                                mode: document.querySelector('input[name="processingMode"]:checked')?.value ?? 'Sequential',
                                debugMode: document.querySelector('#debugMode')?.checked ?? true,
                                showFieldIds: document.querySelector('#showFieldIds')?.checked ?? true,
                                // Accessibility services
                                useAdobeAutotag: document.querySelector('#useAdobeAutotag')?.checked ?? false,
                                useAsposeAutotag: document.querySelector('#useAsposeAutotag')?.checked ?? false,
                                useAsposeFontEmbed: document.querySelector('#useAsposeFontEmbed')?.checked ?? true,
                                usePassportPdf: document.querySelector('#usePassportPdf')?.checked ?? false
                            };
                        }
                    } else {
                        // Use standard endpoints
                        endpoint = isWord ? '/api/convert' : '/api/remediate-pdf';
                    }
                    
                    const result = await window.accessForm.uploadOriginalFile(endpoint, file, config);

                    // Clean up intervals and overlay
                    if (timerInterval) {
                        clearInterval(timerInterval);
                    }
                    if (window.aiProgressInterval) {
                        clearInterval(window.aiProgressInterval);
                        window.aiProgressInterval = null;
                    }
                    const overlayToRemove = document.getElementById('processing-overlay');
                    if (overlayToRemove) {
                        overlayToRemove.remove();
                    }

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

                        await dotnetHelper.invokeMethodAsync('OnFileProcessedDirectly', fileData, JSON.stringify(result));
                    } else {
                        alert('File processing failed. Please try again.');
                    }
                } catch (error) {
                    console.error('Error in file processing:', error);
                    alert('Error processing file: ' + error.message);

                    // Clean up intervals and overlay on error
                    if (timerInterval) {
                        clearInterval(timerInterval);
                    }
                    if (window.aiProgressInterval) {
                        clearInterval(window.aiProgressInterval);
                        window.aiProgressInterval = null;
                    }
                    const overlayToRemove = document.getElementById('processing-overlay');
                    if (overlayToRemove) {
                        overlayToRemove.remove();
                    }
                }
            }
        }, false);
    },

    // Upload original file directly without base64 conversion
    uploadOriginalFile: async function (endpoint, file, config) {
        try {
            console.log(`uploadOriginalFile: ${endpoint}, file: ${file.name}`);
            console.log(`uploadOriginalFile: file size: ${file.size}, type: ${file.type}`);
            
            // Create FormData directly from the original file
            const formData = new FormData();
            formData.append('file', file, file.name);
            
            // Add configuration parameters if provided
            if (config) {
                if (config.useSyncfusion !== undefined) formData.append("useSyncfusion", String(config.useSyncfusion));
                if (config.useGoogle !== undefined) formData.append("useGoogle", String(config.useGoogle));
                if (config.useClaudeVision !== undefined) formData.append("useClaudeVision", String(config.useClaudeVision));
                if (config.useClaudeValidation !== undefined) formData.append("useClaudeValidation", String(config.useClaudeValidation));
                if (config.useGroqValidation !== undefined) formData.append("useGroqValidation", String(config.useGroqValidation));
                if (config.useSignatureDetection !== undefined) formData.append("useSignatureDetection", String(config.useSignatureDetection));
                if (config.mode !== undefined) formData.append("mode", config.mode);
                if (config.debugMode !== undefined) formData.append("debugMode", String(config.debugMode));
                if (config.showFieldIds !== undefined) formData.append("showFieldIds", String(config.showFieldIds));
                console.log(`uploadOriginalFile: Added config parameters`, config);
            }
            
            console.log(`uploadOriginalFile: FormData created`);
            
            // Send request
            // Use current window location instead of hardcoded port
            const baseUrl = window.location.origin;
            const response = await fetch(`${baseUrl}${endpoint}`, {
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
    uploadFileDirectly: async function (endpoint, fileName, base64Data, contentType, useAi, config) {
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
            
            formData.append("useAi", useAi || false);
            
            // Add configuration parameters if provided
            if (config) {
                if (config.useSyncfusion !== undefined) formData.append("useSyncfusion", String(config.useSyncfusion));
                if (config.useGoogle !== undefined) formData.append("useGoogle", String(config.useGoogle));
                if (config.useClaudeVision !== undefined) formData.append("useClaudeVision", String(config.useClaudeVision));
                if (config.useClaudeValidation !== undefined) formData.append("useClaudeValidation", String(config.useClaudeValidation));
                if (config.useGroqValidation !== undefined) formData.append("useGroqValidation", String(config.useGroqValidation));
                if (config.useSignatureDetection !== undefined) formData.append("useSignatureDetection", String(config.useSignatureDetection));
                if (config.mode !== undefined) formData.append("mode", config.mode);
                if (config.debugMode !== undefined) formData.append("debugMode", String(config.debugMode));
                if (config.showFieldIds !== undefined) formData.append("showFieldIds", String(config.showFieldIds));
                // Accessibility services
                if (config.useAdobeAutotag !== undefined) formData.append("useAdobeAutotag", String(config.useAdobeAutotag));
                if (config.useAsposeAutotag !== undefined) formData.append("useAsposeAutotag", String(config.useAsposeAutotag));
                if (config.useAsposeFontEmbed !== undefined) formData.append("useAsposeFontEmbed", String(config.useAsposeFontEmbed));
                if (config.usePassportPdf !== undefined) formData.append("usePassportPdf", String(config.usePassportPdf));
                console.log(`uploadFileDirectly: Added config parameters`, config);
            }
            
            console.log(`uploadFileDirectly: useAi flag: ${useAi || false}`);
            console.log(`uploadFileDirectly: FormData created with blob size: ${blob.size}`);
            
            // Send request
            // Use current window location instead of hardcoded port
            const baseUrl = window.location.origin;
            const response = await fetch(`${baseUrl}${endpoint}`, {
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
        console.log('[DEBUG] triggerFileInputWithPassport called with inputId:', inputId);
        const input = document.getElementById(inputId);
        console.log('[DEBUG] Found input element:', input);
        if (input) {
            console.log('[DEBUG] Setting onchange handler');
            input.onchange = async function (event) {
                const files = event.target.files;
                if (files && files.length > 0) {
                    const file = files[0];

                    // Create processing overlay with timer
                    const overlay = document.createElement('div');
                    overlay.id = 'processing-overlay';
                    overlay.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.5);display:flex;align-items:center;justify-content:center;z-index:9999';

                    overlay.innerHTML = `
                        <div style="background:white;padding:30px;border-radius:10px;text-align:center;max-width:600px;max-height:80vh;overflow-y:auto">
                            <div class="spinner-border text-primary mb-3" style="width:3rem;height:3rem" role="status">
                                <span class="visually-hidden">Processing...</span>
                            </div>
                            <h4>Processing PDF with Closed-Loop Remediation</h4>
                            <div class="fs-1 fw-bold text-primary mt-3" id="timer-display">0:00</div>
                            <p class="text-muted mt-2" id="current-phase-text">Validating compliance and fixing errors...</p>

                            <!-- Processing Steps Checklist -->
                            <div class="mt-4" style="text-align:left">
                                <h6 class="mb-3">Progress:</h6>
                                <div id="processing-steps-list">
                                    <div class="processing-step" data-step="Validating PDF" style="margin-bottom:8px">
                                        <input type="checkbox" disabled style="margin-right:8px">
                                        <span>Initial Validation</span>
                                    </div>
                                    <div class="processing-step" data-step="Whitespace Cleanup" style="margin-bottom:8px">
                                        <input type="checkbox" disabled style="margin-right:8px">
                                        <span>Whitespace Cleanup</span>
                                    </div>
                                    <div class="processing-step" data-step="Content Remediation" style="margin-bottom:8px">
                                        <input type="checkbox" disabled style="margin-right:8px">
                                        <span>Content Remediation</span>
                                    </div>
                                    <div class="processing-step" data-step="Structure Enhancement" style="margin-bottom:8px">
                                        <input type="checkbox" disabled style="margin-right:8px">
                                        <span>Structure Enhancement</span>
                                    </div>
                                    <div class="processing-step" data-step="Form Field Remediation" style="margin-bottom:8px">
                                        <input type="checkbox" disabled style="margin-right:8px">
                                        <span>Form Field Remediation</span>
                                    </div>
                                    <div class="processing-step" data-step="Link Structure Fixes" style="margin-bottom:8px">
                                        <input type="checkbox" disabled style="margin-right:8px">
                                        <span>Link Structure Fixes</span>
                                    </div>
                                    <div class="processing-step" data-step="Font Fixes" style="margin-bottom:8px">
                                        <input type="checkbox" disabled style="margin-right:8px">
                                        <span>Font & PDF/A Conversion</span>
                                    </div>
                                    <div class="processing-step" data-step="Table and List Structure Fixes" style="margin-bottom:8px">
                                        <input type="checkbox" disabled style="margin-right:8px">
                                        <span>Table and List Structure</span>
                                    </div>
                                    <div class="processing-step" data-step="Alternative Text" style="margin-bottom:8px">
                                        <input type="checkbox" disabled style="margin-right:8px">
                                        <span>Alternative Text</span>
                                    </div>
                                    <div class="processing-step" data-step="GPT-Powered Remediation" style="margin-bottom:8px">
                                        <input type="checkbox" disabled style="margin-right:8px">
                                        <span>GPT Fallback</span>
                                    </div>
                                    <div class="processing-step" data-step="Metadata Finalization" style="margin-bottom:8px">
                                        <input type="checkbox" disabled style="margin-right:8px">
                                        <span>Metadata Finalization</span>
                                    </div>
                                </div>
                            </div>

                            <!-- Action Buttons -->
                            <div class="mt-4" style="display:flex;gap:10px;justify-content:center">
                                <button id="download-current-btn" class="btn btn-secondary" style="display:none">
                                    Download Current PDF
                                </button>
                                <button id="cancel-processing-btn" class="btn btn-danger" style="display:none">
                                    Cancel Processing
                                </button>
                            </div>
                        </div>
                    `;

                    document.body.appendChild(overlay);

                    // Start timer
                    const startTime = Date.now();
                    const timerInterval = setInterval(() => {
                        const elapsed = Math.floor((Date.now() - startTime) / 1000);
                        const minutes = Math.floor(elapsed / 60);
                        const seconds = elapsed % 60;
                        const display = document.getElementById('timer-display');
                        if (display) {
                            display.textContent = `${minutes}:${seconds.toString().padStart(2, '0')}`;
                        }
                    }, 1000);

                    try {
                        console.log('Using PassportPDF for processing');

                        const endpoint = '/api/process-with-passportpdf-auto';

                        // Create FormData with PDF/UA validation enabled
                        const formData = new FormData();
                        formData.append('file', file);

                        // Add config parameters from UI checkboxes
                        formData.append('useSyncfusion', String(document.querySelector('#useSyncfusion')?.checked ?? true));
                        formData.append('useGoogle', String(document.querySelector('#useGoogle')?.checked ?? false));
                        formData.append('useClaudeVision', String(document.querySelector('#useClaudeVision')?.checked ?? false));
                        formData.append('useClaudeValidation', String(document.querySelector('#useClaudeValidation')?.checked ?? false));
                        formData.append('useGroqValidation', String(document.querySelector('#useGroqValidation')?.checked ?? false));
                        formData.append('useMultiStageValidation', String(document.querySelector('#useMultiStageValidation')?.checked ?? false));
                        formData.append('useSignatureDetection', String(document.querySelector('#useSignatureDetection')?.checked ?? false));
                        formData.append('useAsposeFontEmbed', String(document.querySelector('#useAsposeFontEmbed')?.checked ?? true));

                        // Make the fetch request to PassportPDF endpoint
                        const response = await fetch(endpoint, {
                            method: 'POST',
                            body: formData
                        });

                        if (!response.ok) {
                            throw new Error(`Server returned ${response.status}: ${response.statusText}`);
                        }

                        const result = await response.json();

                        // Show buttons after 3 seconds
                        setTimeout(() => {
                            const downloadBtn = document.getElementById('download-current-btn');
                            const cancelBtn = document.getElementById('cancel-processing-btn');
                            if (downloadBtn) downloadBtn.style.display = 'inline-block';
                            if (cancelBtn) cancelBtn.style.display = 'inline-block';
                        }, 3000);

                        // Progress polling removed

                        // Wire up download button
                        const downloadBtn = document.getElementById('download-current-btn');
                        if (downloadBtn) {
                            downloadBtn.onclick = () => {
                                if (window.accessForm._currentProcessingPdf) {
                                    // Download the current PDF
                                    const blob = new Blob([window.accessForm._currentProcessingPdf], { type: 'application/pdf' });
                                    const url = URL.createObjectURL(blob);
                                    const a = document.createElement('a');
                                    a.href = url;
                                    a.download = 'current-processing-state.pdf';
                                    a.click();
                                    URL.revokeObjectURL(url);
                                } else {
                                    alert('Current PDF not available yet');
                                }
                            };
                        }

                        // Wire up cancel button
                        const cancelBtn = document.getElementById('cancel-processing-btn');
                        if (cancelBtn) {
                            cancelBtn.onclick = () => {
                                if (confirm('Are you sure you want to cancel processing?')) {
                                    // Clear intervals
                                    clearInterval(timerInterval);
                                    if (window.accessForm._progressInterval) {
                                        clearInterval(window.accessForm._progressInterval);
                                    }

                                    // Remove overlay
                                    const overlayToRemove = document.getElementById('processing-overlay');
                                    if (overlayToRemove) {
                                        overlayToRemove.remove();
                                    }

                                    // Optionally call a cancel endpoint
                                    if (result && result.sessionId) {
                                        fetch(`/api/cancel/${result.sessionId}`, { method: 'POST' }).catch(err => {
                                            console.error('Error canceling:', err);
                                        });
                                    }
                                }
                            };
                        }

                        // Clean up
                        clearInterval(timerInterval);
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
                                await window.accessForm.dotNetHelper.invokeMethodAsync('OnFileProcessedDirectly', fileData, JSON.stringify(result));
                            } else {
                                console.error('Blazor helper not found');
                                alert('Error: Unable to process file. Please refresh the page.');
                            }
                        } else {
                            alert('PassportPDF processing failed. Please try again.');
                        }
                    } catch (error) {
                        // Clean up on error
                        clearInterval(timerInterval);
                        const overlayToRemove = document.getElementById('processing-overlay');
                        if (overlayToRemove) {
                            overlayToRemove.remove();
                        }

                        console.error('Error processing with PassportPDF:', error);
                        alert('Error processing file: ' + error.message);
                    }
                }
            };
            console.log('[DEBUG] About to click input element');
            input.click();
            console.log('[DEBUG] Input clicked');
        } else {
            console.error('[DEBUG] Input element not found!');
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
                            // AI Processing stages
                            const aiStages = [
                                { name: 'Uploading document', time: 2 },
                                { name: 'Initial document analysis', time: 3 },
                                { name: 'Claude AI field detection', time: 15 },
                                { name: 'Field validation and optimization', time: 8 },
                                { name: 'Creating interactive form fields', time: 5 },
                                { name: 'Final quality check', time: 2 }
                            ];

                            let currentStage = 0;
                            let stageProgress = 0;
                            const totalTime = aiStages.reduce((sum, s) => sum + s.time, 0);

                            overlay.innerHTML = `
                                <div style="background:white;padding:30px;border-radius:10px;text-align:center;min-width:500px;max-width:600px">
                                    <div class="spinner-border text-primary mb-3" style="width:3rem;height:3rem" role="status">
                                        <span class="visually-hidden">Processing...</span>
                                    </div>
                                    <h4>AI Document Processing</h4>

                                    <div class="fs-1 fw-bold text-primary mt-3" id="timer-display">0:00</div>

                                    <div class="alert alert-info mt-3 mb-3">
                                        <p class="mb-2">🤖 <strong>Anthropic Claude AI Analysis</strong></p>
                                        <p class="mb-1" id="stage-name">Initializing...</p>
                                        <p class="text-muted small mb-0">Advanced form field detection and optimization</p>
                                    </div>

                                    <!-- Progress bar disabled - using checkboxes in Blazor UI instead
                                    <div class="progress mb-3" style="height: 25px;">
                                        <div id="progress-bar" class="progress-bar progress-bar-striped progress-bar-animated bg-primary"
                                             role="progressbar" style="width: 0%" aria-valuenow="0" aria-valuemin="0" aria-valuemax="100">
                                            <span id="progress-text">0%</span>
                                        </div>
                                    </div> -->

                                    <div class="small text-muted">
                                        Stage <span id="stage-num">1</span> of ${aiStages.length}
                                    </div>
                                </div>
                            `;

                            // Start timer
                            const startTime = Date.now();
                            timerInterval = setInterval(() => {
                                const elapsed = Math.floor((Date.now() - startTime) / 1000);
                                const minutes = Math.floor(elapsed / 60);
                                const seconds = elapsed % 60;
                                const display = document.getElementById('timer-display');
                                if (display) {
                                    display.textContent = `${minutes}:${seconds.toString().padStart(2, '0')}`;
                                }
                            }, 100);

                            // Simulate progress through stages
                            let progressInterval = setInterval(() => {
                                if (currentStage < aiStages.length) {
                                    stageProgress += 100 / (aiStages[currentStage].time * 10); // Update every 100ms

                                    if (stageProgress >= 100) {
                                        currentStage++;
                                        stageProgress = 0;
                                    }

                                    if (currentStage < aiStages.length) {
                                        // Update stage display
                                        const stageEl = document.getElementById('stage-name');
                                        if (stageEl) stageEl.textContent = aiStages[currentStage].name + '...';

                                        const stageNumEl = document.getElementById('stage-num');
                                        if (stageNumEl) stageNumEl.textContent = (currentStage + 1).toString();

                                        // Calculate overall progress
                                        const completedTime = aiStages.slice(0, currentStage).reduce((sum, s) => sum + s.time, 0);
                                        const currentTime = aiStages[currentStage].time * (stageProgress / 100);
                                        const overallProgress = ((completedTime + currentTime) / totalTime) * 100;

                                        const progressBar = document.getElementById('progress-bar');
                                        if (progressBar) {
                                            progressBar.style.width = overallProgress + '%';
                                            progressBar.setAttribute('aria-valuenow', overallProgress.toString());
                                        }

                                        const progressText = document.getElementById('progress-text');
                                        if (progressText) {
                                            progressText.textContent = Math.round(overallProgress) + '%';
                                        }
                                    }
                                }
                            }, 100);

                            // Store progress interval for cleanup
                            window.aiProgressInterval = progressInterval;
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
                        let config = null;
                        if (window.accessForm && window.accessForm.isAiModeEnabled && window.accessForm.isAiModeEnabled()) {
                            // Use configurable endpoint for Word docs in AI mode
                            endpoint = isWord ? '/api/convert-with-config' : '/api/convert-with-ai';
                            // Get configuration from UI if using configurable endpoint
                            if (endpoint === '/api/convert-with-config') {
                                const fontEmbedCheckbox = document.querySelector('#useAsposeFontEmbed');
                                console.log('DEBUG: useAsposeFontEmbed checkbox element:', fontEmbedCheckbox);
                                console.log('DEBUG: useAsposeFontEmbed checked value:', fontEmbedCheckbox?.checked);

                                config = {
                                    useSyncfusion: document.querySelector('#useSyncfusion')?.checked ?? true,
                                    useGoogle: document.querySelector('#useGoogle')?.checked ?? false,
                                    useClaudeVision: document.querySelector('#useClaudeVision')?.checked ?? false,
                                    useClaudeValidation: document.querySelector('#useClaudeValidation')?.checked ?? false,
                                    mode: document.querySelector('input[name="processingMode"]:checked')?.value ?? 'Sequential',
                                    debugMode: document.querySelector('#debugMode')?.checked ?? true,
                                    showFieldIds: document.querySelector('#showFieldIds')?.checked ?? true,
                                    // Accessibility services
                                    useAdobeAutotag: document.querySelector('#useAdobeAutotag')?.checked ?? false,
                                    useAsposeAutotag: document.querySelector('#useAsposeAutotag')?.checked ?? false,
                                    useAsposeFontEmbed: true,  // HARDCODED TO TRUE FOR TESTING
                                    usePassportPdf: document.querySelector('#usePassportPdf')?.checked ?? false
                                };

                                console.log('DEBUG: Final config object:', config);
                                console.log('DEBUG: useAsposeFontEmbed HARDCODED TO TRUE');
                            }
                        } else {
                            // Use standard endpoints
                            endpoint = isWord ? '/api/convert' : '/api/remediate-pdf';
                        }
                        
                        const result = await window.accessForm.uploadOriginalFile(endpoint, file, config);
                        
                        // Remove processing overlay and clean up intervals
                        if (timerInterval) {
                            clearInterval(timerInterval);
                        }
                        if (window.aiProgressInterval) {
                            clearInterval(window.aiProgressInterval);
                            window.aiProgressInterval = null;
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
                                await window.accessForm.dotNetHelper.invokeMethodAsync('OnFileProcessedDirectly', fileData, JSON.stringify(result));
                            } else {
                                console.error('Blazor helper not found');
                                alert('Error: Unable to process file. Please refresh the page.');
                            }
                        } else {
                            alert('File processing failed. Please try again.');
                        }
                    } catch (error) {
                        console.error('Error processing browse button file:', error);

                        // Remove processing overlay and clean up intervals on error
                        if (timerInterval) {
                            clearInterval(timerInterval);
                        }
                        if (window.aiProgressInterval) {
                            clearInterval(window.aiProgressInterval);
                            window.aiProgressInterval = null;
                        }
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

    // Download file from base64 with "Save As" dialog
    downloadFile: async function (base64Data, fileName) {
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

            // Try to use File System Access API (Chrome/Edge) for "Save As" dialog
            if ('showSaveFilePicker' in window) {
                try {
                    const fileHandle = await window.showSaveFilePicker({
                        suggestedName: fileName || 'document.pdf',
                        types: [{
                            description: 'PDF Document',
                            accept: { 'application/pdf': ['.pdf'] }
                        }]
                    });

                    const writable = await fileHandle.createWritable();
                    await writable.write(blob);
                    await writable.close();

                    console.log(`Successfully saved: ${fileName} (${blob.size} bytes)`);
                    return true;
                } catch (err) {
                    // User cancelled the save dialog or API not supported
                    if (err.name === 'AbortError') {
                        console.log('Save cancelled by user');
                        return false;
                    }
                    // Fall through to legacy method
                    console.log('File System Access API failed, using fallback:', err);
                }
            }

            // Fallback: Use legacy download with "Save As" prompt by NOT setting download attribute
            // This triggers the browser's native "Save As" dialog
            const url = window.URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            // Setting download attribute bypasses "Save As" dialog, so we omit it for Safari/Firefox
            // However, for better compatibility, we keep it but rely on browser settings
            link.download = fileName || 'document.pdf';
            link.setAttribute('target', '_blank'); // Open in new tab context
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);

            // Clean up
            setTimeout(() => {
                window.URL.revokeObjectURL(url);
            }, 100);

            console.log(`Successfully triggered download: ${fileName} (${blob.size} bytes)`);
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
