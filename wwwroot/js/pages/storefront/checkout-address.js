(() => {
    "use strict";

    const page = document.querySelector("[data-checkout-page]");
    const api = window.FastBuyCart;
    if (!page || !api) return;

    const form = page.querySelector("[data-checkout-form]");
    const provinceSelect = page.querySelector("[data-province-select]");
    const districtSelect = page.querySelector("[data-district-select]");
    const wardSelect = page.querySelector("[data-ward-select]");
    const validateButton = page.querySelector("[data-validate-address]");
    const placeOrderButton = page.querySelector("[data-place-order]");
    const state = page.querySelector("[data-address-validation-state]");
    const message = page.querySelector("[data-checkout-message]");
    const canonical = page.querySelector("[data-canonical-address]");
    const canonicalText = page.querySelector("[data-canonical-address-text]");
    const onlineOutcome = page.querySelector("[data-online-outcome]");
    const paymentInputs = [...page.querySelectorAll('input[name="PaymentMethod"]')];

    function setBusy(element, busy) {
        if (!element) return;
        element.disabled = busy;
        element.setAttribute("aria-busy", busy ? "true" : "false");
    }

    function setMessage(text, type = "info") {
        if (!message) return;
        message.hidden = !text;
        message.textContent = text ?? "";
        message.classList.toggle("checkout-alert--danger", type === "error");
        message.classList.toggle("checkout-alert--success", type === "success");
    }

    function resetValidation() {
        if (state) {
            state.textContent = "Chưa kiểm tra";
            state.classList.remove("is-valid", "is-invalid");
        }
        if (canonical) canonical.hidden = true;
        setMessage("");
    }

    function setOptions(select, items, placeholder, valueKey, labelKey) {
        if (!select) return;
        select.replaceChildren(new Option(placeholder, ""));
        (items ?? []).forEach((item) => {
            select.add(new Option(item[labelKey], item[valueKey]));
        });
        select.disabled = (items ?? []).length === 0;
    }

    async function loadOptions(url, select, placeholder, valueKey, labelKey) {
        setOptions(select, [], "Đang tải...", valueKey, labelKey);
        const response = await fetch(url, {
            headers: { Accept: "application/json" },
            credentials: "same-origin"
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || payload?.success !== true) {
            setOptions(select, [], placeholder, valueKey, labelKey);
            throw new Error(payload?.message ?? "Không thể tải dữ liệu địa chỉ.");
        }
        setOptions(select, payload.data, placeholder, valueKey, labelKey);
        return payload.data ?? [];
    }

    async function loadDistricts(provinceId, selectedDistrictId = "") {
        setOptions(wardSelect, [], "Chọn phường/xã", "code", "name");
        if (!Number.isInteger(provinceId) || provinceId <= 0) {
            setOptions(districtSelect, [], "Chọn quận/huyện", "id", "name");
            return;
        }
        await loadOptions(
            "/checkout/districts/" + provinceId,
            districtSelect,
            "Chọn quận/huyện",
            "id",
            "name"
        );
        if (selectedDistrictId) districtSelect.value = String(selectedDistrictId);
    }

    async function loadWards(districtId, selectedWardCode = "") {
        if (!Number.isInteger(districtId) || districtId <= 0) {
            setOptions(wardSelect, [], "Chọn phường/xã", "code", "name");
            return;
        }
        await loadOptions(
            "/checkout/wards/" + districtId,
            wardSelect,
            "Chọn phường/xã",
            "code",
            "name"
        );
        if (selectedWardCode) wardSelect.value = selectedWardCode;
    }

    provinceSelect?.addEventListener("change", async () => {
        resetValidation();
        try {
            await loadDistricts(Number.parseInt(provinceSelect.value, 10));
        } catch (error) {
            setMessage(error.message, "error");
        }
    });

    districtSelect?.addEventListener("change", async () => {
        resetValidation();
        try {
            await loadWards(Number.parseInt(districtSelect.value, 10));
        } catch (error) {
            setMessage(error.message, "error");
        }
    });

    wardSelect?.addEventListener("change", resetValidation);
    form?.querySelector('input[name="AddressLine"]')?.addEventListener("input", resetValidation);

    validateButton?.addEventListener("click", async () => {
        const addressLine = form?.elements.namedItem("AddressLine")?.value?.trim() ?? "";
        const provinceId = Number.parseInt(provinceSelect?.value ?? "", 10);
        const districtId = Number.parseInt(districtSelect?.value ?? "", 10);
        const wardCode = wardSelect?.value?.trim() ?? "";

        if (addressLine.length < 3
            || !Number.isInteger(provinceId)
            || !Number.isInteger(districtId)
            || !wardCode) {
            setMessage("Hãy nhập địa chỉ cụ thể và chọn đủ ba cấp địa chỉ.", "error");
            state?.classList.add("is-invalid");
            return;
        }

        setBusy(validateButton, true);
        if (state) {
            state.textContent = "Đang kiểm tra...";
            state.classList.remove("is-valid", "is-invalid");
        }
        setMessage("");

        try {
            const result = await api.requestJson("/checkout/validate-address", {
                method: "POST",
                body: { addressLine, provinceId, districtId, wardCode }
            });
            const data = result.data;
            if (state) {
                state.textContent = "Địa chỉ hợp lệ";
                state.classList.add("is-valid");
            }
            if (canonical && canonicalText) {
                canonical.hidden = false;
                canonicalText.textContent = addressLine + ", "
                    + data.wardName + ", "
                    + data.districtName + ", "
                    + data.provinceName;
            }
            setMessage("Địa chỉ đã được xác minh. Hệ thống sẽ kiểm tra lại khi đặt hàng.", "success");
        } catch (error) {
            if (state) {
                state.textContent = "Cần kiểm tra lại";
                state.classList.add("is-invalid");
            }
            setMessage(error.message, "error");
        } finally {
            setBusy(validateButton, false);
        }
    });

    function updatePaymentVisibility() {
        const selected = paymentInputs.find((input) => input.checked)?.value;
        if (onlineOutcome) onlineOutcome.hidden = selected !== "MockOnline";
    }

    paymentInputs.forEach((input) => input.addEventListener("change", updatePaymentVisibility));
    updatePaymentVisibility();

    form?.addEventListener("submit", (event) => {
        if (!form.checkValidity()) {
            event.preventDefault();
            form.reportValidity();
            return;
        }
        setBusy(placeOrderButton, true);
        if (placeOrderButton) placeOrderButton.textContent = "Đang tạo đơn...";
    });

    async function restoreAddressSelection() {
        const initialProvince = Number.parseInt(page.dataset.initialProvince ?? "", 10);
        const initialDistrict = Number.parseInt(page.dataset.initialDistrict ?? "", 10);
        const initialWard = page.dataset.initialWard ?? "";
        if (!Number.isInteger(initialProvince) || initialProvince <= 0) return;

        provinceSelect.value = String(initialProvince);
        try {
            await loadDistricts(initialProvince, initialDistrict);
            if (Number.isInteger(initialDistrict) && initialDistrict > 0) {
                await loadWards(initialDistrict, initialWard);
            }
        } catch (error) {
            setMessage(error.message, "error");
        }
    }

    restoreAddressSelection();
})();
