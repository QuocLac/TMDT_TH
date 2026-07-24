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

    const glyphByKey = Object.freeze({
        folder: "📁",
        catalog: "▦",
        tags: "🏷",
        gift: "🎁",
        electronics: "▣",
        phone: "📱",
        laptop: "💻",
        tablet: "▤",
        television: "▭",
        camera: "📷",
        audio: "🎧",
        gaming: "🎮",
        "computer-accessories": "⌨",
        fashion: "👕",
        shoes: "👟",
        eyewear: "👓",
        jewelry: "◆",
        watch: "◷",
        bag: "👜",
        home: "⌂",
        furniture: "▰",
        kitchen: "♨",
        appliances: "◉",
        lighting: "💡",
        tools: "🛠",
        decor: "🎨",
        beauty: "✨",
        health: "♥",
        spa: "❀",
        fitness: "🏋",
        sports: "🏃",
        bicycle: "🚲",
        outdoor: "⛺",
        baby: "👶",
        toys: "🧩",
        books: "📖",
        stationery: "✎",
        pets: "🐾",
        food: "🍴",
        grocery: "🧺",
        coffee: "☕",
        car: "🚗",
        motorcycle: "🏍",
        office: "💼",
        printing: "▣"
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

    function glyphFor(key, cssClass = "") {
        if (key && glyphByKey[key]) {
            return glyphByKey[key];
        }

        for (const className of String(cssClass).split(/\s+/)) {
            if (glyphByClass[className]) {
                return glyphByClass[className];
            }
        }

        return "•";
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
                target.replaceChildren();
                target.textContent = glyphFor(key);
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
        setGlyph(
            selectedIconPreview,
            glyphFor(key, cssClass),
            "category-ui-icon");
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
            preview.textContent = glyphFor(icon.key, icon.cssClass);
            preview.setAttribute("aria-hidden", "true");

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
