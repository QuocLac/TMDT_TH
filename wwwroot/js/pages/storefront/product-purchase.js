(() => {
    "use strict";

    const panel = document.querySelector("[data-product-purchase]");
    const cart = window.FastBuyCart;

    if (!panel || !cart) return;

    const productId = Number.parseInt(panel.dataset.productId ?? "", 10);
    const loading = panel.querySelector("[data-purchase-loading]");
    const ready = panel.querySelector("[data-purchase-ready]");
    const error = panel.querySelector("[data-purchase-error]");
    const optionsHost = panel.querySelector("[data-purchase-options]");
    const selectionText = panel.querySelector("[data-purchase-selection]");
    const price = panel.querySelector("[data-purchase-price]");
    const originalPrice = panel.querySelector("[data-purchase-original-price]");
    const sku = panel.querySelector("[data-purchase-sku]");
    const quantity = panel.querySelector("[data-purchase-quantity]");
    const minus = panel.querySelector("[data-purchase-minus]");
    const plus = panel.querySelector("[data-purchase-plus]");
    const addButton = panel.querySelector("[data-purchase-add]");
    const buyNowButton = panel.querySelector("[data-purchase-buy-now]");
    const mainImage = document.querySelector("[data-product-main-image]");

    const state = {
        product: null,
        selections: {},
        selectedItem: null
    };

    function matchesSelections(item, ignoredKey = null) {
        return Object.entries(state.selections).every(([key, value]) => {
            if (key === ignoredKey || !value) return true;
            return item.attributes?.[key] === value;
        });
    }

    function valueIsAvailable(groupKey, value) {
        return state.product.variants.some((item) =>
            item.stockQuantity > 0
            && item.attributes?.[groupKey] === value
            && matchesSelections(item, groupKey)
        );
    }

    function findSelectedItem() {
        const groups = state.product?.attributeGroups ?? [];
        if (!groups.every((group) => state.selections[group.key])) {
            return null;
        }

        return state.product.variants.find((item) =>
            item.stockQuantity > 0
            && groups.every((group) =>
                item.attributes?.[group.key]
                === state.selections[group.key])
        ) ?? null;
    }

    function updateQuantityControls() {
        const max = state.selectedItem?.stockQuantity ?? 1;
        const next = Math.min(
            Math.max(1, Number.parseInt(quantity.value, 10) || 1),
            Math.max(1, max)
        );

        quantity.value = String(next);
        quantity.max = String(Math.max(1, max));
        quantity.disabled = !state.selectedItem;
        minus.disabled = !state.selectedItem || next <= 1;
        plus.disabled = !state.selectedItem || next >= max;
    }

    function updatePanel() {
        optionsHost
            .querySelectorAll("[data-purchase-option]")
            .forEach((button) => {
                const key = button.dataset.groupKey;
                const value = button.dataset.groupValue;
                const available = valueIsAvailable(key, value);

                button.disabled = !available;
                button.classList.toggle(
                    "is-selected",
                    state.selections[key] === value);
                button.setAttribute(
                    "aria-pressed",
                    state.selections[key] === value ? "true" : "false");
            });

        state.selectedItem = findSelectedItem();

        if (!state.selectedItem) {
            const missing = state.product.attributeGroups
                .filter((group) => !state.selections[group.key])
                .map((group) => group.label);

            selectionText.textContent = missing.length
                ? `Cần chọn: ${missing.join(", ")}.`
                : "Tổ hợp này hiện không còn hàng. Hãy chọn phương án khác.";
            selectionText.classList.remove("is-ready");
            price.textContent = cart.formatMoney(
                state.product.minimumPrice);
            originalPrice.hidden = true;
            sku.textContent = "—";
            addButton.disabled = true;
            buyNowButton.disabled = true;
            updateQuantityControls();
            return;
        }

        const item = state.selectedItem;
        selectionText.textContent = `Còn ${item.stockQuantity} sản phẩm.`;
        selectionText.classList.add("is-ready");
        price.textContent = cart.formatMoney(item.effectivePrice);
        sku.textContent = item.sku;

        if (item.originalPrice > item.effectivePrice) {
            originalPrice.textContent = cart.formatMoney(item.originalPrice);
            originalPrice.hidden = false;
        } else {
            originalPrice.hidden = true;
        }

        if (mainImage && item.imageUrl) {
            mainImage.src = item.imageUrl;
        }

        addButton.disabled = false;
        buyNowButton.disabled = false;
        updateQuantityControls();
    }

    function renderGroup(group) {
        const fieldset = document.createElement("fieldset");
        fieldset.className = "product-purchase-group";

        const legend = document.createElement("legend");
        legend.textContent = group.label;
        fieldset.append(legend);

        const values = document.createElement("div");
        values.className = "product-purchase-group__values";

        group.values.forEach((value) => {
            const button = document.createElement("button");
            button.type = "button";
            button.textContent = value;
            button.dataset.purchaseOption = "true";
            button.dataset.groupKey = group.key;
            button.dataset.groupValue = value;
            button.setAttribute("aria-pressed", "false");
            values.append(button);
        });

        fieldset.append(values);
        return fieldset;
    }

    function renderProduct(product) {
        state.product = product;
        optionsHost.replaceChildren();

        if (!product.variants?.length) {
            loading.hidden = true;
            error.hidden = false;
            error.textContent =
                "Sản phẩm chưa có mã hàng phù hợp để đặt mua.";
            return;
        }

        product.attributeGroups.forEach((group) => {
            optionsHost.append(renderGroup(group));

            const availableValues = group.values.filter((value) =>
                product.variants.some((item) =>
                    item.stockQuantity > 0
                    && item.attributes?.[group.key] === value)
            );

            if (availableValues.length === 1) {
                state.selections[group.key] = availableValues[0];
            }
        });

        loading.hidden = true;
        ready.hidden = false;
        updatePanel();
    }

    async function submit(buyNow) {
        if (!state.selectedItem) return;

        const button = buyNow ? buyNowButton : addButton;
        const originalText = button.textContent;

        addButton.disabled = true;
        buyNowButton.disabled = true;
        button.textContent = "Đang kiểm tra…";

        try {
            const result = await cart.requestJson("/cart/add", {
                method: "POST",
                body: {
                    productId,
                    variantId: state.selectedItem.id,
                    quantity: Number.parseInt(quantity.value, 10) || 1,
                    buyNow
                }
            });

            cart.applyCartSummary(result.cart);
            cart.toast(result.message, "success");

            if (result.redirectUrl) {
                window.location.assign(result.redirectUrl);
                return;
            }

            updatePanel();
        } catch (requestError) {
            error.hidden = false;
            error.textContent = requestError.message;
            cart.toast(requestError.message, "error");
            updatePanel();
        } finally {
            button.textContent = originalText;
        }
    }

    optionsHost.addEventListener("click", (event) => {
        const button = event.target.closest("[data-purchase-option]");
        if (!button || button.disabled || !state.product) return;

        const key = button.dataset.groupKey;
        const value = button.dataset.groupValue;
        state.selections[key] = value;

        state.product.attributeGroups.forEach((group) => {
            const selectedValue = state.selections[group.key];
            if (!selectedValue || group.key === key) return;

            const compatible = state.product.variants.some((item) =>
                item.stockQuantity > 0
                && item.attributes?.[group.key] === selectedValue
                && matchesSelections(item, group.key)
            );

            if (!compatible) {
                delete state.selections[group.key];
            }
        });

        updatePanel();
    });

    quantity.addEventListener("change", updateQuantityControls);
    minus.addEventListener("click", () => {
        quantity.value = String(
            Math.max(1, Number(quantity.value) - 1));
        updateQuantityControls();
    });
    plus.addEventListener("click", () => {
        const max = state.selectedItem?.stockQuantity ?? 1;
        quantity.value = String(
            Math.min(max, Number(quantity.value) + 1));
        updateQuantityControls();
    });
    addButton.addEventListener("click", () => submit(false));
    buyNowButton.addEventListener("click", () => submit(true));

    async function load() {
        if (!Number.isInteger(productId) || productId <= 0) {
            loading.hidden = true;
            error.hidden = false;
            error.textContent = "Sản phẩm không hợp lệ.";
            return;
        }

        try {
            const response = await cart.requestJson(
                `/cart/product-options/${productId}`);
            renderProduct(response.data);
        } catch (requestError) {
            loading.hidden = true;
            error.hidden = false;
            error.textContent = requestError.message;
        }
    }

    load();
})();
