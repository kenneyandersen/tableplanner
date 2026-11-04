// Small browser helpers called from Blazor through JS interop.
window.tablePlanner = {
    /** Offers text content to the user as a file download. */
    downloadFile(fileName, contentType, content) {
        const blob = new Blob([content], { type: contentType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    },

    /** Keeps sending pointer events to the element while dragging, even when the pointer leaves it. */
    capturePointer(element, pointerId) {
        try {
            element.setPointerCapture(pointerId);
        } catch {
            // The pointer is already released (very short click); nothing to capture.
        }
    },

    /**
     * Stops the browser's own handling of the editor's shortcut keys (scrolling, bookmarking, …)
     * while the element has focus. Blazor still receives the key events.
     */
    preventEditorKeys(element) {
        element.addEventListener("keydown", e => {
            const arrows = ["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"];
            const ctrlKeys = ["a", "d", "y", "z"];
            if (arrows.includes(e.key) || e.key === "Delete" || e.key === "Backspace"
                || ((e.ctrlKey || e.metaKey) && ctrlKeys.includes(e.key.toLowerCase()))) {
                e.preventDefault();
            }
        });
    },
};
