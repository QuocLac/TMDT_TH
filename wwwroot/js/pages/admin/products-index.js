(() => {
    "use strict";

    const root = document.querySelector("[data-product-index]");
    const http = window.FastBuyHttp;
    if (!root || !http) return;

    const feedback = root.querySelector("[data-page-feedback]");
    const dialog = root.querySelector("[data-variant-dialog]");
    const form = root.querySelector("[data-variant-form]");
    const dialogTitle = root.querySelector("[data-variant-dialog-title]");
    const currency = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 });

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

    function openVariantDialog(productId, row = null) {
        form.reset();
        setField("ProductId", productId);
        setField("IsActive", true);

        if (row) {
            dialogTitle.textContent = `Chỉnh sửa ${row.dataset.sku}`;
            setField("VariantId", row.dataset.variantId);
            setField("RowVersion", row.dataset.rowVersion);
            setField("Color", row.dataset.color);
            setField("Size", row.dataset.size);
            setField("Price", row.dataset.price);
            setField("StockQuantity", row.dataset.stock);
            setField("IsActive", row.dataset.active);
        } else {
            dialogTitle.textContent = "Thêm biến thể";
            setField("VariantId", 0);
            setField("RowVersion", "");
        }

        dialog.showModal();
    }

    root.addEventListener("click", async (event) => {
        const productCard = event.target.closest("[data-product-id]");
        const variantRow = event.target.closest("[data-variant-row]");

        if (event.target.closest("[data-add-variant]")) {
            openVariantDialog(Number(productCard.dataset.productId));
            return;
        }

        if (event.target.closest("[data-edit-variant]")) {
            openVariantDialog(Number(variantRow.dataset.productId), variantRow);
            return;
        }

        const toggleProductButton = event.target.closest("[data-toggle-product]");
        if (toggleProductButton) {
            toggleProductButton.disabled = true;
            try {
                const formData = new FormData();
                formData.append("id", productCard.dataset.productId);
                const result = await http.postForm("/Admin/Products/ToggleStatus", formData);
                if (!result.success) throw new Error(result.message ?? "Không thể đổi trạng thái.");
                setFeedback(result.message, "success");
                window.setTimeout(() => window.location.reload(), 350);
            } catch (error) {
                setFeedback(error.message, "error");
                toggleProductButton.disabled = false;
            }
            return;
        }

        const toggleVariantButton = event.target.closest("[data-toggle-variant]");
        if (toggleVariantButton) {
            toggleVariantButton.disabled = true;
            try {
                const result = await http.postJson("/Admin/Products/ToggleVariantStatus", {
                    variantId: Number(variantRow.dataset.variantId),
                    rowVersion: variantRow.dataset.rowVersion
                });
                if (!result.success) throw new Error(result.message ?? "Không thể đổi trạng thái biến thể.");
                variantRow.dataset.rowVersion = result.rowVersion;
                variantRow.dataset.active = String(result.isActive);
                const badge = variantRow.querySelector("[data-variant-status]");
                badge.textContent = result.isActive ? "Đang bán" : "Tạm ẩn";
                badge.classList.toggle("status-badge--active", result.isActive);
                badge.classList.toggle("status-badge--inactive", !result.isActive);
                toggleVariantButton.textContent = result.isActive ? "Ẩn" : "Hiện";
                setFeedback("Đã cập nhật trạng thái biến thể.", "success");
            } catch (error) {
                setFeedback(error.message, "error");
            } finally {
                toggleVariantButton.disabled = false;
            }
            return;
        }

        const updatePriceButton = event.target.closest("[data-update-price]");
        if (updatePriceButton) {
            const input = variantRow.querySelector("[data-list-price]");
            const newPrice = Number(input.value);
            if (!Number.isFinite(newPrice) || newPrice <= 0) {
                setFeedback("Giá niêm yết phải lớn hơn 0.", "error");
                return;
            }

            updatePriceButton.disabled = true;
            try {
                const result = await http.postJson("/Admin/Products/UpdateVariantPrice", {
                    variantId: Number(variantRow.dataset.variantId),
                    newPrice,
                    note: "Cập nhật nhanh từ danh sách sản phẩm",
                    rowVersion: variantRow.dataset.rowVersion
                });
                if (!result.success) throw new Error(result.message ?? "Không thể cập nhật giá.");
                variantRow.dataset.price = String(result.listPrice);
                variantRow.dataset.currentPrice = String(result.currentPrice);
                variantRow.dataset.rowVersion = result.rowVersion;
                const effectiveCell = variantRow.querySelector("[data-current-price-cell]");
                effectiveCell.replaceChildren();
                const price = document.createElement("strong");
                price.textContent = currency.format(result.currentPrice);
                effectiveCell.append(price);
                if (Number(result.currentPrice) < Number(result.listPrice)) {
                    const note = document.createElement("small");
                    note.className = "sale-note";
                    note.textContent = "Đang có chiến dịch";
                    effectiveCell.append(note);
                }
                setFeedback(result.message, "success");
            } catch (error) {
                input.value = variantRow.dataset.price;
                setFeedback(error.message, "error");
            } finally {
                updatePriceButton.disabled = false;
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
            if (!form.querySelector('[data-field="IsActive"]').checked) {
                formData.set("IsActive", "false");
            }
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
})();
