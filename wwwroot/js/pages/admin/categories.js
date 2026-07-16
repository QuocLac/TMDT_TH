(() => {
    "use strict";

    const page = document.querySelector("[data-category-page]");
    const modalElement = document.getElementById("categoryModal");
    const form = document.getElementById("categoryForm");

    if (!page || !modalElement || !form || typeof bootstrap === "undefined") {
        return;
    }

    const modal = new bootstrap.Modal(modalElement);
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

    const title = document.getElementById("categoryModalLabel");
    const errorBox = document.getElementById("categoryFormError");
    const errorMessage = errorBox?.querySelector("[data-category-error-message]");
    const submitButton = document.getElementById("categorySubmitButton");
    const iconSearch = document.getElementById("CategoryIconSearch");
    const iconGroup = document.getElementById("CategoryIconGroup");
    const iconGrid = modalElement.querySelector("[data-icon-grid]");
    const iconStatus = modalElement.querySelector("[data-icon-status]");
    const selectedIconPreview = modalElement.querySelector("[data-selected-icon-preview]");
    const selectedIconLabel = modalElement.querySelector("[data-selected-icon-label]");
    const selectedIconKey = modalElement.querySelector("[data-selected-icon-key]");
    const metaDescriptionCount = modalElement.querySelector("[data-meta-description-count]");

    const iconDefinitions = new Map();
    let iconAbortController = null;
    let iconSearchTimer = null;

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
            throw new Error(payload.message || "Không thể hoàn tất yêu cầu.");
        }

        return payload;
    }

    function showError(message) {
        if (errorMessage) {
            errorMessage.textContent = message;
        } else {
            errorBox.textContent = message;
        }

        errorBox.classList.remove("d-none");
    }

    function clearError() {
        if (errorMessage) {
            errorMessage.textContent = "";
        } else {
            errorBox.textContent = "";
        }

        errorBox.classList.add("d-none");
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
        selectedIconPreview.className = cssClass;
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
            empty.innerHTML =
                '<i class="fa-solid fa-magnifying-glass" aria-hidden="true"></i>'
                + "<strong>Không tìm thấy biểu tượng phù hợp</strong>"
                + "<span>Hãy thử từ khóa hoặc nhóm khác.</span>";
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

            const iconElement = document.createElement("i");
            iconElement.className = icon.cssClass;
            iconElement.setAttribute("aria-hidden", "true");
            preview.append(iconElement);

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

        const selectedDefinition = iconDefinitions.get(fields.iconKey.value);
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
            if (error.name === "AbortError") {
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
        modal.show();

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
            fields.displayOrder.value = String(category.displayOrder ?? 0);
            fields.isVisible.checked = category.isVisible === true;
            fields.metaTitle.value = category.metaTitle ?? "";
            fields.metaDescription.value = category.metaDescription ?? "";
            updateMetaDescriptionCount();

            const selfOption = fields.parentId.querySelector(
                `option[value="${CSS.escape(String(category.id))}"]`);

            if (selfOption) {
                selfOption.disabled = true;
            }

            const definition = iconDefinitions.get(category.iconKey);
            if (definition) {
                selectIcon(
                    definition.key,
                    definition.cssClass,
                    definition.label);
            } else {
                selectIcon(
                    "folder",
                    "fa-solid fa-folder",
                    "Danh mục chung");
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
        const button = event.target.closest("[data-icon-key]");
        if (!button) {
            return;
        }

        selectIcon(
            button.dataset.iconKey,
            button.dataset.iconCssClass,
            button.dataset.iconLabel);
    });

    document.addEventListener("click", async event => {
        const openButton = event.target.closest("[data-category-open]");
        if (openButton) {
            const id = Number(openButton.dataset.categoryOpen || 0);
            await openEditor(id);
            return;
        }

        const deleteButton = event.target.closest("[data-category-delete]");
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
                    displayOrder: Number(fields.displayOrder.value || 0),
                    isVisible: fields.isVisible.checked,
                    metaTitle: fields.metaTitle.value || null,
                    metaDescription: fields.metaDescription.value || null
                })
            });

            modal.hide();
            window.location.reload();
        } catch (error) {
            showError(error.message);
        } finally {
            submitButton.disabled = false;
            submitButton.textContent = originalLabel;
        }
    });

    modalElement.addEventListener("shown.bs.modal", () => {
        fields.name.focus();
    });

    modalElement.addEventListener("hidden.bs.modal", () => {
        iconAbortController?.abort();
        clearError();
    });
})();
