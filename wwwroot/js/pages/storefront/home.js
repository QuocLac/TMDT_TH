(() => {
    "use strict";

    const cart = window.FastBuyCart;

    function normalizeCommercialLanguage() {
        const replacements = new Map([
            ["FastBuy shopping system", "Mua sắm đa ngành hàng"],
            ["Smart variant shopping", "Chọn đúng sản phẩm"],
            ["Smart Cart", "Giỏ hàng tiện lợi"],
            ["Live pricing", "Giá cập nhật"],
            ["Live", "Đang cập nhật"],
            ["Session", "Tự động lưu"],
            ["database", "hệ thống"],
            ["Database", "hệ thống"],
            ["campaign", "lịch giá"],
            ["Campaign", "Lịch giá"],
            ["biến thể", "lựa chọn mua"],
            ["Biến thể", "Lựa chọn mua"],
            ["giá hiệu lực", "giá đang áp dụng"],
            ["Giá hiệu lực", "Giá đang áp dụng"],
            ["tồn kho thực tế", "số lượng có thể bán"],
            ["Tồn kho thực tế", "Số lượng có thể bán"]
        ]);

        const walker = document.createTreeWalker(
            document.body,
            NodeFilter.SHOW_TEXT
        );

        const nodes = [];
        while (walker.nextNode()) {
            nodes.push(walker.currentNode);
        }

        nodes.forEach((node) => {
            let value = node.nodeValue ?? "";
            replacements.forEach((replacement, source) => {
                value = value.replaceAll(source, replacement);
            });
            node.nodeValue = value;
        });

        document.querySelectorAll("[aria-label], [placeholder], [title]").forEach((element) => {
            ["aria-label", "placeholder", "title"].forEach((attribute) => {
                if (!element.hasAttribute(attribute)) return;

                let value = element.getAttribute(attribute) ?? "";
                replacements.forEach((replacement, source) => {
                    value = value.replaceAll(source, replacement);
                });
                element.setAttribute(attribute, value);
            });
        });
    }

    function initializeCountdowns() {
        const countdowns = document.querySelectorAll("[data-countdown]");
        if (!countdowns.length) return;

        const pad = (value) =>
            String(Math.max(0, value)).padStart(2, "0");

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

            if ([...countdowns].every(
                    (item) => item.dataset.expired === "true")) {
                window.clearInterval(timer);
            }
        }, 1000);
    }

    function initializePurchaseOptionPicker() {
        const dialog = document.querySelector("[data-variant-picker]");

        if (!dialog || !cart) {
            document.addEventListener("click", (event) => {
                const button = event.target.closest(
                    "[data-open-variant-picker]");
                if (!button) return;

                event.preventDefault();
                const fallbackUrl = button.dataset.productUrl;
                if (fallbackUrl) {
                    window.location.assign(fallbackUrl);
                }
            });
            return;
        }

        const productName = dialog.querySelector(
            "[data-picker-product-name]");
        const image = dialog.querySelector("[data-picker-image]");
        const loading = dialog.querySelector("[data-picker-loading]");
        const ready = dialog.querySelector("[data-picker-ready]");
        const error = dialog.querySelector("[data-picker-error]");
        const optionsHost = dialog.querySelector(
            "[data-picker-attributes]");
        const status = dialog.querySelector("[data-picker-status]");
        const price = dialog.querySelector("[data-picker-price]");
        const originalPrice = dialog.querySelector(
            "[data-picker-original-price]");
        const quantity = dialog.querySelector("[data-picker-quantity]");
        const minus = dialog.querySelector(
            "[data-picker-quantity-minus]");
        const plus = dialog.querySelector(
            "[data-picker-quantity-plus]");
        const confirm = dialog.querySelector("[data-picker-confirm]");

        const state = {
            product: null,
            selections: {},
            selectedItem: null,
            intent: "add"
        };

        function resetPicker(productId, intent) {
            state.product = null;
            state.selections = {};
            state.selectedItem = null;
            state.intent = intent;

            dialog.dataset.productId = String(productId);
            productName.textContent = "Đang tải sản phẩm";
            image.src = "/images/no-image.png";
            image.alt = "";
            loading.hidden = false;
            ready.hidden = true;
            error.hidden = true;
            error.textContent = "";
            optionsHost.replaceChildren();
            status.textContent =
                "Hãy chọn đầy đủ để xác định đúng sản phẩm.";
            status.className = "variant-picker__selection-status";
            price.textContent = "—";
            originalPrice.hidden = true;
            originalPrice.textContent = "";
            quantity.value = "1";
            quantity.max = "1";
            confirm.disabled = true;
            confirm.textContent = intent === "buy-now"
                ? "Mua ngay"
                : "Thêm vào giỏ";
        }

        function matchesSelections(item, selections, ignoredKey = null) {
            return Object.entries(selections).every(([key, value]) => {
                if (key === ignoredKey || !value) return true;
                return item.attributes?.[key] === value;
            });
        }

        function valueHasAvailableItem(groupKey, value) {
            return state.product.variants.some((item) =>
                item.stockQuantity > 0
                && item.attributes?.[groupKey] === value
                && matchesSelections(item, state.selections, groupKey)
            );
        }

        function findSelectedItem() {
            const groups = state.product?.attributeGroups ?? [];
            const allSelected = groups.every((group) =>
                Boolean(state.selections[group.key]));

            if (!allSelected) return null;

            return state.product.variants.find((item) =>
                item.stockQuantity > 0
                && groups.every((group) =>
                    item.attributes?.[group.key]
                    === state.selections[group.key])
            ) ?? null;
        }

        function updateValueAvailability() {
            optionsHost
                .querySelectorAll("[data-attribute-option]")
                .forEach((button) => {
                    const groupKey = button.dataset.attributeKey;
                    const value = button.dataset.attributeValue;
                    const available = valueHasAvailableItem(
                        groupKey,
                        value);

                    button.disabled = !available;
                    button.classList.toggle(
                        "is-selected",
                        state.selections[groupKey] === value);
                    button.setAttribute(
                        "aria-pressed",
                        state.selections[groupKey] === value
                            ? "true"
                            : "false");
                });
        }

        function updateSelection() {
            if (!state.product) return;

            updateValueAvailability();
            state.selectedItem = findSelectedItem();

            if (!state.selectedItem) {
                const remaining = state.product.attributeGroups
                    .filter((group) => !state.selections[group.key])
                    .map((group) => group.label);

                status.className =
                    "variant-picker__selection-status";
                status.textContent = remaining.length
                    ? `Cần chọn: ${remaining.join(", ")}.`
                    : "Lựa chọn này hiện không còn hàng. Hãy chọn phương án khác.";
                price.textContent = cart.formatMoney(
                    state.product.minimumPrice);
                originalPrice.hidden = true;
                quantity.max = "1";
                quantity.value = "1";
                minus.disabled = true;
                plus.disabled = true;
                confirm.disabled = true;
                return;
            }

            const item = state.selectedItem;
            image.src = item.imageUrl
                || state.product.imageUrl
                || "/images/no-image.png";
            image.alt = state.product.productName;
            price.textContent = cart.formatMoney(item.effectivePrice);

            if (item.originalPrice > item.effectivePrice) {
                originalPrice.textContent = cart.formatMoney(
                    item.originalPrice);
                originalPrice.hidden = false;
            } else {
                originalPrice.hidden = true;
            }

            const maxQuantity = Math.max(1, item.stockQuantity);
            const nextQuantity = Math.min(
                Math.max(
                    1,
                    Number.parseInt(quantity.value, 10) || 1),
                maxQuantity);

            quantity.max = String(maxQuantity);
            quantity.value = String(nextQuantity);

            status.className =
                "variant-picker__selection-status is-ready";
            status.textContent =
                `Còn ${item.stockQuantity} sản phẩm.`;
            minus.disabled = nextQuantity <= 1;
            plus.disabled = nextQuantity >= item.stockQuantity;
            confirm.disabled = false;
        }

        function createOptionGroup(group) {
            const fieldset = document.createElement("fieldset");
            fieldset.className = "variant-attribute-group";

            const legend = document.createElement("legend");
            legend.textContent = group.label;
            fieldset.append(legend);

            const options = document.createElement("div");
            options.className =
                "variant-attribute-group__options";

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
            productName.textContent = product.productName;
            image.src = product.imageUrl || "/images/no-image.png";
            image.alt = product.productName;
            loading.hidden = true;
            ready.hidden = false;
            optionsHost.replaceChildren();

            if (!product.variants?.length) {
                ready.hidden = true;
                error.hidden = false;
                error.textContent =
                    "Sản phẩm chưa có lựa chọn mua phù hợp.";
                confirm.disabled = true;
                return;
            }

            product.attributeGroups.forEach((group) => {
                optionsHost.append(createOptionGroup(group));

                const availableValues = group.values.filter((value) =>
                    product.variants.some((item) =>
                        item.stockQuantity > 0
                        && item.attributes?.[group.key] === value));

                if (availableValues.length === 1) {
                    state.selections[group.key] =
                        availableValues[0];
                }
            });

            if (product.attributeGroups.length === 0) {
                state.selectedItem = product.variants.find(
                    (item) => item.stockQuantity > 0) ?? null;
            }

            updateSelection();
        }

        async function openPicker(button) {
            const productId = Number.parseInt(
                button.dataset.productId ?? "",
                10);

            if (!Number.isInteger(productId) || productId <= 0) {
                return;
            }

            const intent = button.dataset.cartIntent === "buy-now"
                ? "buy-now"
                : "add";

            resetPicker(productId, intent);
            dialog.showModal();

            try {
                const response = await cart.requestJson(
                    `/cart/product-options/${productId}`);
                renderProduct(response.data);
            } catch (requestError) {
                loading.hidden = true;
                ready.hidden = true;
                error.hidden = false;
                error.textContent = requestError.message;
                confirm.disabled = true;
            }
        }

        optionsHost.addEventListener("click", (event) => {
            const button = event.target.closest(
                "[data-attribute-option]");

            if (!button || button.disabled || !state.product) {
                return;
            }

            const key = button.dataset.attributeKey;
            const value = button.dataset.attributeValue;
            state.selections[key] = value;

            state.product.attributeGroups.forEach((group) => {
                const selectedValue =
                    state.selections[group.key];

                if (!selectedValue) return;

                const compatible = state.product.variants.some((item) =>
                    item.stockQuantity > 0
                    && item.attributes?.[group.key] === selectedValue
                    && matchesSelections(
                        item,
                        state.selections,
                        group.key));

                if (!compatible && group.key !== key) {
                    delete state.selections[group.key];
                }
            });

            updateSelection();
        });

        quantity.addEventListener("change", () => {
            const max = state.selectedItem?.stockQuantity ?? 1;
            const next = Math.min(
                Math.max(
                    1,
                    Number.parseInt(quantity.value, 10) || 1),
                max);

            quantity.value = String(next);
            minus.disabled = next <= 1;
            plus.disabled = next >= max;
        });

        minus.addEventListener("click", () => {
            quantity.value = String(
                Math.max(1, Number(quantity.value) - 1));
            quantity.dispatchEvent(new Event("change"));
        });

        plus.addEventListener("click", () => {
            const max = state.selectedItem?.stockQuantity ?? 1;
            quantity.value = String(
                Math.min(max, Number(quantity.value) + 1));
            quantity.dispatchEvent(new Event("change"));
        });

        confirm.addEventListener("click", async () => {
            if (!state.product
                || !state.selectedItem
                || confirm.disabled) {
                return;
            }

            confirm.disabled = true;
            const originalText = confirm.textContent;
            let redirecting = false;
            confirm.textContent = "Đang kiểm tra số lượng…";

            try {
                const result = await cart.requestJson("/cart/add", {
                    method: "POST",
                    body: {
                        productId: state.product.productId,
                        variantId: state.selectedItem.id,
                        quantity: Number.parseInt(
                            quantity.value,
                            10) || 1,
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
                    confirm.disabled = !state.selectedItem;
                }
            }
        });

        document.addEventListener("click", (event) => {
            const openButton = event.target.closest(
                "[data-open-variant-picker]");

            if (openButton) {
                event.preventDefault();
                event.stopPropagation();
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

    normalizeCommercialLanguage();
    initializeCountdowns();
    initializePurchaseOptionPicker();
})();
