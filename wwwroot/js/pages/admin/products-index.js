(() => {
    "use strict";

    const root = document.querySelector("[data-product-index]");
    const http = window.FastBuyHttp;
    if (!root || !http) return;

    const feedback = root.querySelector("[data-page-feedback]");
    const dialog = root.querySelector("[data-variant-dialog]");
    const form = root.querySelector("[data-variant-form]");
    const dialogTitle = root.querySelector("[data-variant-dialog-title]");
    const currency = new Intl.NumberFormat("vi-VN", {
        style: "currency",
        currency: "VND",
        maximumFractionDigits: 0
    });

    function commercialize(message) {
        return String(message ?? "")
            .replaceAll("biến thể", "lựa chọn bán")
            .replaceAll("Biến thể", "Lựa chọn bán")
            .replaceAll("chiến dịch", "lịch giá")
            .replaceAll("Chiến dịch", "Lịch giá");
    }

    function setFeedback(message, type = "") {
        feedback.textContent = commercialize(message);
        feedback.classList.remove("is-error", "is-success");
        if (type) feedback.classList.add(`is-${type}`);
    }

    function setField(name, value) {
        const field = form.querySelector(`[data-field="${name}"]`);
        if (!field) return;

        if (field.type === "checkbox") {
            field.checked = value === true || value === "true";
        } else {
            field.value = value ?? "";
        }
    }

    function openSelectionDialog(productId, row = null) {
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
            dialogTitle.textContent = "Thêm lựa chọn bán";
            setField("VariantId", 0);
            setField("RowVersion", "");
        }

        dialog.showModal();
    }

    root.addEventListener("click", async (event) => {
        const productCard = event.target.closest("[data-product-id]");
        const selectionRow = event.target.closest("[data-variant-row]");

        if (event.target.closest("[data-add-variant]")) {
            openSelectionDialog(Number(productCard.dataset.productId));
            return;
        }

        if (event.target.closest("[data-edit-variant]")) {
            openSelectionDialog(
                Number(selectionRow.dataset.productId),
                selectionRow
            );
            return;
        }

        const toggleProductButton = event.target.closest("[data-toggle-product]");
        if (toggleProductButton) {
            toggleProductButton.disabled = true;

            try {
                const formData = new FormData();
                formData.append("id", productCard.dataset.productId);

                const result = await http.postForm(
                    "/Admin/Products/ToggleStatus",
                    formData
                );

                if (!result.success) {
                    throw new Error(result.message ?? "Không thể đổi trạng thái.");
                }

                setFeedback(result.message, "success");
                window.setTimeout(() => window.location.reload(), 350);
            } catch (error) {
                setFeedback(error.message, "error");
                toggleProductButton.disabled = false;
            }

            return;
        }

        const toggleSelectionButton = event.target.closest("[data-toggle-variant]");
        if (toggleSelectionButton) {
            toggleSelectionButton.disabled = true;

            try {
                const result = await http.postJson(
                    "/Admin/Products/ToggleVariantStatus",
                    {
                        variantId: Number(selectionRow.dataset.variantId),
                        rowVersion: selectionRow.dataset.rowVersion
                    }
                );

                if (!result.success) {
                    throw new Error(
                        result.message ?? "Không thể đổi trạng thái lựa chọn bán."
                    );
                }

                selectionRow.dataset.rowVersion = result.rowVersion;
                selectionRow.dataset.active = String(result.isActive);

                const badge = selectionRow.querySelector("[data-variant-status]");
                badge.textContent = result.isActive
                    ? "Đang bán"
                    : "Tạm ngừng bán";
                badge.classList.toggle("status-badge--active", result.isActive);
                badge.classList.toggle("status-badge--inactive", !result.isActive);

                toggleSelectionButton.textContent = result.isActive
                    ? "Ngừng bán"
                    : "Bán lại";

                setFeedback(
                    "Đã cập nhật trạng thái lựa chọn bán.",
                    "success"
                );
            } catch (error) {
                setFeedback(error.message, "error");
            } finally {
                toggleSelectionButton.disabled = false;
            }

            return;
        }

        const updatePriceButton = event.target.closest("[data-update-price]");
        if (!updatePriceButton) return;

        const input = selectionRow.querySelector("[data-list-price]");
        const newPrice = Number(input.value);

        if (!Number.isFinite(newPrice) || newPrice <= 0) {
            setFeedback("Giá niêm yết phải lớn hơn 0.", "error");
            return;
        }

        updatePriceButton.disabled = true;

        try {
            const result = await http.postJson(
                "/Admin/Products/UpdateVariantPrice",
                {
                    variantId: Number(selectionRow.dataset.variantId),
                    newPrice,
                    note: "Cập nhật nhanh từ danh sách sản phẩm",
                    rowVersion: selectionRow.dataset.rowVersion
                }
            );

            if (!result.success) {
                throw new Error(result.message ?? "Không thể cập nhật giá.");
            }

            selectionRow.dataset.price = String(result.listPrice);
            selectionRow.dataset.currentPrice = String(result.currentPrice);
            selectionRow.dataset.rowVersion = result.rowVersion;

            const effectiveCell =
                selectionRow.querySelector("[data-current-price-cell]");
            effectiveCell.replaceChildren();

            const price = document.createElement("strong");
            price.textContent = currency.format(result.currentPrice);
            effectiveCell.append(price);

            if (Number(result.currentPrice) !== Number(result.listPrice)) {
                const note = document.createElement("small");
                note.className = "sale-note";
                note.textContent = "Đang áp dụng giá theo lịch";
                effectiveCell.append(note);
            }

            setFeedback(result.message, "success");
        } catch (error) {
            input.value = selectionRow.dataset.price;
            setFeedback(error.message, "error");
        } finally {
            updatePriceButton.disabled = false;
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

            const result = await http.postForm(
                "/Admin/Products/SaveQuickVariant",
                formData
            );

            if (!result.success) {
                throw new Error(
                    result.message ?? "Không thể lưu lựa chọn bán."
                );
            }

            dialog.close();
            setFeedback(result.message, "success");
            window.setTimeout(() => window.location.reload(), 350);
        } catch (error) {
            setFeedback(error.message, "error");
            submitButton.disabled = false;
        }
    });
})();
