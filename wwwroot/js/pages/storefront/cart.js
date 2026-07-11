(() => {
    "use strict";

    const api = window.FastBuyCart;
    const page = document.querySelector("[data-cart-page]");
    if (!api || !page) return;

    let pendingRequests = 0;

    function setBusy(element, busy) {
        if (!element) return;
        element.disabled = busy;
        element.setAttribute("aria-busy", busy ? "true" : "false");
    }

    async function mutate(url, body, trigger) {
        pendingRequests++;
        setBusy(trigger, true);

        try {
            const result = await api.requestJson(url, {
                method: "POST",
                body
            });
            api.applyCartSummary(result.cart);
            api.toast(result.message, "success");
            return result;
        } catch (error) {
            if (error.payload?.cart) {
                api.applyCartSummary(error.payload.cart);
            }
            api.toast(error.message, "error", 4800);
            throw error;
        } finally {
            pendingRequests--;
            setBusy(trigger, false);
        }
    }

    function getLine(element) {
        return element.closest("[data-cart-line]");
    }

    function getVariantId(line) {
        return Number.parseInt(line?.dataset.variantId ?? "", 10);
    }

    async function updateQuantity(line, nextQuantity, trigger) {
        const variantId = getVariantId(line);
        const input = line?.querySelector("[data-cart-quantity]");
        if (!Number.isInteger(variantId) || !input) return;

        const previous = Number.parseInt(input.value, 10) || 1;
        const maximum = Number.parseInt(input.max, 10) || 1;
        const normalized = Math.min(Math.max(1, nextQuantity), maximum);
        input.value = String(normalized);

        try {
            await mutate("/cart/quantity", {
                variantId,
                quantity: normalized
            }, trigger ?? input);
        } catch {
            input.value = String(previous);
        }
    }

    page.addEventListener("click", async (event) => {
        const minus = event.target.closest("[data-cart-quantity-minus]");
        if (minus) {
            const line = getLine(minus);
            const input = line?.querySelector("[data-cart-quantity]");
            if (input) {
                await updateQuantity(line, Number(input.value) - 1, minus);
            }
            return;
        }

        const plus = event.target.closest("[data-cart-quantity-plus]");
        if (plus) {
            const line = getLine(plus);
            const input = line?.querySelector("[data-cart-quantity]");
            if (input) {
                await updateQuantity(line, Number(input.value) + 1, plus);
            }
            return;
        }

        const remove = event.target.closest("[data-cart-remove]");
        if (remove) {
            const line = getLine(remove);
            const variantId = getVariantId(line);
            if (!Number.isInteger(variantId)) return;

            if (!window.confirm("Xóa biến thể này khỏi giỏ hàng?")) return;

            try {
                const result = await mutate("/cart/remove", { variantId }, remove);
                line.remove();
                if ((result.cart?.totalLineCount ?? 0) === 0) {
                    window.location.reload();
                }
            } catch {
                // Toast đã hiển thị lỗi.
            }
            return;
        }

        const checkout = event.target.closest("[data-prepare-checkout]");
        if (checkout) {
            const resultHost = document.querySelector("[data-checkout-result]");
            setBusy(checkout, true);
            checkout.textContent = "Đang kiểm tra giá và tồn kho…";

            try {
                const result = await api.requestJson("/cart/prepare-checkout", {
                    method: "POST"
                });
                api.applyCartSummary(result.cart);
                if (result.success) {
                    window.location.assign(result.redirectUrl || "/checkout");
                    return;
                }
                resultHost.hidden = false;
                resultHost.classList.remove("is-error");
                resultHost.textContent = result.message;
            } catch (error) {
                resultHost.hidden = false;
                resultHost.classList.add("is-error");
                resultHost.textContent = error.message;
                api.toast(error.message, "error", 5000);
            } finally {
                checkout.textContent = "Kiểm tra giỏ hàng đã chọn";
                setBusy(checkout, false);
            }
        }
    });

    page.addEventListener("change", async (event) => {
        const quantity = event.target.closest("[data-cart-quantity]");
        if (quantity) {
            const line = getLine(quantity);
            await updateQuantity(
                line,
                Number.parseInt(quantity.value, 10) || 1,
                quantity
            );
            return;
        }

        const selection = event.target.closest("[data-cart-line-select]");
        if (selection) {
            const line = getLine(selection);
            const variantId = getVariantId(line);
            if (!Number.isInteger(variantId)) return;

            const desired = selection.checked;
            try {
                await mutate("/cart/selection", {
                    variantId,
                    isSelected: desired
                }, selection);
            } catch {
                selection.checked = !desired;
            }
            return;
        }

        const selectAll = event.target.closest("[data-cart-select-all]");
        if (selectAll) {
            const desired = selectAll.checked;
            try {
                const result = await mutate("/cart/selection/all", {
                    isSelected: desired
                }, selectAll);

                const selectedById = new Map(
                    (result.cart?.items ?? []).map((item) => [
                        String(item.variantId),
                        Boolean(item.isSelected)
                    ])
                );

                document.querySelectorAll("[data-cart-line]").forEach((line) => {
                    const checkbox = line.querySelector("[data-cart-line-select]");
                    if (!checkbox) return;
                    checkbox.checked = selectedById.get(line.dataset.variantId ?? "") ?? false;
                });
            } catch {
                selectAll.checked = !desired;
            }
        }
    });

    const customerForm = document.querySelector("[data-mock-customer-form]");
    customerForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        const submit = customerForm.querySelector("[data-save-mock-customer]");
        const status = customerForm.querySelector("[data-customer-save-status]");
        const formData = new FormData(customerForm);

        setBusy(submit, true);
        status.textContent = "Đang lưu…";

        try {
            const result = await api.requestJson("/cart/mock-customer", {
                method: "POST",
                body: {
                    fullName: formData.get("fullName"),
                    email: formData.get("email"),
                    phone: formData.get("phone"),
                    addressLine: formData.get("addressLine"),
                    ward: formData.get("ward"),
                    district: formData.get("district"),
                    city: formData.get("city")
                }
            });

            status.textContent = result.message;
            api.toast(result.message, "success");
        } catch (error) {
            status.textContent = error.message;
            api.toast(error.message, "error");
        } finally {
            setBusy(submit, false);
        }
    });
})();
