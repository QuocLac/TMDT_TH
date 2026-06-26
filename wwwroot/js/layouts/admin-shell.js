(() => {
    "use strict";

    const openClass = "admin-sidebar-open";
    const body = document.body;
    const toggle = document.querySelector("[data-admin-sidebar-toggle]");
    const dismiss = document.querySelector("[data-admin-sidebar-dismiss]");
    const sidebar = document.getElementById("admin-sidebar");
    const desktopMedia = window.matchMedia("(min-width: 62rem)");

    if (!toggle || !sidebar) return;

    function setOpen(isOpen, { restoreFocus = false } = {}) {
        body.classList.toggle(openClass, isOpen);
        toggle.setAttribute("aria-expanded", String(isOpen));
        if (dismiss) dismiss.tabIndex = isOpen ? 0 : -1;

        if (isOpen) {
            sidebar.querySelector("a, button, [tabindex]:not([tabindex='-1'])")?.focus();
        } else if (restoreFocus) {
            toggle.focus();
        }
    }

    toggle.addEventListener("click", () => {
        setOpen(!body.classList.contains(openClass));
    });

    dismiss?.addEventListener("click", () => setOpen(false, { restoreFocus: true }));

    sidebar.addEventListener("click", (event) => {
        if (!desktopMedia.matches && event.target.closest("a")) {
            setOpen(false);
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && body.classList.contains(openClass)) {
            setOpen(false, { restoreFocus: true });
        }
    });

    desktopMedia.addEventListener?.("change", (event) => {
        if (event.matches) setOpen(false);
    });
})();
