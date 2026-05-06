window.scrollToBottom = function (elementId) {
    requestAnimationFrame(() => {
        const el = document.getElementById(elementId);
        if (el) el.scrollTop = el.scrollHeight;
    });
};
