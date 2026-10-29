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
// Header menus: close on outside click or Escape, and keep one open at a time.
document.addEventListener('click', event => {
    document.querySelectorAll('.header-menu[open]').forEach(menu => {
        if (!menu.contains(event.target)) menu.removeAttribute('open');
    });
});
document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    document.querySelectorAll('.header-menu[open]').forEach(menu => {
        menu.removeAttribute('open');
        menu.querySelector('summary').focus();
    });
});
// Read the user's device-local time rather than the server's time zone.
window.osynixLocalClock = () => {
    const now = new Date();
    return { hour: now.getHours(), date: now.toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'short', year: 'numeric' }) };
};

window.osynixCopyReviewLink = async value => {
    try {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(value);
            return true;
        }
    } catch { /* Keep a manual-copy fallback for restricted browsers. */ }
    const field = document.getElementById('hiring-review-link');
    if (!field) return false;
    field.focus();
    field.select();
    try { return document.execCommand('copy'); } catch { return false; }
};
