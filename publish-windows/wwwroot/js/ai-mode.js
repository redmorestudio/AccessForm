// Add this to your wwwroot/js/site.js file after the existing accessForm functions

// AI Mode functions
window.accessForm.checkAiHealth = async function() {
    try {
        const response = await fetch('/api/health');
        const data = await response.json();
        return data;
    } catch (error) {
        console.error('Health check failed:', error);
        return null;
    }
};

window.accessForm.uploadFileWithAi = async function(endpoint, fileName, base64Data, mimeType) {
    try {
        // Convert base64 to blob
        const byteCharacters = atob(base64Data);
        const byteNumbers = new Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        const byteArray = new Uint8Array(byteNumbers);
        const blob = new Blob([byteArray], { type: mimeType });
        
        // Create FormData
        const formData = new FormData();
        formData.append('file', blob, fileName);
        
        // Send request with AI endpoint
        const response = await fetch('/api/convert-with-ai', {
            method: 'POST',
            body: formData
        });
        
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        return await response.text();
    } catch (error) {
        console.error('AI upload failed:', error);
        throw error;
    }
};
