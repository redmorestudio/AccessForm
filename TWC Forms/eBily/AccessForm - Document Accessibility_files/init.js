// Safe initialization wrapper
window.safeInitialize = {
    initializeDragDrop: function(dotnetHelper, dropZoneId) {
        // Wait for accessForm to be available
        if (window.accessForm && window.accessForm.initializeDragDrop) {
            window.accessForm.initializeDragDrop(dotnetHelper, dropZoneId);
        } else {
            console.log('Waiting for accessForm to load...');
            setTimeout(function() {
                window.safeInitialize.initializeDragDrop(dotnetHelper, dropZoneId);
            }, 100);
        }
    }
};

// Direct function mappings for Blazor
window.initializeDragDrop = function(dotnetHelper, dropZoneId) {
    window.safeInitialize.initializeDragDrop(dotnetHelper, dropZoneId);
};
