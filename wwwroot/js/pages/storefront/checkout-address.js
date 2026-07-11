(() => {
    "use strict";

    const page = document.querySelector("[data-checkout-page]");
    const api = window.FastBuyCart;
    if (!page || !api) return;

    const form = page.querySelector("[data-checkout-form]");
    const provinceSelect = page.querySelector("[data-province-select]");
    const districtSelect = page.querySelector("[data-district-select]");
    const wardSelect = page.querySelector("[data-ward-select]");
    const state = page.querySelector("[data-address-validation-state]");
    const message = page.querySelector("[data-checkout-message]");
    const canonical = page.querySelector("[data-canonical-address]");
    const canonicalText = page.querySelector("[data-canonical-address-text]");

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
            state.textContent = "Chưa đối chiếu";
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
        try {
            const response = await fetch(url, {
                headers: { Accept: "application/json" },
                credentials: "same-origin"
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || payload?.success !== true) {
                throw new Error(payload?.message ?? "Không thể tải dữ liệu địa chỉ.");
            }
            setOptions(select, payload.data, placeholder, valueKey, labelKey);
        } catch (error) {
            setOptions(select, [], placeholder, valueKey, labelKey);
            setMessage(error.message, "error");
        }
    }

    provinceSelect?.addEventListener("change", async () => {
        const provinceId = Number.parseInt(provinceSelect.value, 10);
        setOptions(wardSelect, [], "Chọn phường/xã", "code", "name");
        resetValidation();
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
    });

    districtSelect?.addEventListener("change", async () => {
        const districtId = Number.parseInt(districtSelect.value, 10);
        resetValidation();
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
    });

    wardSelect?.addEventListener("change", resetValidation);

    form?.addEventListener("submit", async (event) => {
        event.preventDefault();
        const submit = form.querySelector("[data-validate-address]");
        const addressLine = form.elements.namedItem("addressLine")?.value?.trim() ?? "";
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

        setBusy(submit, true);
        if (state) {
            state.textContent = "Đang đối chiếu...";
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
                state.textContent = "Đã xác minh";
                state.classList.add("is-valid");
            }
            if (canonical && canonicalText) {
                canonical.hidden = false;
                canonicalText.textContent = addressLine + ", "
                    + data.wardName + ", "
                    + data.districtName + ", "
                    + data.provinceName;
            }
            setMessage("Địa chỉ hợp lệ. Có thể chuyển sang bước lấy báo giá GHN.", "success");
            api.toast("Địa chỉ đã được xác minh với GHN.", "success");
        } catch (error) {
            if (state) {
                state.textContent = "Cần kiểm tra lại";
                state.classList.add("is-invalid");
            }
            setMessage(error.message, "error");
        } finally {
            setBusy(submit, false);
        }
    });
})();
