// Ensure accessForm is available globally
window.addEventListener('DOMContentLoaded', function() {
    console.log('DOM loaded, accessForm available:', !!window.accessForm);
});

// Make sure the functions are available as direct window functions for Blazor
window.triggerFileInputWithHandler = function(inputId) {
    if (window.accessForm && window.accessForm.triggerFileInputWithHandler) {
        window.accessForm.triggerFileInputWithHandler(inputId);
    } else {
        console.log('Waiting for accessForm to load...');
        setTimeout(function() {
            if (window.accessForm && window.accessForm.triggerFileInputWithHandler) {
                window.accessForm.triggerFileInputWithHandler(inputId);
            }
        }, 100);
    }
};

window.triggerFileInputWithPassport = function(inputId) {
    if (window.accessForm && window.accessForm.triggerFileInputWithPassport) {
        window.accessForm.triggerFileInputWithPassport(inputId);
    } else {
        console.log('Waiting for accessForm to load...');
        setTimeout(function() {
            if (window.accessForm && window.accessForm.triggerFileInputWithPassport) {
                window.accessForm.triggerFileInputWithPassport(inputId);
            }
        }, 100);
    }
};
