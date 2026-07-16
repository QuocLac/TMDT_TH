(() => {
    "use strict";

    const page = document.querySelector("[data-checkout-page]");
    const api = window.FastBuyCart;

    if (!page || !api) {
        return;
    }

    const form = page.querySelector("[data-checkout-form]");
    const provinceSelect = page.querySelector("[data-province-select]");
    const districtSelect = page.querySelector("[data-district-select]");
    const wardSelect = page.querySelector("[data-ward-select]");
    const validateButton = page.querySelector("[data-validate-address]");
    const placeOrderButton = page.querySelector("[data-place-order]");
    const submitLabel = page.querySelector("[data-submit-label]");
    const validationState = page.querySelector(
        "[data-address-validation-state]");
    const message = page.querySelector("[data-checkout-message]");
    const canonical = page.querySelector("[data-canonical-address]");
    const canonicalText = page.querySelector(
        "[data-canonical-address-text]");
    const paymentInputs = [
        ...page.querySelectorAll('input[name="PaymentMethod"]')
    ];

    const shippingQuote = page.querySelector("[data-shipping-quote]");
    const shippingStatus = page.querySelector("[data-shipping-status]");
    const shippingMessage = page.querySelector("[data-shipping-message]");
    const shippingServiceName = page.querySelector(
        "[data-shipping-service-name]");
    const shippingFee = page.querySelector("[data-shipping-fee]");
    const grandTotal = page.querySelector("[data-grand-total]");

    const expectedShippingFee = page.querySelector(
        "[data-expected-shipping-fee]");
    const shippingServiceId = page.querySelector(
        "[data-shipping-service-id]");
    const shippingServiceTypeId = page.querySelector(
        "[data-shipping-service-type-id]");

    const currencyFormatter = new Intl.NumberFormat("vi-VN", {
        style: "currency",
        currency: "VND",
        maximumFractionDigits: 0
    });

    let quoteRequestSequence = 0;
    let quoteTimer = null;
    let quoteReady = expectedShippingFee?.value !== ""
        && Number.isFinite(Number(expectedShippingFee.value))
        && shippingServiceTypeId?.value !== ""
        && Number(shippingServiceTypeId.value) > 0;

    function selectedPaymentMethod() {
        return paymentInputs.find(input => input.checked)?.value ?? "COD";
    }

    function setBusy(element, busy) {
        if (!element) {
            return;
        }

        element.disabled = busy;
        element.setAttribute(
            "aria-busy",
            busy ? "true" : "false");
    }

    function setMessage(text, type = "info") {
        if (!message) {
            return;
        }

        message.hidden = !text;
        message.textContent = text ?? "";
        message.classList.toggle(
            "checkout-alert--danger",
            type === "error");
        message.classList.toggle(
            "checkout-alert--success",
            type === "success");
        message.classList.toggle(
            "checkout-alert--warning",
            type === "warning");
    }

    function resetValidation() {
        if (validationState) {
            validationState.textContent = "Chưa xác minh";
            validationState.classList.remove(
                "is-valid",
                "is-invalid");
        }

        if (canonical) {
            canonical.hidden = true;
        }

        setMessage("");
    }

    function clearQuote(messageText = "Chọn địa chỉ để tính phí giao hàng.") {
        quoteRequestSequence++;
        quoteReady = false;

        if (expectedShippingFee) {
            expectedShippingFee.value = "";
        }
        if (shippingServiceId) {
            shippingServiceId.value = "0";
        }
        if (shippingServiceTypeId) {
            shippingServiceTypeId.value = "0";
        }

        shippingQuote?.classList.remove("is-ready", "is-loading");
        shippingQuote?.classList.add("is-pending");

        if (shippingStatus) {
            shippingStatus.textContent = "Chờ địa chỉ";
        }
        if (shippingMessage) {
            shippingMessage.textContent = messageText;
        }
        if (shippingServiceName) {
            shippingServiceName.textContent = "Chưa xác định";
        }
        if (shippingFee) {
            shippingFee.textContent = "Chờ tính phí";
        }
        if (grandTotal) {
            grandTotal.textContent = "—";
        }

        updateSubmitState();
    }

    function setQuoteLoading() {
        quoteReady = false;
        shippingQuote?.classList.remove("is-ready", "is-pending");
        shippingQuote?.classList.add("is-loading");

        if (shippingStatus) {
            shippingStatus.textContent = "Đang tính";
        }
        if (shippingMessage) {
            shippingMessage.textContent =
                "Đang lấy biểu phí phù hợp từ GHN…";
        }
        if (shippingServiceName) {
            shippingServiceName.textContent = "Đang xác định";
        }
        if (shippingFee) {
            shippingFee.textContent = "Đang tính…";
        }
        if (grandTotal) {
            grandTotal.textContent = "Đang cập nhật…";
        }

        updateSubmitState();
    }

    function applyQuote(data) {
        quoteReady = true;

        expectedShippingFee.value = String(data.fee);
        shippingServiceId.value = String(data.serviceId);
        shippingServiceTypeId.value = String(data.serviceTypeId);

        shippingQuote?.classList.remove("is-loading", "is-pending");
        shippingQuote?.classList.add("is-ready");

        if (shippingStatus) {
            shippingStatus.textContent = data.isFallback
                ? "Phí tiêu chuẩn"
                : "Đã tính phí";
        }
        if (shippingMessage) {
            shippingMessage.textContent = data.message;
        }
        if (shippingServiceName) {
            shippingServiceName.textContent = data.serviceName;
        }
        if (shippingFee) {
            shippingFee.textContent = currencyFormatter.format(data.fee);
        }
        if (grandTotal) {
            grandTotal.textContent =
                currencyFormatter.format(data.grandTotal);
        }

        updateSubmitState();
    }

    function updateSubmitState() {
        if (!placeOrderButton) {
            return;
        }

        const addressReady =
            Number(provinceSelect?.value) > 0
            && Number(districtSelect?.value) > 0
            && Boolean(wardSelect?.value);

        const paymentReady = paymentInputs.some(
            input => input.checked && !input.disabled);

        placeOrderButton.disabled =
            !quoteReady || !addressReady || !paymentReady;
    }

    function updatePaymentPresentation() {
        const paymentMethod = selectedPaymentMethod();

        if (submitLabel) {
            submitLabel.textContent = paymentMethod === "VNPAY"
                ? "Thanh toán qua VNPay"
                : "Đặt hàng";
        }

        scheduleQuote();
    }

    function setOptions(
        select,
        items,
        placeholder,
        valueKey,
        labelKey) {
        if (!select) {
            return;
        }

        select.replaceChildren(
            new Option(placeholder, ""));

        for (const item of items ?? []) {
            select.add(
                new Option(
                    item[labelKey],
                    item[valueKey]));
        }

        select.disabled = (items ?? []).length === 0;
    }

    async function loadOptions(
        url,
        select,
        placeholder,
        valueKey,
        labelKey) {
        setOptions(
            select,
            [],
            "Đang tải…",
            valueKey,
            labelKey);

        const response = await fetch(url, {
            headers: {
                Accept: "application/json"
            },
            credentials: "same-origin"
        });

        const payload = await response
            .json()
            .catch(() => null);

        if (!response.ok || payload?.success !== true) {
            setOptions(
                select,
                [],
                placeholder,
                valueKey,
                labelKey);

            throw new Error(
                payload?.message
                ?? "Không thể tải dữ liệu địa chỉ.");
        }

        setOptions(
            select,
            payload.data,
            placeholder,
            valueKey,
            labelKey);

        return payload.data ?? [];
    }

    async function loadDistricts(
        provinceId,
        selectedDistrictId = "") {
        setOptions(
            wardSelect,
            [],
            "Chọn phường/xã",
            "code",
            "name");

        if (!Number.isInteger(provinceId)
            || provinceId <= 0) {
            setOptions(
                districtSelect,
                [],
                "Chọn quận/huyện",
                "id",
                "name");
            return;
        }

        await loadOptions(
            `/checkout/districts/${provinceId}`,
            districtSelect,
            "Chọn quận/huyện",
            "id",
            "name");

        if (selectedDistrictId) {
            districtSelect.value =
                String(selectedDistrictId);
        }
    }

    async function loadWards(
        districtId,
        selectedWardCode = "") {
        if (!Number.isInteger(districtId)
            || districtId <= 0) {
            setOptions(
                wardSelect,
                [],
                "Chọn phường/xã",
                "code",
                "name");
            return;
        }

        await loadOptions(
            `/checkout/wards/${districtId}`,
            wardSelect,
            "Chọn phường/xã",
            "code",
            "name");

        if (selectedWardCode) {
            wardSelect.value = selectedWardCode;
        }
    }

    function scheduleQuote() {
        window.clearTimeout(quoteTimer);
        clearQuote(
            "Phí giao hàng sẽ được cập nhật theo địa chỉ và phương thức thanh toán.");
        quoteTimer = window.setTimeout(
            requestShippingQuote,
            250);
    }

    async function requestShippingQuote() {
        const districtId = Number.parseInt(
            districtSelect?.value ?? "",
            10);
        const wardCode = wardSelect?.value?.trim() ?? "";

        if (!Number.isInteger(districtId)
            || districtId <= 0
            || !wardCode) {
            clearQuote();
            return;
        }

        const requestSequence = ++quoteRequestSequence;
        setQuoteLoading();

        try {
            const result = await api.requestJson(
                page.dataset.quoteUrl,
                {
                    method: "POST",
                    body: {
                        cartVersion: Number(
                            page.dataset.cartVersion),
                        districtId,
                        wardCode,
                        paymentMethod:
                            selectedPaymentMethod()
                    }
                });

            if (requestSequence !== quoteRequestSequence) {
                return;
            }

            applyQuote(result.data);
        } catch (error) {
            if (requestSequence !== quoteRequestSequence) {
                return;
            }

            clearQuote(
                error.message
                ?? "Chưa thể tính phí giao hàng.");
            setMessage(
                error.message
                ?? "Chưa thể tính phí giao hàng.",
                "error");
        }
    }

    provinceSelect?.addEventListener(
        "change",
        async () => {
            resetValidation();
            clearQuote();

            try {
                await loadDistricts(
                    Number.parseInt(
                        provinceSelect.value,
                        10));
            } catch (error) {
                setMessage(error.message, "error");
            }
        });

    districtSelect?.addEventListener(
        "change",
        async () => {
            resetValidation();
            clearQuote();

            try {
                await loadWards(
                    Number.parseInt(
                        districtSelect.value,
                        10));
            } catch (error) {
                setMessage(error.message, "error");
            }
        });

    wardSelect?.addEventListener(
        "change",
        () => {
            resetValidation();
            scheduleQuote();
        });

    form
        ?.querySelector('input[name="AddressLine"]')
        ?.addEventListener(
            "input",
            resetValidation);

    paymentInputs.forEach(input => {
        input.addEventListener(
            "change",
            updatePaymentPresentation);
    });

    validateButton?.addEventListener(
        "click",
        async () => {
            const addressLine =
                form?.elements
                    .namedItem("AddressLine")
                    ?.value
                    ?.trim()
                ?? "";

            const provinceId = Number.parseInt(
                provinceSelect?.value ?? "",
                10);
            const districtId = Number.parseInt(
                districtSelect?.value ?? "",
                10);
            const wardCode =
                wardSelect?.value?.trim()
                ?? "";

            if (addressLine.length < 3
                || !Number.isInteger(provinceId)
                || !Number.isInteger(districtId)
                || !wardCode) {
                setMessage(
                    "Hãy nhập địa chỉ cụ thể và chọn đủ ba cấp địa chỉ.",
                    "error");
                validationState?.classList.add(
                    "is-invalid");
                return;
            }

            setBusy(validateButton, true);

            if (validationState) {
                validationState.textContent =
                    "Đang xác minh…";
                validationState.classList.remove(
                    "is-valid",
                    "is-invalid");
            }

            setMessage("");

            try {
                const result = await api.requestJson(
                    "/checkout/validate-address",
                    {
                        method: "POST",
                        body: {
                            addressLine,
                            provinceId,
                            districtId,
                            wardCode
                        }
                    });

                const data = result.data;

                if (validationState) {
                    validationState.textContent =
                        "Địa chỉ hợp lệ";
                    validationState.classList.add(
                        "is-valid");
                }

                if (canonical && canonicalText) {
                    canonical.hidden = false;
                    canonicalText.textContent =
                        `${addressLine}, ${data.wardName}, `
                        + `${data.districtName}, ${data.provinceName}`;
                }

                setMessage(
                    "Địa chỉ đã được xác minh theo dữ liệu GHN.",
                    "success");

                await requestShippingQuote();
            } catch (error) {
                if (validationState) {
                    validationState.textContent =
                        "Cần kiểm tra lại";
                    validationState.classList.add(
                        "is-invalid");
                }

                setMessage(
                    error.message,
                    "error");
            } finally {
                setBusy(validateButton, false);
            }
        });

    form?.addEventListener(
        "submit",
        event => {
            if (!form.checkValidity()) {
                event.preventDefault();
                form.reportValidity();
                return;
            }

            if (!quoteReady) {
                event.preventDefault();
                setMessage(
                    "Vui lòng chờ hệ thống tính xong phí giao hàng.",
                    "warning");
                requestShippingQuote();
                return;
            }

            setBusy(placeOrderButton, true);

            if (submitLabel) {
                submitLabel.textContent =
                    selectedPaymentMethod() === "VNPAY"
                        ? "Đang chuyển tới VNPay…"
                        : "Đang ghi nhận đơn…";
            }
        });

    async function restoreAddressSelection() {
        const initialProvince = Number.parseInt(
            page.dataset.initialProvince ?? "",
            10);
        const initialDistrict = Number.parseInt(
            page.dataset.initialDistrict ?? "",
            10);
        const initialWard =
            page.dataset.initialWard ?? "";

        if (!Number.isInteger(initialProvince)
            || initialProvince <= 0) {
            updateSubmitState();
            return;
        }

        provinceSelect.value =
            String(initialProvince);

        try {
            await loadDistricts(
                initialProvince,
                initialDistrict);

            if (Number.isInteger(initialDistrict)
                && initialDistrict > 0) {
                await loadWards(
                    initialDistrict,
                    initialWard);
            }

            if (initialWard) {
                await requestShippingQuote();
            }
        } catch (error) {
            clearQuote(error.message);
            setMessage(error.message, "error");
        }
    }

    updatePaymentPresentation();
    restoreAddressSelection();
})();
