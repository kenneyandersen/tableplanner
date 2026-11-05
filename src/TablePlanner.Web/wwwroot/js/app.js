// Small browser helpers called from Blazor through JS interop.

// Rows marked data-drag-group (groups on the Plan page) are dragged with the browser's drag and drop. Some browsers
// only start a drag when it carries data, which Blazor cannot set, so it is set here.
document.addEventListener("dragstart", e => {
    if (e.target instanceof Element && e.target.closest("[data-drag-group]")) {
        e.dataTransfer.setData("text/plain", "");
        e.dataTransfer.effectAllowed = "move";
    }
});

window.tablePlanner = {
    /** Offers content (text, or bytes from a .NET byte[]) to the user as a file download. */
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

    /**
     * Reads an uploaded picture and makes it at most maxSize pixels on its longest side, as a JPEG data URL
     * (on white, for pictures with transparency). Returns { dataUrl, width, height }.
     */
    async prepareImage(bytes, maxSize) {
        const bitmap = await createImageBitmap(new Blob([bytes]));
        const scale = Math.min(1, maxSize / Math.max(bitmap.width, bitmap.height));
        const width = Math.max(1, Math.round(bitmap.width * scale));
        const height = Math.max(1, Math.round(bitmap.height * scale));
        const canvas = document.createElement("canvas");
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext("2d");
        context.fillStyle = "#ffffff";
        context.fillRect(0, 0, width, height);
        context.drawImage(bitmap, 0, 0, width, height);
        bitmap.close();
        return { dataUrl: canvas.toDataURL("image/jpeg", 0.88), width, height };
    },

    /** Clicks an element by ID, e.g. a hidden file input opened from a menu. */
    clickElement(id) {
        document.getElementById(id)?.click();
    },

    /** The element's top-left corner on the screen, as [left, top]. */
    elementOrigin(element) {
        const rect = element.getBoundingClientRect();
        return [rect.left, rect.top];
    },

    /** Converts a pointer position on the page to the SVG's own coordinates (metres on a floor). */
    svgPoint(svg, clientX, clientY) {
        const point = new DOMPoint(clientX, clientY).matrixTransform(svg.getScreenCTM().inverse());
        return [point.x, point.y];
    },

    /** Images kept in IndexedDB (they are too large for localStorage), keyed by "projectId/imageId". */
    images: {
        open() {
            this._db ??= new Promise((resolve, reject) => {
                const request = indexedDB.open("tableplanner", 1);
                request.onupgradeneeded = () => request.result.createObjectStore("images");
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
            return this._db;
        },
        async run(mode, action) {
            const db = await this.open();
            return new Promise((resolve, reject) => {
                const store = db.transaction("images", mode).objectStore("images");
                const request = action(store);
                request.onsuccess = () => resolve(request.result ?? null);
                request.onerror = () => reject(request.error);
            });
        },
        put(key, dataUrl) { return this.run("readwrite", s => s.put(dataUrl, key)); },
        get(key) { return this.run("readonly", s => s.get(key)); },
        remove(key) { return this.run("readwrite", s => s.delete(key)); },
        keys(prefix) { return this.run("readonly", s => s.getAllKeys(IDBKeyRange.bound(prefix, prefix + "\uffff"))); },
        removeAll(prefix) { return this.run("readwrite", s => s.delete(IDBKeyRange.bound(prefix, prefix + "\uffff"))); },
    },

    /** Opens the browser's print dialog for the document in an iframe (the print preview). */
    printFrame(frame) {
        frame.contentWindow.focus();
        frame.contentWindow.print();
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
