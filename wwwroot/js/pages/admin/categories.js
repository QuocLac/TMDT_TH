(() => {
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
        metaTitle: document.getElementById("MetaTitle"),
        metaDescription: document.getElementById("MetaDescription")
    };
    const title = document.getElementById("categoryModalLabel");
    const errorBox = document.getElementById("categoryFormError");
    const submitButton = document.getElementById("categorySubmitButton");

    function csrfToken() {
        return document.querySelector('meta[name="request-verification-token"]')?.content ?? "";
    }

    async function request(url, options = {}) {
        const headers = new Headers(options.headers ?? {});
        headers.set("RequestVerificationToken", csrfToken());
        const response = await fetch(url, { ...options, headers });
        const payload = await response.json().catch(() => ({ success: false, message: "Phản hồi máy chủ không hợp lệ." }));
        if (!response.ok || payload.success === false) {
            throw new Error(payload.message || "Không thể hoàn tất yêu cầu.");
        }
        return payload;
    }

    function showError(message) {
        errorBox.textContent = message;
        errorBox.classList.remove("d-none");
    }

    function clearError() {
        errorBox.textContent = "";
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

    function resetForm() {
        form.reset();
        fields.id.value = "0";
        [...fields.parentId.options].forEach(option => option.disabled = false);
        title.textContent = "Thêm danh mục";
        clearError();
    }

    async function openEditor(id) {
        resetForm();
        if (!id) {
            modal.show();
            fields.name.focus();
            return;
        }

        try {
            const payload = await request(`${page.dataset.getUrl}?id=${encodeURIComponent(id)}`);
            const category = payload.data;
            fields.id.value = category.id;
            fields.name.value = category.name;
            fields.parentId.value = category.parentId ?? "";
            fields.slug.value = category.slug;
            fields.metaTitle.value = category.metaTitle ?? "";
            fields.metaDescription.value = category.metaDescription ?? "";
            const selfOption = fields.parentId.querySelector(`option[value="${CSS.escape(String(category.id))}"]`);
            if (selfOption) {
                selfOption.disabled = true;
            }
            title.textContent = "Cập nhật danh mục";
            modal.show();
        } catch (error) {
            window.alert(error.message);
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
        if (!window.confirm(`Xóa ${name}? Chỉ danh mục không có sản phẩm hoặc danh mục con mới được xóa.`)) {
            return;
        }

        deleteButton.disabled = true;
        try {
            await request(`${page.dataset.deleteUrl}?id=${encodeURIComponent(id)}`, { method: "POST" });
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

        submitButton.disabled = true;
        const originalLabel = submitButton.textContent;
        submitButton.textContent = "Đang lưu…";

        try {
            await request(page.dataset.saveUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    id: Number(fields.id.value || 0),
                    name: fields.name.value,
                    parentId: fields.parentId.value ? Number(fields.parentId.value) : null,
                    slug: fields.slug.value,
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
})();
