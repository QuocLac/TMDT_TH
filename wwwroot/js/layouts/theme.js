(() => {
    "use strict";

    const storageKey = "fastbuy-theme";
    const iconSystemVersion = "2026.07.25.1";
    const root = document.documentElement;
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const currentScript = document.currentScript;

    function ensureIconSystem() {
        if (window.FastBuyIcons
            || document.querySelector("[data-fastbuy-icons-script]")) {
            return;
        }

        const iconScriptUrl = currentScript?.src
            ? new URL("../core/icons.js", currentScript.src)
            : new URL("/js/core/icons.js", window.location.origin);

        iconScriptUrl.searchParams.set("v", iconSystemVersion);

        const script = document.createElement("script");
        script.src = iconScriptUrl.href;
        script.async = false;
        script.dataset.fastbuyIconsScript = "true";
        document.head.append(script);
    }

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

    ensureIconSystem();

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
