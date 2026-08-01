(() => {
    "use strict";

    const styleVersion = "2026.07.31.1";
    const adjustmentBySku = new Map();
    let httpHookInstalled = false;

    const definitions = Object.freeze({
        FixedPrice: {
            option: "Đặt giá cố định",
            label: "Giá mới",
            placeholder: "Nhập giá bán mới",
            help: "Giá cố định áp dụng giống nhau cho toàn bộ SKU đã chọn."
        },
        PercentIncrease: {
            option: "Tăng theo phần trăm",
            label: "Phần trăm tăng",
            placeholder: "Ví dụ: 10",
            help: "Tính từ giá niêm yết của từng SKU. Tối đa 1000%."
        },
        AmountIncrease: {
            option: "Tăng số tiền",
            label: "Số tiền tăng",
            placeholder: "Ví dụ: 50000",
            help: "Cộng số tiền vào giá niêm yết của từng SKU."
        },
        PercentOff: {
            option: "Giảm theo phần trăm",
            label: "Phần trăm giảm",
            placeholder: "Ví dụ: 10",
            help: "Tính từ giá niêm yết của từng SKU và phải nhỏ hơn 100%."
        },
        AmountOff: {
            option: "Giảm số tiền",
            label: "Số tiền giảm",
            placeholder: "Ví dụ: 50000",
            help: "Trừ số tiền khỏi giá niêm yết của từng SKU."
        }
    });

    function ensureStylesheet() {
        if (document.querySelector(
                "[data-pricing-enhancement-styles]")) {
            return;
        }

        const currentScript = document.currentScript;
        const url = currentScript?.src
            ? new URL(
                "../../../css/pages/admin/price-campaigns-enhancements.css",
                currentScript.src)
            : new URL(
                "/css/pages/admin/price-campaigns-enhancements.css",
                window.location.origin);

        url.searchParams.set("v", styleVersion);

        const link = document.createElement("link");
        link.rel = "stylesheet";
        link.href = url.href;
        link.dataset.pricingEnhancementStyles = "true";
        document.head.append(link);
    }

    function captureItems(items) {
        (items ?? []).forEach(item => {
            if (!item?.sku || !item?.adjustmentType) {
                return;
            }

            adjustmentBySku.set(item.sku, {
                type: item.adjustmentType,
                value: Number(item.adjustmentValue)
            });
        });

        rewriteStagingDescriptions();
    }

    function installHttpHook() {
        if (httpHookInstalled || !window.FastBuyHttp) {
            return Boolean(window.FastBuyHttp);
        }

        const http = window.FastBuyHttp;
        const originalPostJson = http.postJson.bind(http);
        const originalGetJson = http.getJson.bind(http);

        http.postJson = async (url, payload, ...rest) => {
            const result = await originalPostJson(
                url,
                payload,
                ...rest);

            if (String(url).includes(
                    "/Admin/PriceCampaigns/")) {
                captureItems(result?.data?.items);
            }

            return result;
        };

        http.getJson = async (url, ...rest) => {
            const result = await originalGetJson(
                url,
                ...rest);

            if (String(url).includes(
                    "/Admin/PriceCampaigns/GetCampaign")) {
                captureItems(result?.data?.items);
            }

            return result;
        };

        httpHookInstalled = true;
        return true;
    }

    function ensureAdjustmentOptions(select) {
        if (!select) {
            return;
        }

        const desiredOrder = [
            "FixedPrice",
            "PercentIncrease",
            "AmountIncrease",
            "PercentOff",
            "AmountOff"
        ];

        const existing = new Map(
            [...select.options].map(option => [
                option.value,
                option
            ]));

        desiredOrder.forEach(value => {
            let option = existing.get(value);

            if (!option) {
                option = document.createElement("option");
                option.value = value;
            }

            option.textContent =
                definitions[value].option;
            select.append(option);
        });
    }

    function syncAdjustmentControl(root) {
        const select = root.querySelector(
            "[data-adjustment-type]");
        const input = root.querySelector(
            "[data-adjustment-value]");
        const label = root.querySelector(
            "[data-adjustment-label]");
        const preview = root.querySelector(
            "[data-configuration-preview]");

        if (!select || !input || !label) {
            return;
        }

        ensureAdjustmentOptions(select);

        const definition =
            definitions[select.value]
            ?? definitions.FixedPrice;

        label.textContent = definition.label;
        input.placeholder = definition.placeholder;
        input.min = "0.01";
        input.step = "0.01";

        if (select.value === "PercentOff") {
            input.max = "99.99";
        } else if (
            select.value === "PercentIncrease") {
            input.max = "1000";
        } else {
            input.removeAttribute("max");
        }

        if (preview
            && !preview.textContent.includes("Đang ")) {
            preview.textContent = definition.help;
        }
    }

    function describeAdjustment(type, value) {
        const number = Number(value);

        if (type === "PercentIncrease") {
            return `Tăng ${number}%`;
        }

        if (type === "AmountIncrease") {
            return `Tăng ${formatCurrency(number)}`;
        }

        if (type === "PercentOff") {
            return `Giảm ${number}%`;
        }

        if (type === "AmountOff") {
            return `Giảm ${formatCurrency(number)}`;
        }

        return `Giá cố định ${formatCurrency(number)}`;
    }

    function formatCurrency(value) {
        return new Intl.NumberFormat("vi-VN", {
            style: "currency",
            currency: "VND",
            maximumFractionDigits: 0
        }).format(value);
    }

    function rewriteStagingDescriptions() {
        document.querySelectorAll(
            ".pricing-staging-row").forEach(row => {
            const sku =
                row.children[0]
                    ?.querySelector("strong")
                    ?.textContent
                    ?.trim();

            const adjustment = adjustmentBySku.get(sku);
            const description =
                row.children[3]
                    ?.querySelector("strong");

            if (!adjustment || !description) {
                return;
            }

            description.textContent =
                describeAdjustment(
                    adjustment.type,
                    adjustment.value);
        });
    }

    async function hydrateExistingCampaign(root) {
        const campaignId = Number(
            root.dataset.campaignId ?? 0);

        if (!campaignId || !window.FastBuyHttp) {
            return;
        }

        try {
            const result =
                await window.FastBuyHttp.getJson(
                    `/Admin/PriceCampaigns/GetCampaign/${campaignId}`);

            if (result?.success) {
                captureItems(result.data?.items);
            }
        } catch {
            // The main page already owns error reporting.
        }
    }

    function initializeEditor(root) {
        const select = root.querySelector(
            "[data-adjustment-type]");
        const dialog = root.querySelector(
            "[data-price-config-dialog]");

        ensureAdjustmentOptions(select);
        syncAdjustmentControl(root);

        select?.addEventListener("change", () => {
            window.setTimeout(
                () => syncAdjustmentControl(root),
                0);
        });

        dialog?.addEventListener("toggle", () => {
            if (dialog.open) {
                window.setTimeout(
                    () => syncAdjustmentControl(root),
                    0);
            }
        });

        const observer = new MutationObserver(() => {
            rewriteStagingDescriptions();

            if (dialog?.open) {
                syncAdjustmentControl(root);
            }
        });

        observer.observe(root, {
            childList: true,
            subtree: true
        });

        hydrateExistingCampaign(root);
    }

    function initialize() {
        ensureStylesheet();

        if (!installHttpHook()) {
            let attempts = 0;
            const timer = window.setInterval(() => {
                attempts++;
                if (installHttpHook()
                    || attempts >= 100) {
                    window.clearInterval(timer);
                }
            }, 50);
        }

        const editor = document.querySelector(
            "[data-price-campaign-editor]");

        if (editor) {
            initializeEditor(editor);
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initialize,
            { once: true });
    } else {
        initialize();
    }
})();
