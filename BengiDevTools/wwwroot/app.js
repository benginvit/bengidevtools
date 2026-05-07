window.scrollToBottom = function (elementId) {
    requestAnimationFrame(() => {
        const el = document.getElementById(elementId);
        if (el) el.scrollTop = el.scrollHeight;
    });
};

window.makeDraggable = function (modalId) {
    const modal  = document.getElementById(modalId);
    if (!modal) return;
    const handle = modal.querySelector('.monitor-drag-handle');
    if (!handle) return;
    handle.addEventListener('mousedown', function (e) {
        if (e.button !== 0) return;
        const rect = modal.getBoundingClientRect();
        modal.style.transform = 'none';
        modal.style.left = rect.left + 'px';
        modal.style.top  = rect.top  + 'px';
        let dx = e.clientX - rect.left;
        let dy = e.clientY - rect.top;
        function onMove(e) {
            modal.style.left = Math.max(0, e.clientX - dx) + 'px';
            modal.style.top  = Math.max(0, e.clientY - dy) + 'px';
        }
        function onUp() {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup',  onUp);
        }
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup',  onUp);
        e.preventDefault();
    });
};

window.downloadFile = function (filename, content) {
    const blob = new Blob([content], { type: 'application/json' });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
};
