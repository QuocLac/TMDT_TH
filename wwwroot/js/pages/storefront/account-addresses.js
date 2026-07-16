(() => {
    "use strict";

    const page = document.querySelector("[data-address-book]");
    if (!page) return;

    const form = page.querySelector("[data-address-form]");
    const province = form?.querySelector("[data-province]");
    const district = form?.querySelector("[data-district]");
    const ward = form?.querySelector("[data-ward]");
    if (!form || !province || !district || !ward) return;

    const selectedFromServer = {
        provinceId: String(page.dataset.initialProvince || ""),
        districtId: String(page.dataset.initialDistrict || ""),
        wardCode: String(page.dataset.initialWard || "")
    };

    async function fetchItems(url) {
        const response = await fetch(url, {
            headers: { Accept: "application/json" },
            credentials: "same-origin"
        });
        const payload = await response.json();
        if (!response.ok || payload.success !== true) {
            throw new Error(payload.message || "Không thể tải dữ liệu địa chỉ.");
        }
        return payload.data || [];
    }

    function setOptions(select, items, placeholder, valueKey, labelKey) {
        select.innerHTML = "";
        const empty = document.createElement("option");
        empty.value = "";
        empty.textContent = placeholder;
        select.append(empty);

        for (const item of items) {
            const option = document.createElement("option");
            option.value = String(item[valueKey]);
            option.textContent = item[labelKey];
            select.append(option);
        }

        select.disabled = items.length === 0;
    }

    async function loadProvinces(selectedValue = "") {
        const items = await fetchItems("/checkout/provinces");
        setOptions(province, items, "Chọn tỉnh/thành phố", "id", "name");
        province.value = String(selectedValue || "");
    }

    async function loadDistricts(provinceId, selectedValue = "") {
        if (!provinceId) {
            setOptions(district, [], "Chọn quận/huyện", "id", "name");
            setOptions(ward, [], "Chọn phường/xã", "code", "name");
            return;
        }

        const items = await fetchItems(`/checkout/districts/${encodeURIComponent(provinceId)}`);
        setOptions(district, items, "Chọn quận/huyện", "id", "name");
        district.value = String(selectedValue || "");
    }

    async function loadWards(districtId, selectedValue = "") {
        if (!districtId) {
            setOptions(ward, [], "Chọn phường/xã", "code", "name");
            return;
        }

        const items = await fetchItems(`/checkout/wards/${encodeURIComponent(districtId)}`);
        setOptions(ward, items, "Chọn phường/xã", "code", "name");
        ward.value = String(selectedValue || "");
    }

    async function selectAddress(card) {
        form.querySelector("[data-address-id]").value = card.dataset.id || "";
        form.querySelector("[data-recipient]").value = card.dataset.recipient || "";
        form.querySelector("[data-phone]").value = card.dataset.phone || "";
        form.querySelector("[data-street]").value = card.dataset.street || "";

        const provinceId = card.dataset.provinceId || "";
        const districtId = card.dataset.districtId || "";
        const wardCode = card.dataset.wardCode || "";

        await loadProvinces(provinceId);
        await loadDistricts(provinceId, districtId);
        await loadWards(districtId, wardCode);
        form.scrollIntoView({ behavior: "smooth", block: "start" });
    }

    page.addEventListener("click", async (event) => {
        const edit = event.target.closest("[data-address-edit]");
        if (edit) {
            const card = edit.closest("[data-address]");
            if (card) await selectAddress(card);
            return;
        }

        if (event.target.closest("[data-address-reset]")) {
            form.reset();
            form.querySelector("[data-address-id]").value = "";
            await loadProvinces();
            setOptions(district, [], "Chọn quận/huyện", "id", "name");
            setOptions(ward, [], "Chọn phường/xã", "code", "name");
        }
    });

    province.addEventListener("change", async () => {
        await loadDistricts(province.value);
        setOptions(ward, [], "Chọn phường/xã", "code", "name");
    });

    district.addEventListener("change", async () => {
        await loadWards(district.value);
    });

    (async () => {
        try {
            await loadProvinces(selectedFromServer.provinceId);
            await loadDistricts(
                selectedFromServer.provinceId,
                selectedFromServer.districtId);
            await loadWards(
                selectedFromServer.districtId,
                selectedFromServer.wardCode);
        } catch (error) {
            console.error(error);
        }
    })();
})();
