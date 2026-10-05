// Keep a profile/editor modal in its own scroll and keyboard context.
(() => {
    let overlay = null, previousFocus = null, previousOverflow = "";
    const focusables = () => [...overlay.querySelectorAll('button:not(:disabled),a[href],input:not(:disabled),select:not(:disabled),textarea:not(:disabled),[tabindex="0"]')].filter(el => el.getClientRects().length);
    const update = () => {
        const next = document.querySelector('.overlay');
        if (next === overlay) return;
        if (next) {
            if (!overlay) { previousFocus = document.activeElement; previousOverflow = document.body.style.overflow; }
            overlay = next;
            document.body.style.overflow = 'hidden';
            (overlay.querySelector('.drawer-head > button') || focusables()[0])?.focus({ preventScroll: true });
        } else {
            overlay = null;
            document.body.style.overflow = previousOverflow;
            if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true });
        }
    };
    new MutationObserver(update).observe(document.body, { childList: true, subtree: true });
    document.addEventListener('keydown', event => {
        if (!overlay) return;
        if (event.key === 'Escape') { overlay.querySelector('.drawer-head > button')?.click(); return; }
        if (event.key !== 'Tab') return;
        const items = focusables();
        if (!items.length) { event.preventDefault(); return; }
        if (event.shiftKey && (document.activeElement === items[0] || !overlay.contains(document.activeElement))) { event.preventDefault(); items.at(-1).focus(); }
        else if (!event.shiftKey && (document.activeElement === items.at(-1) || !overlay.contains(document.activeElement))) { event.preventDefault(); items[0].focus(); }
    });
})();
