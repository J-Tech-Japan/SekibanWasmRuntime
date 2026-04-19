// CSP-safe Bootstrap modal helpers. The template-imported razor pages originally did
// `IJSRuntime.InvokeVoidAsync("eval", "new bootstrap.Modal(...).show()")`, which breaks
// under any strict Content-Security-Policy. Wrap the Bootstrap API once here and let
// razor call us via `IJSRuntime.InvokeVoidAsync("sekibanInterop.showModal", "#id")`.

(function () {
    function getOrCreateModal(selectorOrElement) {
        const el = typeof selectorOrElement === 'string'
            ? document.querySelector(selectorOrElement)
            : selectorOrElement;
        if (!el) return null;
        // `bootstrap` is shipped by the `bootstrap.bundle.min.js` tag in App.razor.
        if (typeof bootstrap === 'undefined' || !bootstrap.Modal) return null;
        return bootstrap.Modal.getOrCreateInstance(el);
    }

    window.sekibanInterop = {
        showModal(selector) {
            const modal = getOrCreateModal(selector);
            if (modal) modal.show();
        },
        hideModal(selector) {
            const modal = getOrCreateModal(selector);
            if (modal) modal.hide();
        }
    };
})();
