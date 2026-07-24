(() => {
    "use strict";

    const page = document.querySelector("[data-category-page]");
    const modalElement = document.getElementById("categoryModal");
    const form = document.getElementById("categoryForm");

    if (!page || !modalElement || !form) {
        return;
    }

    const fields = {
        id: document.getElementById("CategoryId"),
        name: document.getElementById("CategoryName"),
        parentId: document.getElementById("ParentId"),
        slug: document.getElementById("CategorySlug"),
        iconKey: document.getElementById("CategoryIconKey"),
        displayOrder: document.getElementById("CategoryDisplayOrder"),
        isVisible: document.getElementById("CategoryIsVisible"),
        metaTitle: document.getElementById("MetaTitle"),
        metaDescription: document.getElementById("MetaDescription")
    };

    const requiredFields = Object.values(fields);
    if (requiredFields.some(field => !field)) {
        console.error("Category editor is missing one or more required fields.");
        return;
    }

    const title = document.getElementById("categoryModalLabel");
    const errorBox = document.getElementById("categoryFormError");
    const errorMessage = errorBox?.querySelector(
        "[data-category-error-message]");
    const submitButton = document.getElementById(
        "categorySubmitButton");
    const iconSearch = document.getElementById("CategoryIconSearch");
    const iconGroup = document.getElementById("CategoryIconGroup");
    const iconGrid = modalElement.querySelector("[data-icon-grid]");
    const iconStatus = modalElement.querySelector("[data-icon-status]");
    const selectedIconPreview = modalElement.querySelector(
        "[data-selected-icon-preview]");
    const selectedIconLabel = modalElement.querySelector(
        "[data-selected-icon-label]");
    const selectedIconKey = modalElement.querySelector(
        "[data-selected-icon-key]");
    const metaDescriptionCount = modalElement.querySelector(
        "[data-meta-description-count]");

    if (
        !title
        || !submitButton
        || !iconSearch
        || !iconGroup
        || !iconGrid
        || !iconStatus
        || !selectedIconPreview
        || !selectedIconLabel
        || !selectedIconKey
    ) {
        console.error("Category editor controls are incomplete.");
        return;
    }

    const iconShapeByKey = Object.freeze({
        folder: "folder",
        catalog: "grid",
        tags: "tag",
        gift: "gift",
        electronics: "chip",
        phone: "phone",
        laptop: "laptop",
        tablet: "tablet",
        television: "monitor",
        camera: "camera",
        audio: "headphones",
        gaming: "gamepad",
        "computer-accessories": "keyboard",
        fashion: "shirt",
        shoes: "shoe",
        eyewear: "glasses",
        jewelry: "gem",
        watch: "watch",
        bag: "bag",
        home: "house",
        furniture: "sofa",
        kitchen: "utensils",
        appliances: "appliance",
        lighting: "bulb",
        tools: "wrench",
        decor: "palette",
        beauty: "sparkles",
        health: "heart",
        spa: "flower",
        fitness: "dumbbell",
        sports: "running",
        bicycle: "bike",
        outdoor: "tent",
        baby: "baby",
        toys: "puzzle",
        books: "book",
        stationery: "pen",
        pets: "paw",
        food: "utensils",
        grocery: "basket",
        coffee: "cup",
        car: "car",
        motorcycle: "motorcycle",
        office: "briefcase",
        printing: "printer"
    });

    /*
       All drawings are local SVG line icons. They inherit currentColor,
       so light/dark themes and selected states stay strictly monochrome.
    */
    const iconMarkup = Object.freeze({
        folder:
            '<path d="M3 7.5h6l2 2H21v9.5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>'
            + '<path d="M3 7.5V6a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v1.5"/>',
        grid:
            '<rect x="3.5" y="3.5" width="7" height="7" rx="1.2"/>'
            + '<rect x="13.5" y="3.5" width="7" height="7" rx="1.2"/>'
            + '<rect x="3.5" y="13.5" width="7" height="7" rx="1.2"/>'
            + '<rect x="13.5" y="13.5" width="7" height="7" rx="1.2"/>',
        tag:
            '<path d="M20.5 13.2 13.2 20.5a2 2 0 0 1-2.8 0L3.5 13.6V4h9.6l7.4 7.4a1.3 1.3 0 0 1 0 1.8z"/>'
            + '<circle cx="8" cy="8" r="1.2"/>',
        gift:
            '<rect x="3.5" y="9" width="17" height="11.5" rx="1.5"/>'
            + '<path d="M12 9v11.5M3 13h18M12 9H7.7a2.2 2.2 0 1 1 0-4.4C10.6 4.6 12 9 12 9z"/>'
            + '<path d="M12 9h4.3a2.2 2.2 0 1 0 0-4.4C13.4 4.6 12 9 12 9z"/>',
        chip:
            '<rect x="6" y="6" width="12" height="12" rx="2"/>'
            + '<rect x="9" y="9" width="6" height="6" rx="1"/>'
            + '<path d="M9 2.5v3M15 2.5v3M9 18.5v3M15 18.5v3M2.5 9h3M2.5 15h3M18.5 9h3M18.5 15h3"/>',
        phone:
            '<rect x="7" y="2.5" width="10" height="19" rx="2.2"/>'
            + '<path d="M10 5h4M11 18.5h2"/>',
        laptop:
            '<rect x="4.5" y="4" width="15" height="11" rx="1.5"/>'
            + '<path d="M2.5 19h19l-2-4H4.5z"/>',
        tablet:
            '<rect x="5.5" y="2.5" width="13" height="19" rx="2"/>'
            + '<circle cx="12" cy="18.3" r=".7" fill="currentColor" stroke="none"/>',
        monitor:
            '<rect x="3" y="4" width="18" height="13" rx="2"/>'
            + '<path d="M8 21h8M12 17v4"/>',
        camera:
            '<path d="M4 7.5h3l1.3-2h7.4l1.3 2h3a1.5 1.5 0 0 1 1.5 1.5v9A1.5 1.5 0 0 1 20 19.5H4A1.5 1.5 0 0 1 2.5 18V9A1.5 1.5 0 0 1 4 7.5z"/>'
            + '<circle cx="12" cy="13.5" r="3.5"/>',
        headphones:
            '<path d="M4 14v-2a8 8 0 0 1 16 0v2"/>'
            + '<path d="M4 13h2.5v7H5a2 2 0 0 1-2-2v-3a2 2 0 0 1 1-1.7zM20 13h-2.5v7H19a2 2 0 0 0 2-2v-3a2 2 0 0 0-1-1.7z"/>',
        gamepad:
            '<path d="M7 8h10a4 4 0 0 1 3.8 5.3l-1.5 4.2a2.2 2.2 0 0 1-3.6.9L13.8 17h-3.6l-1.9 1.4a2.2 2.2 0 0 1-3.6-.9l-1.5-4.2A4 4 0 0 1 7 8z"/>'
            + '<path d="M7.5 12v4M5.5 14h4M16.5 12.5h.01M18.5 15h.01"/>',
        keyboard:
            '<rect x="2.5" y="6" width="19" height="12" rx="2"/>'
            + '<path d="M6 10h.01M9 10h.01M12 10h.01M15 10h.01M18 10h.01M6 14h.01M9 14h.01M12 14h6"/>',
        shirt:
            '<path d="M8.5 4 5 5.5 2.8 10l3.4 1.7L7.5 9v11h9V9l1.3 2.7 3.4-1.7L19 5.5 15.5 4A4 4 0 0 1 12 6a4 4 0 0 1-3.5-2z"/>',
        shoe:
            '<path d="M4 5.5c1.8 4.4 4.8 7.2 9 8.2l5.2 1.3a3.2 3.2 0 0 1 2.3 3v1H5.7A3.7 3.7 0 0 1 2 15.3V8.5A3 3 0 0 1 4 5.5z"/>'
            + '<path d="M8.5 11.5 10 9.8M11.3 13l1.5-1.7M4 16h16"/>',
        glasses:
            '<circle cx="7" cy="13" r="4"/>'
            + '<circle cx="17" cy="13" r="4"/>'
            + '<path d="M11 13h2M3 11 2 8M21 11l1-3"/>',
        gem:
            '<path d="m4 8 4-4h8l4 4-8 12z"/>'
            + '<path d="M4 8h16M8 4l4 4 4-4M8 8l4 12 4-12"/>',
        watch:
            '<circle cx="12" cy="12" r="6"/>'
            + '<path d="M9 2h6l1 4M9 22h6l1-4M12 8.5V12l2.5 1.5"/>',
        bag:
            '<path d="M5 8h14l1 13H4z"/>'
            + '<path d="M8.5 9V6.5a3.5 3.5 0 0 1 7 0V9"/>',
        house:
            '<path d="m3 11 9-8 9 8"/>'
            + '<path d="M5 10v10h14V10M9 20v-6h6v6"/>',
        sofa:
            '<path d="M5 12V8a3 3 0 0 1 3-3h8a3 3 0 0 1 3 3v4"/>'
            + '<path d="M4 11a2 2 0 0 0-2 2v5h20v-5a2 2 0 0 0-4 0v1H6v-1a2 2 0 0 0-2-2zM5 18v3M19 18v3"/>',
        utensils:
            '<path d="M6 3v8M3.5 3v5a2.5 2.5 0 0 0 5 0V3M6 11v10"/>'
            + '<path d="M16 3v18M16 3c3 2 4 5 4 8h-4"/>',
        appliance:
            '<rect x="4" y="3" width="16" height="18" rx="2"/>'
            + '<path d="M4 8h16M8 5.5h.01M11 5.5h.01"/>'
            + '<circle cx="12" cy="14.5" r="4"/>',
        bulb:
            '<path d="M9 18h6M9.5 21h5"/>'
            + '<path d="M8.5 15.5A6 6 0 1 1 15.5 15.5c-.8.6-1.2 1.3-1.3 2.5h-4.4c-.1-1.2-.5-1.9-1.3-2.5z"/>',
        wrench:
            '<path d="M14.5 6.5a5 5 0 0 0-6.8 6.8L3 18l3 3 4.7-4.7a5 5 0 0 0 6.8-6.8l-3 3-3-3z"/>',
        palette:
            '<path d="M12 3a9 9 0 1 0 0 18h1.2a1.8 1.8 0 0 0 0-3.6h-.8a1.8 1.8 0 0 1 0-3.6H15A6 6 0 0 0 12 3z"/>'
            + '<circle cx="7.5" cy="10" r=".8" fill="currentColor" stroke="none"/>'
            + '<circle cx="9.5" cy="6.8" r=".8" fill="currentColor" stroke="none"/>'
            + '<circle cx="14" cy="6.5" r=".8" fill="currentColor" stroke="none"/>',
        sparkles:
            '<path d="m12 3 1.2 3.3L16.5 7.5l-3.3 1.2L12 12l-1.2-3.3-3.3-1.2 3.3-1.2z"/>'
            + '<path d="m18.5 13 .8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8zM5 14l.8 2.2L8 17l-2.2.8L5 20l-.8-2.2L2 17l2.2-.8z"/>',
        heart:
            '<path d="M20.5 5.7a5 5 0 0 0-7.1 0L12 7.1l-1.4-1.4a5 5 0 0 0-7.1 7.1L12 21l8.5-8.2a5 5 0 0 0 0-7.1z"/>'
            + '<path d="M7 12h3l1-2 2 5 1-3h3"/>',
        flower:
            '<circle cx="12" cy="12" r="2"/>'
            + '<path d="M12 10c-4-1-5-5-2-7 3 1 4 4 2 7zM14 12c1-4 5-5 7-2-1 3-4 4-7 2zM12 14c4 1 5 5 2 7-3-1-4-4-2-7zM10 12c-1 4-5 5-7 2 1-3 4-4 7-2z"/>',
        dumbbell:
            '<path d="M3 9v6M6 7v10M18 7v10M21 9v6M6 12h12"/>',
        running:
            '<circle cx="14.5" cy="4.5" r="2"/>'
            + '<path d="m12 8 3 2 3 1M12 8l-2 5 4 2 2 5M10 13l-4 2-2 4M14 15l-4 5"/>',
        bike:
            '<circle cx="6" cy="17" r="4"/>'
            + '<circle cx="18" cy="17" r="4"/>'
            + '<path d="m6 17 4-8h4l4 8M10 9l4 8M8 6h4M14 9l2-3h3"/>',
        tent:
            '<path d="m3 20 9-16 9 16zM12 4v16M8 20l4-7 4 7"/>',
        baby:
            '<circle cx="12" cy="13" r="7"/>'
            + '<path d="M9 5c0-2 1.5-3 3-3 1.2 0 2 .8 2 1.8 0 1.4-1.2 2.2-2.5 1.7M9.5 13h.01M14.5 13h.01M10 16c1.2 1 2.8 1 4 0"/>',
        puzzle:
            '<path d="M4 4h6a2 2 0 1 0 4 0h6v6a2 2 0 1 1 0 4v6h-6a2 2 0 1 0-4 0H4v-6a2 2 0 1 0 0-4z"/>',
        book:
            '<path d="M4 4h5a3 3 0 0 1 3 3v14a3 3 0 0 0-3-3H4zM20 4h-5a3 3 0 0 0-3 3v14a3 3 0 0 1 3-3h5z"/>',
        pen:
            '<path d="m4 20 4.5-1 10-10-3.5-3.5-10 10zM13.5 6.5 17 10M4 20l1-4.5"/>',
        paw:
            '<circle cx="7" cy="8" r="2"/>'
            + '<circle cx="17" cy="8" r="2"/>'
            + '<circle cx="4.5" cy="13" r="1.8"/>'
            + '<circle cx="19.5" cy="13" r="1.8"/>'
            + '<path d="M8 19c0-3 1.8-5 4-5s4 2 4 5c0 2-1.5 3-4 3s-4-1-4-3z"/>',
        basket:
            '<path d="M3 10h18l-2 10H5zM8 10l4-7 4 7M7 14v3M12 14v3M17 14v3"/>',
        cup:
            '<path d="M5 7h12v8a5 5 0 0 1-5 5H10a5 5 0 0 1-5-5z"/>'
            + '<path d="M17 9h1.5a3 3 0 0 1 0 6H17M8 3v2M12 3v2M16 3v2"/>',
        car:
            '<path d="M5 17H3v-5l2-5h14l2 5v5h-2"/>'
            + '<path d="M5 17h14M6 12h12M7 17v2M17 17v2"/>'
            + '<circle cx="7" cy="15" r="1"/>'
            + '<circle cx="17" cy="15" r="1"/>',
        motorcycle:
            '<circle cx="6" cy="17" r="4"/>'
            + '<circle cx="18" cy="17" r="4"/>'
            + '<path d="M6 17h5l3-6h3l1 6M10 11H7l-2 3M13 8h4l2 3"/>',
        briefcase:
            '<rect x="3" y="7" width="18" height="13" rx="2"/>'
            + '<path d="M8 7V4h8v3M3 12h18M10 12v2h4v-2"/>',
        printer:
            '<path d="M6 9V3h12v6M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/>'
            + '<rect x="6" y="14" width="12" height="7"/>'
            + '<path d="M18 12h.01"/>'
    });

    const glyphByClass = Object.freeze({
        "fa-plus": "+",
        "fa-layer-group": "▦",
        "fa-eye": "◉",
        "fa-eye-slash": "◌",
        "fa-boxes-stacked": "▤",
        "fa-folder-open": "📂",
        "fa-pen": "✎",
        "fa-trash-can": "⌫",
        "fa-pen-to-square": "✎",
        "fa-magnifying-glass-chart": "⌕",
        "fa-icons": "◆",
        "fa-circle-exclamation": "!"
    });

    const iconDefinitions = new Map();
    let iconAbortController = null;
    let iconSearchTimer = null;
    let lastFocusedElement = null;

    function csrfToken() {
        return document
            .querySelector('meta[name="request-verification-token"]')
            ?.content ?? "";
    }

    async function request(url, options = {}) {
        const headers = new Headers(options.headers ?? {});
        headers.set("Accept", "application/json");

        const method = String(options.method ?? "GET").toUpperCase();
        if (method !== "GET" && method !== "HEAD") {
            headers.set("RequestVerificationToken", csrfToken());
        }

        const response = await fetch(url, {
            credentials: "same-origin",
            ...options,
            method,
            headers
        });

        const payload = await response.json().catch(() => ({
            success: false,
            message: "Phản hồi máy chủ không hợp lệ."
        }));

        if (!response.ok || payload.success === false) {
            throw new Error(
                payload.message || "Không thể hoàn tất yêu cầu.");
        }

        return payload;
    }

    function createCategoryIconSvg(key) {
        const shape = iconShapeByKey[key] ?? "folder";
        const svg = document.createElementNS(
            "http://www.w3.org/2000/svg",
            "svg");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("fill", "none");
        svg.setAttribute("stroke", "currentColor");
        svg.setAttribute("stroke-width", "1.8");
        svg.setAttribute("stroke-linecap", "round");
        svg.setAttribute("stroke-linejoin", "round");
        svg.setAttribute("focusable", "false");
        svg.setAttribute("aria-hidden", "true");
        svg.classList.add("category-mono-icon");
        svg.innerHTML = iconMarkup[shape] ?? iconMarkup.folder;
        return svg;
    }

    function renderCategoryIcon(element, key) {
        if (!element) {
            return;
        }

        [...element.classList]
            .filter(className => className.startsWith("fa-"))
            .forEach(className => element.classList.remove(className));

        element.classList.add("category-mono-icon-host");
        element.dataset.categoryIcon = key || "folder";
        element.replaceChildren(
            createCategoryIconSvg(key || "folder"));
        element.setAttribute("aria-hidden", "true");
    }

    function setGlyph(element, glyph, className = "category-ui-icon") {
        if (!element) {
            return;
        }

        element.className = className;
        element.textContent = glyph;
        element.setAttribute("aria-hidden", "true");
    }

    function renderStaticFallbacks() {
        page.querySelectorAll(".category-name-cell").forEach(cell => {
            const keyText = cell.querySelector(
                ".admin-cell-secondary")?.textContent ?? "";
            const key = keyText.replace(/^\s*Icon:\s*/i, "").trim();
            const target = cell.querySelector(".category-icon");
            if (target) {
                renderCategoryIcon(target, key);
            }
        });

        page.querySelectorAll(".category-row-actions").forEach(actions => {
            const edit = actions.querySelector("[data-category-open]");
            const remove = actions.querySelector("[data-category-delete]");

            if (edit) {
                edit.replaceChildren();
                const glyph = document.createElement("span");
                glyph.className = "category-ui-icon";
                glyph.textContent = "✎";
                glyph.setAttribute("aria-hidden", "true");
                const label = document.createElement("span");
                label.textContent = "Sửa";
                edit.append(glyph, label);
            }

            if (remove) {
                remove.replaceChildren();
                const glyph = document.createElement("span");
                glyph.className = "category-ui-icon";
                glyph.textContent = "⌫";
                glyph.setAttribute("aria-hidden", "true");
                const label = document.createElement("span");
                label.textContent = "Xóa";
                remove.append(glyph, label);
            }
        });

        page.querySelectorAll("i[class*='fa-']").forEach(icon => {
            const key = [...icon.classList].find(
                className => glyphByClass[className]);
            if (key) {
                setGlyph(icon, glyphByClass[key]);
            }
        });

        modalElement.querySelectorAll("i[class*='fa-']").forEach(icon => {
            const key = [...icon.classList].find(
                className => glyphByClass[className]);
            if (key) {
                setGlyph(icon, glyphByClass[key]);
            }
        });
    }

    function showError(message) {
        if (!errorBox) {
            window.alert(message);
            return;
        }

        if (errorMessage) {
            errorMessage.textContent = message;
        } else {
            errorBox.textContent = message;
        }

        errorBox.classList.remove("d-none");
    }

    function clearError() {
        if (!errorBox) {
            return;
        }

        if (errorMessage) {
            errorMessage.textContent = "";
        } else {
            errorBox.textContent = "";
        }

        errorBox.classList.add("d-none");
    }

    function openModal() {
        lastFocusedElement = document.activeElement;
        modalElement.hidden = false;
        modalElement.classList.add("is-open");
        modalElement.setAttribute("aria-hidden", "false");
        document.body.classList.add("category-modal-open");

        window.requestAnimationFrame(() => {
            fields.name.focus();
        });
    }

    function closeModal() {
        iconAbortController?.abort();
        modalElement.classList.remove("is-open");
        modalElement.setAttribute("aria-hidden", "true");
        modalElement.hidden = true;
        document.body.classList.remove("category-modal-open");
        clearError();

        if (lastFocusedElement instanceof HTMLElement) {
            lastFocusedElement.focus();
        }
    }

    function slugify(value) {
        return value
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .replace(/đ/g, "d")
            .replace(/Đ/g, "D")
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, "-")
            .replace(/^-+|-+$/g, "");
    }

    function updateMetaDescriptionCount() {
        if (metaDescriptionCount) {
            metaDescriptionCount.textContent = String(
                fields.metaDescription.value.length);
        }
    }

    function selectIcon(key, cssClass, label) {
        fields.iconKey.value = key;
        renderCategoryIcon(selectedIconPreview, key);
        selectedIconLabel.textContent = label;
        selectedIconKey.textContent = key;

        iconGrid
            .querySelectorAll("[data-icon-key]")
            .forEach(button => {
                const selected = button.dataset.iconKey === key;
                button.classList.toggle("is-selected", selected);
                button.setAttribute("aria-selected", String(selected));
            });
    }

    function populateGroups(groups) {
        if (!Array.isArray(groups) || iconGroup.options.length > 1) {
            return;
        }

        const fragment = document.createDocumentFragment();
        for (const group of groups) {
            const option = document.createElement("option");
            option.value = group.key;
            option.textContent = group.label;
            fragment.append(option);
        }

        iconGroup.append(fragment);
    }

    function renderIcons(icons) {
        iconGrid.replaceChildren();
        iconDefinitions.clear();

        if (!Array.isArray(icons) || icons.length === 0) {
            const empty = document.createElement("div");
            empty.className = "category-icon-empty";

            const symbol = document.createElement("span");
            symbol.className = "category-ui-icon";
            symbol.textContent = "⌕";
            symbol.setAttribute("aria-hidden", "true");

            const titleElement = document.createElement("strong");
            titleElement.textContent = "Không tìm thấy biểu tượng phù hợp";

            const description = document.createElement("span");
            description.textContent = "Hãy thử từ khóa hoặc nhóm khác.";

            empty.append(symbol, titleElement, description);
            iconGrid.append(empty);
            iconStatus.textContent = "Không có biểu tượng phù hợp.";
            return;
        }

        const fragment = document.createDocumentFragment();

        for (const icon of icons) {
            iconDefinitions.set(icon.key, icon);

            const button = document.createElement("button");
            button.type = "button";
            button.className = "category-icon-option";
            button.dataset.iconKey = icon.key;
            button.dataset.iconCssClass = icon.cssClass;
            button.dataset.iconLabel = icon.label;
            button.setAttribute("role", "option");

            const selected = fields.iconKey.value === icon.key;
            button.classList.toggle("is-selected", selected);
            button.setAttribute("aria-selected", String(selected));
            button.setAttribute(
                "aria-label",
                `Chọn biểu tượng ${icon.label}`);

            const preview = document.createElement("span");
            preview.className = "category-icon-option__preview";
            renderCategoryIcon(preview, icon.key);

            const copy = document.createElement("span");
            copy.className = "category-icon-option__copy";

            const label = document.createElement("strong");
            label.textContent = icon.label;

            const group = document.createElement("small");
            group.textContent = icon.groupLabel;

            copy.append(label, group);
            button.append(preview, copy);
            fragment.append(button);
        }

        iconGrid.append(fragment);
        iconStatus.textContent = `${icons.length} biểu tượng phù hợp.`;

        const selectedDefinition = iconDefinitions.get(
            fields.iconKey.value);
        if (selectedDefinition) {
            selectIcon(
                selectedDefinition.key,
                selectedDefinition.cssClass,
                selectedDefinition.label);
        }
    }

    async function loadIconCatalog() {
        iconAbortController?.abort();
        iconAbortController = new AbortController();

        iconGrid.setAttribute("aria-busy", "true");
        iconStatus.textContent = "Đang tải thư viện biểu tượng…";

        const url = new URL(
            page.dataset.iconCatalogUrl,
            window.location.origin);
        const query = iconSearch.value.trim();
        const group = iconGroup.value;

        if (query) {
            url.searchParams.set("query", query);
        }

        if (group) {
            url.searchParams.set("group", group);
        }

        url.searchParams.set("limit", "60");

        try {
            const payload = await request(url.toString(), {
                signal: iconAbortController.signal
            });

            populateGroups(payload.data?.groups ?? []);
            renderIcons(payload.data?.items ?? []);
        } catch (error) {
            if (error instanceof DOMException && error.name === "AbortError") {
                return;
            }

            iconGrid.replaceChildren();
            iconStatus.textContent = error.message;
            showError(error.message);
        } finally {
            iconGrid.removeAttribute("aria-busy");
        }
    }

    function resetForm() {
        form.reset();
        fields.id.value = "0";
        fields.displayOrder.value = "0";
        fields.isVisible.checked = true;
        delete fields.slug.dataset.edited;

        [...fields.parentId.options].forEach(option => {
            option.disabled = false;
        });

        iconSearch.value = "";
        iconGroup.value = "";
        title.textContent = "Thêm danh mục";
        updateMetaDescriptionCount();
        clearError();
        selectIcon("folder", "fa-solid fa-folder", "Danh mục chung");
    }

    async function openEditor(id) {
        resetForm();
        openModal();

        try {
            const categoryPromise = id
                ? request(
                    `${page.dataset.getUrl}?id=${encodeURIComponent(id)}`)
                : Promise.resolve(null);

            const [, categoryPayload] = await Promise.all([
                loadIconCatalog(),
                categoryPromise
            ]);

            if (!categoryPayload) {
                return;
            }

            const category = categoryPayload.data;
            fields.id.value = String(category.id);
            fields.name.value = category.name;
            fields.parentId.value = category.parentId ?? "";
            fields.slug.value = category.slug;
            fields.slug.dataset.edited = "true";
            fields.displayOrder.value = String(
                category.displayOrder ?? 0);
            fields.isVisible.checked = category.isVisible === true;
            fields.metaTitle.value = category.metaTitle ?? "";
            fields.metaDescription.value = category.metaDescription ?? "";
            updateMetaDescriptionCount();

            [...fields.parentId.options].forEach(option => {
                option.disabled = option.value === String(category.id);
            });

            const definition = iconDefinitions.get(category.iconKey);
            if (definition) {
                selectIcon(
                    definition.key,
                    definition.cssClass,
                    definition.label);
            } else {
                selectIcon(
                    category.iconKey || "folder",
                    "",
                    category.iconKey || "Danh mục chung");
            }

            title.textContent = "Cập nhật danh mục";
        } catch (error) {
            showError(error.message);
        }
    }

    fields.name.addEventListener("input", () => {
        if (fields.id.value === "0" || !fields.slug.dataset.edited) {
            fields.slug.value = slugify(fields.name.value);
        }
    });

    fields.slug.addEventListener("input", () => {
        fields.slug.dataset.edited = "true";
    });

    fields.metaDescription.addEventListener(
        "input",
        updateMetaDescriptionCount);

    iconSearch.addEventListener("input", () => {
        window.clearTimeout(iconSearchTimer);
        iconSearchTimer = window.setTimeout(loadIconCatalog, 250);
    });

    iconGroup.addEventListener("change", loadIconCatalog);

    iconGrid.addEventListener("click", event => {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const button = target.closest("[data-icon-key]");
        if (!button) {
            return;
        }

        selectIcon(
            button.dataset.iconKey,
            button.dataset.iconCssClass,
            button.dataset.iconLabel);
    });

    document.addEventListener("click", async event => {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const closeButton = target.closest('[data-bs-dismiss="modal"]');
        if (closeButton && modalElement.contains(closeButton)) {
            closeModal();
            return;
        }

        const openButton = target.closest("[data-category-open]");
        if (openButton) {
            const id = Number(openButton.dataset.categoryOpen || 0);
            await openEditor(id);
            return;
        }

        const deleteButton = target.closest("[data-category-delete]");
        if (!deleteButton) {
            return;
        }

        const id = deleteButton.dataset.categoryDelete;
        const name = deleteButton.dataset.categoryName || "danh mục này";

        if (!window.confirm(
            `Xóa ${name}? Chỉ danh mục không có sản phẩm hoặc danh mục con mới được xóa.`)) {
            return;
        }

        deleteButton.disabled = true;

        try {
            await request(
                `${page.dataset.deleteUrl}?id=${encodeURIComponent(id)}`,
                { method: "POST" });
            window.location.reload();
        } catch (error) {
            deleteButton.disabled = false;
            window.alert(error.message);
        }
    });

    modalElement.addEventListener("click", event => {
        if (event.target === modalElement) {
            closeModal();
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && modalElement.classList.contains("is-open")) {
            event.preventDefault();
            closeModal();
        }
    });

    form.addEventListener("submit", async event => {
        event.preventDefault();
        clearError();

        if (!form.reportValidity()) {
            return;
        }

        if (!fields.iconKey.value) {
            showError("Vui lòng chọn biểu tượng cho danh mục.");
            return;
        }

        submitButton.disabled = true;
        const originalLabel = submitButton.textContent;
        submitButton.textContent = "Đang lưu…";

        try {
            await request(page.dataset.saveUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    id: Number(fields.id.value || 0),
                    name: fields.name.value,
                    parentId: fields.parentId.value
                        ? Number(fields.parentId.value)
                        : null,
                    slug: fields.slug.value,
                    iconKey: fields.iconKey.value,
                    displayOrder: Number(
                        fields.displayOrder.value || 0),
                    isVisible: fields.isVisible.checked,
                    metaTitle: fields.metaTitle.value || null,
                    metaDescription: fields.metaDescription.value || null
                })
            });

            closeModal();
            window.location.reload();
        } catch (error) {
            showError(error.message);
        } finally {
            submitButton.disabled = false;
            submitButton.textContent = originalLabel;
        }
    });

    modalElement.hidden = true;
    modalElement.setAttribute("aria-hidden", "true");
    renderStaticFallbacks();
})();
