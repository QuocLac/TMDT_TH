(() => {
    "use strict";

    const storageKey = "fastbuy-theme";
    const root = document.documentElement;
    const media = window.matchMedia("(prefers-color-scheme: dark)");

    function readPreference() {
        try {
            const value = window.localStorage.getItem(storageKey);
            return value === "light" || value === "dark" ? value : null;
        } catch {
            return null;
        }
    }

    function resolveTheme(preference = readPreference()) {
        return preference ?? (media.matches ? "dark" : "light");
    }

    function applyTheme(theme) {
        root.dataset.theme = theme;
        root.style.colorScheme = theme;
    }

    function updateControls(theme) {
        document.querySelectorAll("[data-theme-toggle]").forEach((button) => {
            const nextTheme = theme === "dark" ? "light" : "dark";
            button.setAttribute("aria-pressed", String(theme === "dark"));
            button.setAttribute(
                "aria-label",
                `Chuyển sang giao diện ${nextTheme === "dark" ? "tối" : "sáng"}`
            );

            const label = button.querySelector("[data-theme-label]");
            if (label) {
                label.textContent = nextTheme === "dark"
                    ? "Giao diện tối"
                    : "Giao diện sáng";
            }
        });
    }

    const initialTheme = resolveTheme();
    applyTheme(initialTheme);

    document.addEventListener("DOMContentLoaded", () => {
        updateControls(root.dataset.theme || initialTheme);

        document.addEventListener("click", (event) => {
            const button = event.target.closest("[data-theme-toggle]");
            if (!button) return;

            const currentTheme = root.dataset.theme || resolveTheme();
            const nextTheme = currentTheme === "dark" ? "light" : "dark";

            try {
                window.localStorage.setItem(storageKey, nextTheme);
            } catch {
                // Theme remains active for the current page.
            }

            applyTheme(nextTheme);
            updateControls(nextTheme);
        });
    });

    media.addEventListener?.("change", () => {
        if (readPreference() !== null) return;
        const theme = resolveTheme(null);
        applyTheme(theme);
        updateControls(theme);
    });
})();
