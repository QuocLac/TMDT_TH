(() => {
    "use strict";

    const cart = window.FastBuyCart;

    function initializeCountdowns() {
        const countdowns = document.querySelectorAll("[data-countdown]");
        if (!countdowns.length) return;

        const pad = (value) => String(Math.max(0, value)).padStart(2, "0");

        function updateCountdown(element) {
            const endAt = Date.parse(element.dataset.countdownEnd ?? "");
            if (!Number.isFinite(endAt)) {
                element.hidden = true;
                return;
            }

            const remaining = Math.max(0, endAt - Date.now());
            const totalSeconds = Math.floor(remaining / 1000);
            const days = Math.floor(totalSeconds / 86400);
            const hours = Math.floor((totalSeconds % 86400) / 3600);
            const minutes = Math.floor((totalSeconds % 3600) / 60);
            const seconds = totalSeconds % 60;

            element.querySelector("[data-countdown-days]").textContent = pad(days);
            element.querySelector("[data-countdown-hours]").textContent = pad(hours);
            element.querySelector("[data-countdown-minutes]").textContent = pad(minutes);
            element.querySelector("[data-countdown-seconds]").textContent = pad(seconds);

            if (remaining === 0) {
                element.dataset.expired = "true";
            }
        }

        countdowns.forEach(updateCountdown);
        const timer = window.setInterval(() => {
            countdowns.forEach(updateCountdown);
            if ([...countdowns].every((item) => item.dataset.expired === "true")) {
                window.clearInterval(timer);
            }
        }, 1000);
    }

    function initializeVariantPicker() {
        const dialog = document.querySelector("[data-variant-picker]");
        if (!dialog || !cart) return;

        const productName = dialog.querySelector("[data-picker-product-name]");
        const image = dialog.querySelector("[data-picker-image]");
        const loading = dialog.querySelector("[data-picker-loading]");
        const ready = dialog.querySelector("[data-picker-ready]");
        const error = dialog.querySelector("[data-picker-error]");
        const attributesHost = dialog.querySelector("[data-picker-attributes]");
        const status = dialog.querySelector("[data-picker-status]");
        const price = dialog.querySelector("[data-picker-price]");
        const originalPrice = dialog.querySelector("[data-picker-original-price]");
        const quantity = dialog.querySelector("[data-picker-quantity]");
        const minus = dialog.querySelector("[data-picker-quantity-minus]");
        const plus = dialog.querySelector("[data-picker-quantity-plus]");
        const confirm = dialog.querySelector("[data-picker-confirm]");

        const state = {
            product: null,
            selections: {},
            selectedVariant: null,
            intent: "add",
            loading: false
        };

        function resetPicker(productId, intent) {
            state.product = null;
            state.selections = {};
            state.selectedVariant = null;
            state.intent = intent;
            state.loading = true;

            dialog.dataset.productId = String(productId);
            productName.textContent = "Đang tải sản phẩm";
            image.src = "/images/no-image.png";
            image.alt = "";
            loading.hidden = false;
            ready.hidden = true;
            error.hidden = true;
            error.textContent = "";
            attributesHost.replaceChildren();
            status.textContent = "Hãy chọn đầy đủ thuộc tính để xác định đúng biến thể.";
            status.className = "variant-picker__selection-status";
            price.textContent = "—";
            originalPrice.hidden = true;
            originalPrice.textContent = "";
            quantity.value = "1";
            quantity.max = "1";
            confirm.disabled = true;
            confirm.textContent = intent === "buy-now" ? "Mua ngay" : "Thêm vào giỏ";
        }

        function matchesSelections(variant, selections, ignoredKey = null) {
            return Object.entries(selections).every(([key, value]) => {
                if (key === ignoredKey || !value) return true;
                return variant.attributes?.[key] === value;
            });
        }

        function optionHasAvailableVariant(groupKey, value) {
            return state.product.variants.some((variant) => {
                if (variant.stockQuantity <= 0) return false;
                if (variant.attributes?.[groupKey] !== value) return false;
                return matchesSelections(variant, state.selections, groupKey);
            });
        }

        function findSelectedVariant() {
            const groups = state.product?.attributeGroups ?? [];
            const allSelected = groups.every((group) => Boolean(state.selections[group.key]));
            if (!allSelected) return null;

            return state.product.variants.find((variant) =>
                variant.stockQuantity > 0
                && groups.every((group) =>
                    variant.attributes?.[group.key] === state.selections[group.key]
                )
            ) ?? null;
        }

        function updateOptionAvailability() {
            attributesHost.querySelectorAll("[data-attribute-option]").forEach((button) => {
                const groupKey = button.dataset.attributeKey;
                const value = button.dataset.attributeValue;
                const available = optionHasAvailableVariant(groupKey, value);
                button.disabled = !available;
                button.classList.toggle(
                    "is-selected",
                    state.selections[groupKey] === value
                );
                button.setAttribute(
                    "aria-pressed",
                    state.selections[groupKey] === value ? "true" : "false"
                );
            });
        }

        function updateSelection() {
            if (!state.product) return;

            updateOptionAvailability();
            state.selectedVariant = findSelectedVariant();

            if (!state.selectedVariant) {
                const remaining = state.product.attributeGroups
                    .filter((group) => !state.selections[group.key])
                    .map((group) => group.label);

                status.className = "variant-picker__selection-status";
                status.textContent = remaining.length
                    ? `Cần chọn: ${remaining.join(", ")}.`
                    : "Tổ hợp thuộc tính này không còn hàng. Hãy chọn tổ hợp khác.";
                price.textContent = cart.formatMoney(state.product.minimumPrice);
                originalPrice.hidden = true;
                quantity.max = "1";
                quantity.value = "1";
                minus.disabled = true;
                plus.disabled = true;
                confirm.disabled = true;
                return;
            }

            const variant = state.selectedVariant;
            image.src = variant.imageUrl || state.product.imageUrl || "/images/no-image.png";
            image.alt = `${state.product.productName} - ${variant.sku}`;
            price.textContent = cart.formatMoney(variant.effectivePrice);

            if (variant.originalPrice > variant.effectivePrice) {
                originalPrice.textContent = cart.formatMoney(variant.originalPrice);
                originalPrice.hidden = false;
            } else {
                originalPrice.hidden = true;
            }

            quantity.max = String(Math.max(1, variant.stockQuantity));
            quantity.value = String(
                Math.min(
                    Math.max(1, Number.parseInt(quantity.value, 10) || 1),
                    variant.stockQuantity
                )
            );

            status.className = "variant-picker__selection-status is-ready";
            status.textContent = `${variant.sku} · Còn ${variant.stockQuantity} sản phẩm.`;
            minus.disabled = Number(quantity.value) <= 1;
            plus.disabled = Number(quantity.value) >= variant.stockQuantity;
            confirm.disabled = false;
        }

        function createAttributeGroup(group) {
            const fieldset = document.createElement("fieldset");
            fieldset.className = "variant-attribute-group";

            const legend = document.createElement("legend");
            legend.textContent = group.label;
            fieldset.append(legend);

            const options = document.createElement("div");
            options.className = "variant-attribute-group__options";

            group.values.forEach((value) => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "variant-option";
                button.textContent = value;
                button.dataset.attributeOption = "true";
                button.dataset.attributeKey = group.key;
                button.dataset.attributeValue = value;
                button.setAttribute("aria-pressed", "false");
                options.append(button);
            });

            fieldset.append(options);
            return fieldset;
        }

        function renderProduct(product) {
            state.product = product;
            state.loading = false;
            productName.textContent = product.productName;
            image.src = product.imageUrl || "/images/no-image.png";
            image.alt = product.productName;
            loading.hidden = true;
            ready.hidden = false;
            attributesHost.replaceChildren();

            if (!product.variants?.length) {
                ready.hidden = true;
                error.hidden = false;
                error.textContent = "Sản phẩm chưa có biến thể hợp lệ để mua.";
                confirm.disabled = true;
                return;
            }

            product.attributeGroups.forEach((group) => {
                attributesHost.append(createAttributeGroup(group));

                const availableValues = group.values.filter((value) =>
                    product.variants.some((variant) =>
                        variant.stockQuantity > 0
                        && variant.attributes?.[group.key] === value
                    )
                );

                if (availableValues.length === 1) {
                    state.selections[group.key] = availableValues[0];
                }
            });

            if (product.attributeGroups.length === 0) {
                state.selectedVariant = product.variants.find((variant) => variant.stockQuantity > 0) ?? null;
            }

            updateSelection();
        }

        async function openPicker(button) {
            const productId = Number.parseInt(button.dataset.productId ?? "", 10);
            if (!Number.isInteger(productId) || productId <= 0) return;

            const intent = button.dataset.cartIntent === "buy-now" ? "buy-now" : "add";
            resetPicker(productId, intent);
            dialog.showModal();

            try {
                const response = await cart.requestJson(`/cart/product-options/${productId}`);
                renderProduct(response.data);
            } catch (requestError) {
                state.loading = false;
                loading.hidden = true;
                ready.hidden = true;
                error.hidden = false;
                error.textContent = requestError.message;
                confirm.disabled = true;
            }
        }

        attributesHost.addEventListener("click", (event) => {
            const button = event.target.closest("[data-attribute-option]");
            if (!button || button.disabled || !state.product) return;

            const key = button.dataset.attributeKey;
            const value = button.dataset.attributeValue;
            state.selections[key] = value;

            // Khi một thuộc tính đổi, giữ các lựa chọn tương thích và bỏ
            // lựa chọn ở nhóm khác nếu không còn biến thể phù hợp.
            state.product.attributeGroups.forEach((group) => {
                const selectedValue = state.selections[group.key];
                if (!selectedValue) return;

                const stillCompatible = state.product.variants.some((variant) =>
                    variant.stockQuantity > 0
                    && variant.attributes?.[group.key] === selectedValue
                    && matchesSelections(variant, state.selections, group.key)
                );

                if (!stillCompatible && group.key !== key) {
                    delete state.selections[group.key];
                }
            });

            updateSelection();
        });

        quantity.addEventListener("change", () => {
            const max = state.selectedVariant?.stockQuantity ?? 1;
            const next = Math.min(
                Math.max(1, Number.parseInt(quantity.value, 10) || 1),
                max
            );
            quantity.value = String(next);
            minus.disabled = next <= 1;
            plus.disabled = next >= max;
        });

        minus.addEventListener("click", () => {
            quantity.value = String(Math.max(1, Number(quantity.value) - 1));
            quantity.dispatchEvent(new Event("change"));
        });

        plus.addEventListener("click", () => {
            const max = state.selectedVariant?.stockQuantity ?? 1;
            quantity.value = String(Math.min(max, Number(quantity.value) + 1));
            quantity.dispatchEvent(new Event("change"));
        });

        confirm.addEventListener("click", async () => {
            if (!state.product || !state.selectedVariant || confirm.disabled) return;

            confirm.disabled = true;
            const originalText = confirm.textContent;
            let redirecting = false;
            confirm.textContent = "Đang kiểm tra tồn kho…";

            try {
                const result = await cart.requestJson("/cart/add", {
                    method: "POST",
                    body: {
                        productId: state.product.productId,
                        variantId: state.selectedVariant.id,
                        quantity: Number.parseInt(quantity.value, 10) || 1,
                        buyNow: state.intent === "buy-now"
                    }
                });

                cart.applyCartSummary(result.cart);
                cart.toast(result.message, "success");

                if (result.redirectUrl) {
                    redirecting = true;
                    window.location.assign(result.redirectUrl);
                    return;
                }

                dialog.close();
            } catch (requestError) {
                error.hidden = false;
                error.textContent = requestError.message;
                cart.toast(requestError.message, "error");
            } finally {
                confirm.textContent = originalText;
                if (!redirecting) {
                    confirm.disabled = !state.selectedVariant;
                }
            }
        });

        document.addEventListener("click", (event) => {
            const openButton = event.target.closest("[data-open-variant-picker]");
            if (openButton) {
                openPicker(openButton);
                return;
            }

            if (event.target.closest("[data-picker-close]")) {
                dialog.close();
            }
        });

        dialog.addEventListener("click", (event) => {
            if (event.target === dialog) {
                dialog.close();
            }
        });
    }

    initializeCountdowns();
    initializeVariantPicker();
})();
