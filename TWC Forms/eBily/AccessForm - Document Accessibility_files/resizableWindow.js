window.initializeResizableWindow = function(windowId, headerId) {
    const windowEl = document.getElementById(windowId);
    const headerEl = document.getElementById(headerId);

    if (!windowEl || !headerEl) {
        console.error('Window or header element not found:', windowId, headerId);
        return;
    }

    // Bring to front when clicked anywhere on the window
    windowEl.addEventListener('mousedown', function() {
        const allWindows = document.querySelectorAll('.tag-modification-window, .cascade-correction-panel');
        allWindows.forEach(w => w.style.zIndex = '1040');
        windowEl.style.zIndex = '1050';
    });

    // Make draggable
    let isDragging = false;
    let currentX;
    let currentY;
    let initialX;
    let initialY;

    headerEl.addEventListener('mousedown', function(e) {
        // Don't drag if clicking the close button
        if (e.target.classList.contains('btn-close')) return;

        isDragging = true;
        initialX = e.clientX - windowEl.offsetLeft;
        initialY = e.clientY - windowEl.offsetTop;

        // Bring to front when starting drag
        const allWindows = document.querySelectorAll('.tag-modification-window, .cascade-correction-panel');
        allWindows.forEach(w => w.style.zIndex = '1040');
        windowEl.style.zIndex = '1050';
    });

    document.addEventListener('mousemove', function(e) {
        if (isDragging) {
            e.preventDefault();
            currentX = e.clientX - initialX;
            currentY = e.clientY - initialY;

            windowEl.style.left = currentX + 'px';
            windowEl.style.top = currentY + 'px';
        }
    });

    document.addEventListener('mouseup', function() {
        isDragging = false;
    });

    // Make resizable
    const resizeHandles = windowEl.querySelectorAll('.resize-handle');

    resizeHandles.forEach(handle => {
        let isResizing = false;
        let startX, startY, startWidth, startHeight, startLeft, startTop;

        handle.addEventListener('mousedown', function(e) {
            isResizing = true;
            startX = e.clientX;
            startY = e.clientY;
            startWidth = parseInt(document.defaultView.getComputedStyle(windowEl).width, 10);
            startHeight = parseInt(document.defaultView.getComputedStyle(windowEl).height, 10);
            startLeft = windowEl.offsetLeft;
            startTop = windowEl.offsetTop;

            e.preventDefault();
            e.stopPropagation();

            // Bring to front when starting resize
            const allWindows = document.querySelectorAll('.tag-modification-window, .cascade-correction-panel');
            allWindows.forEach(w => w.style.zIndex = '1040');
            windowEl.style.zIndex = '1050';
        });

        document.addEventListener('mousemove', function(e) {
            if (!isResizing) return;

            e.preventDefault();

            if (handle.classList.contains('resize-e')) {
                const width = startWidth + (e.clientX - startX);
                if (width > 300) {
                    windowEl.style.width = width + 'px';
                }
            }

            if (handle.classList.contains('resize-w')) {
                const width = startWidth - (e.clientX - startX);
                if (width > 300) {
                    windowEl.style.width = width + 'px';
                    windowEl.style.left = (startLeft + (e.clientX - startX)) + 'px';
                }
            }

            if (handle.classList.contains('resize-s')) {
                const height = startHeight + (e.clientY - startY);
                if (height > 200) {
                    windowEl.style.height = height + 'px';
                }
            }

            if (handle.classList.contains('resize-n')) {
                const height = startHeight - (e.clientY - startY);
                if (height > 200) {
                    windowEl.style.height = height + 'px';
                    windowEl.style.top = (startTop + (e.clientY - startY)) + 'px';
                }
            }

            if (handle.classList.contains('resize-se')) {
                const width = startWidth + (e.clientX - startX);
                const height = startHeight + (e.clientY - startY);
                if (width > 300) windowEl.style.width = width + 'px';
                if (height > 200) windowEl.style.height = height + 'px';
            }

            if (handle.classList.contains('resize-sw')) {
                const width = startWidth - (e.clientX - startX);
                const height = startHeight + (e.clientY - startY);
                if (width > 300) {
                    windowEl.style.width = width + 'px';
                    windowEl.style.left = (startLeft + (e.clientX - startX)) + 'px';
                }
                if (height > 200) windowEl.style.height = height + 'px';
            }

            if (handle.classList.contains('resize-ne')) {
                const width = startWidth + (e.clientX - startX);
                const height = startHeight - (e.clientY - startY);
                if (width > 300) windowEl.style.width = width + 'px';
                if (height > 200) {
                    windowEl.style.height = height + 'px';
                    windowEl.style.top = (startTop + (e.clientY - startY)) + 'px';
                }
            }

            if (handle.classList.contains('resize-nw')) {
                const width = startWidth - (e.clientX - startX);
                const height = startHeight - (e.clientY - startY);
                if (width > 300) {
                    windowEl.style.width = width + 'px';
                    windowEl.style.left = (startLeft + (e.clientX - startX)) + 'px';
                }
                if (height > 200) {
                    windowEl.style.height = height + 'px';
                    windowEl.style.top = (startTop + (e.clientY - startY)) + 'px';
                }
            }
        });

        document.addEventListener('mouseup', function() {
            isResizing = false;
        });
    });
};