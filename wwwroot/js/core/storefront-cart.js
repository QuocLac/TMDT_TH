(() => {
    "use strict";

    if (window.FastBuyCart) return;

    const token = document
        .querySelector('meta[name="request-verification-token"]')
        ?.getAttribute("content") ?? "";

    function formatMoney(value) {
        const number = Number(value);
        if (!Number.isFinite(number)) return "0 ₫";
        return `${new Intl.NumberFormat("vi-VN").format(number)} ₫`;
    }

    async function requestJson(url, options = {}) {
        const method = String(options.method ?? "GET").toUpperCase();
        const headers = new Headers(options.headers ?? {});

        headers.set("Accept", "application/json");
        if (options.body !== undefined && options.body !== null) {
            headers.set("Content-Type", "application/json");
        }
        if (method !== "GET" && method !== "HEAD" && token) {
            headers.set("RequestVerificationToken", token);
        }

        let response;
        try {
            response = await fetch(url, {
                ...options,
                method,
                headers,
                credentials: "same-origin",
                body: options.body === undefined || options.body === null
                    ? undefined
                    : JSON.stringify(options.body)
            });
        } catch (error) {
            const networkError = new Error("Không thể kết nối tới server. Hãy kiểm tra ứng dụng và thử lại.");
            networkError.cause = error;
            networkError.errorCode = "NETWORK_ERROR";
            throw networkError;
        }

        const payload = await response.json().catch(() => null);
        if (!response.ok || payload?.success === false) {
            const error = new Error(
                payload?.message
                ?? `Server trả về lỗi HTTP ${response.status}.`
            );
            error.status = response.status;
            error.errorCode = payload?.errorCode ?? "REQUEST_FAILED";
            error.payload = payload;
            throw error;
        }

        return payload;
    }

    function updateBadge(totalQuantity) {
        const quantity = Math.max(0, Number(totalQuantity) || 0);
        document.querySelectorAll("[data-cart-badge]").forEach((badge) => {
            badge.textContent = String(Math.min(quantity, 99));
            badge.hidden = quantity <= 0;
        });

        document.querySelectorAll(".cart-link").forEach((link) => {
            link.setAttribute("aria-label", `Giỏ hàng có ${quantity} sản phẩm`);
        });
    }

    function toast(message, type = "success", duration = 3200) {
        const host = document.querySelector("[data-cart-toast-host]");
        if (!host || !message) return;

        const element = document.createElement("div");
        element.className = `cart-toast ${type === "error" ? "is-error" : "is-success"}`;
        element.setAttribute("role", type === "error" ? "alert" : "status");
        element.textContent = message;
        host.append(element);

        window.setTimeout(() => {
            element.remove();
        }, duration);
    }

    function applyCartSummary(cart) {
        if (!cart) return;
        updateBadge(cart.totalQuantity);

        const mappings = [
            ["[data-cart-line-count]", cart.totalLineCount],
            ["[data-cart-total-quantity]", cart.totalQuantity],
            ["[data-cart-selected-lines]", cart.selectedLineCount],
            ["[data-cart-selected-quantity]", cart.selectedQuantity],
            ["[data-cart-unavailable-count]", cart.unavailableLineCount],
            ["[data-cart-selected-subtotal]", formatMoney(cart.selectedSubtotal)]
        ];

        mappings.forEach(([selector, value]) => {
            document.querySelectorAll(selector).forEach((element) => {
                element.textContent = String(value);
            });
        });

        const selectAll = document.querySelector("[data-cart-select-all]");
        if (selectAll) {
            selectAll.checked = Boolean(cart.allAvailableSelected);
        }

        const checkoutButton = document.querySelector("[data-prepare-checkout]");
        if (checkoutButton) {
            checkoutButton.disabled = !cart.hasSelectedItems;
        }

        if (cart.customer?.fullName) {
            document.querySelectorAll("[data-mock-account-name]").forEach((element) => {
                element.textContent = cart.customer.fullName;
            });
        }

        const byVariantId = new Map(
            (cart.items ?? []).map((item) => [String(item.variantId), item])
        );

        document.querySelectorAll("[data-cart-line]").forEach((lineElement) => {
            const item = byVariantId.get(lineElement.dataset.variantId ?? "");
            if (!item) return;

            lineElement.dataset.unitPrice = String(item.effectivePrice);
            lineElement.classList.toggle("has-issue", !item.canSelect);

            const checkbox = lineElement.querySelector("[data-cart-line-select]");
            if (checkbox) {
                checkbox.checked = Boolean(item.isSelected);
                checkbox.disabled = !item.canSelect;
            }

            const quantity = lineElement.querySelector("[data-cart-quantity]");
            if (quantity) {
                quantity.value = String(item.quantity);
                quantity.max = String(Math.max(1, item.maxQuantity));
                quantity.disabled = !item.isAvailable;
            }

            const minus = lineElement.querySelector("[data-cart-quantity-minus]");
            if (minus) {
                minus.disabled = !item.isAvailable || item.quantity <= 1;
            }

            const plus = lineElement.querySelector("[data-cart-quantity-plus]");
            if (plus) {
                plus.disabled = !item.isAvailable || item.quantity >= item.maxQuantity;
            }

            const unitPrice = lineElement.querySelector("[data-cart-unit-price]");
            if (unitPrice) {
                unitPrice.textContent = formatMoney(item.effectivePrice);
            }

            const lineTotal = lineElement.querySelector("[data-cart-line-total]");
            if (lineTotal) {
                lineTotal.textContent = formatMoney(item.lineTotal);
            }
        });
    }

    window.FastBuyCart = Object.freeze({
        requestJson,
        formatMoney,
        updateBadge,
        applyCartSummary,
        toast
    });
})();
