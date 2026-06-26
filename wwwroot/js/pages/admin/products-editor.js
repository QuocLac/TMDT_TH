(() => {
    "use strict";

    const http = window.FastBuyHttp;

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

    function initializeCreate(root) {
        const list = root.querySelector("[data-create-variant-list]");
        const template = root.querySelector("[data-create-variant-template]");
        const nameInput = root.querySelector("[data-product-name]");
        const slugInput = root.querySelector("[data-product-slug]");
        let slugWasEdited = Boolean(slugInput.value);

        function reindex() {
            [...list.querySelectorAll("[data-create-variant-row]")].forEach((row, index) => {
                row.querySelector(".variant-create-row__number").textContent = String(index + 1);
                row.querySelectorAll("[data-variant-property]").forEach((field) => {
                    const property = field.dataset.variantProperty;
                    field.name = `Variants[${index}].${property}`;
                    field.id = `Variants_${index}__${property}`;
                    if (field.type === "checkbox") field.value = "true";
                });
                row.querySelectorAll("[data-variant-hidden]").forEach((field) => {
                    field.name = `Variants[${index}].${field.dataset.variantHidden}`;
                });
            });
        }

        root.querySelector("[data-add-create-variant]").addEventListener("click", () => {
            list.append(template.content.cloneNode(true));
            reindex();
        });

        list.addEventListener("click", (event) => {
            const removeButton = event.target.closest("[data-remove-create-variant]");
            if (!removeButton) return;
            if (list.querySelectorAll("[data-create-variant-row]").length <= 1) return;
            removeButton.closest("[data-create-variant-row]").remove();
            reindex();
        });

        slugInput.addEventListener("input", () => { slugWasEdited = true; });
        nameInput.addEventListener("input", () => {
            if (!slugWasEdited) slugInput.value = slugify(nameInput.value);
        });
        reindex();
    }

    function initializeEdit(root) {
        if (!http) return;
        const feedback = root.querySelector("[data-page-feedback]");
        const dialog = root.querySelector("[data-variant-dialog]");
        const form = root.querySelector("[data-variant-form]");
        const title = root.querySelector("[data-variant-dialog-title]");

        function setFeedback(message, type = "") {
            feedback.textContent = message ?? "";
            feedback.classList.remove("is-error", "is-success");
            if (type) feedback.classList.add(`is-${type}`);
        }

        function setField(name, value) {
            const field = form.querySelector(`[data-field="${name}"]`);
            if (!field) return;
            if (field.type === "checkbox") field.checked = value === true || value === "true";
            else field.value = value ?? "";
        }

        function openDialog(row) {
            form.reset();
            setField("ProductId", root.dataset.productId);
            if (row) {
                title.textContent = `Chỉnh sửa ${row.dataset.sku}`;
                setField("VariantId", row.dataset.variantId);
                setField("RowVersion", row.dataset.rowVersion);
                setField("Color", row.dataset.color);
                setField("Size", row.dataset.size);
                setField("Price", row.dataset.price);
                setField("StockQuantity", row.dataset.stock);
                setField("IsActive", row.dataset.active);
            } else {
                title.textContent = "Thêm biến thể";
                setField("VariantId", 0);
                setField("RowVersion", "");
                setField("IsActive", true);
            }
            dialog.showModal();
        }

        root.addEventListener("click", async (event) => {
            const openButton = event.target.closest("[data-open-variant]");
            if (openButton) {
                openDialog(openButton.closest("[data-variant-row]"));
                return;
            }

            const deleteButton = event.target.closest("[data-delete-gallery-image]");
            if (deleteButton) {
                const figure = deleteButton.closest("[data-gallery-image]");
                if (!window.confirm("Xóa ảnh này khỏi thư viện sản phẩm?")) return;
                deleteButton.disabled = true;
                try {
                    const formData = new FormData();
                    formData.append("imageId", figure.dataset.imageId);
                    const result = await http.postForm("/Admin/Products/DeleteProductImage", formData);
                    if (!result.success) throw new Error(result.message ?? "Không thể xóa ảnh.");
                    figure.remove();
                    setFeedback(result.message, "success");
                } catch (error) {
                    setFeedback(error.message, "error");
                    deleteButton.disabled = false;
                }
            }
        });

        root.querySelectorAll("[data-close-variant]").forEach((button) => {
            button.addEventListener("click", () => dialog.close());
        });

        form.addEventListener("submit", async (event) => {
            event.preventDefault();
            const submitButton = form.querySelector('button[type="submit"]');
            submitButton.disabled = true;
            try {
                const formData = new FormData(form);
                if (!form.querySelector('[data-field="IsActive"]').checked) formData.set("IsActive", "false");
                const result = await http.postForm("/Admin/Products/SaveQuickVariant", formData);
                if (!result.success) throw new Error(result.message ?? "Không thể lưu biến thể.");
                dialog.close();
                setFeedback(result.message, "success");
                window.setTimeout(() => window.location.reload(), 350);
            } catch (error) {
                setFeedback(error.message, "error");
                submitButton.disabled = false;
            }
        });
    }

    const createRoot = document.querySelector("[data-product-create]");
    if (createRoot) initializeCreate(createRoot);

    const editRoot = document.querySelector("[data-product-edit]");
    if (editRoot) initializeEdit(editRoot);
})();
