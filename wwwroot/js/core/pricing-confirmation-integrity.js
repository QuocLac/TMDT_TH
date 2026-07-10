(() => {
    "use strict";

    const originalHttp = window.FastBuyHttp;
    if (!originalHttp || originalHttp.__pricingConfirmationIntegrity) {
        return;
    }

    let reviewedFingerprint = null;
    let savedFingerprint = null;

    function isPricingEditor() {
        return Boolean(document.querySelector("[data-price-campaign-editor]"));
    }

    function getConfirmationDialog() {
        return document.querySelector("[data-confirm-dialog]");
    }

    function isConfirmationOpen() {
        return Boolean(getConfirmationDialog()?.open);
    }

    function isPricingAction(url, action) {
        const normalizedUrl = String(url ?? "");
        return normalizedUrl === `/Admin/PriceCampaigns/${action}`
            || normalizedUrl.endsWith(`/PriceCampaigns/${action}`);
    }

    function normalizeNumber(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number : null;
    }

    function createFingerprint(items) {
        if (!Array.isArray(items)) {
            return null;
        }

        const normalizedItems = items
            .map((item) => ({
                variantId: Number(item.variantId),
                rowVersion: String(item.rowVersion ?? ""),
                listPrice: normalizeNumber(item.listPrice),
                currentPrice: normalizeNumber(item.currentPrice),
                newPrice: normalizeNumber(item.newPrice),
                adjustmentType: String(item.adjustmentType ?? ""),
                adjustmentValue: normalizeNumber(item.adjustmentValue)
            }))
            .sort((left, right) => left.variantId - right.variantId);

        return JSON.stringify(normalizedItems);
    }

    function requireReviewBeforeConfirmation() {
        const dialog = getConfirmationDialog();
        if (dialog?.open) {
            dialog.close();
        }

        document.querySelector("[data-staging-list]")
            ?.scrollIntoView({ behavior: "smooth", block: "start" });

        reviewedFingerprint = null;

        return {
            success: false,
            canConfirm: false,
            errorCode: "DRAFT_REVIEW_REQUIRED",
            message:
                "Giá hoặc dữ liệu biến thể đã thay đổi sau bước xem trước. "
                + "Bản nháp mới nhất đã được lưu nhưng chưa được xác nhận. "
                + "Hãy kiểm tra lại danh sách chờ và mở xác nhận một lần nữa."
        };
    }

    async function postJson(url, body, signal) {
        if (isPricingEditor()
            && isConfirmationOpen()
            && isPricingAction(url, "ConfirmDraft")
            && reviewedFingerprint
            && savedFingerprint
            && reviewedFingerprint !== savedFingerprint) {
            return requireReviewBeforeConfirmation();
        }

        const result = await originalHttp.postJson(url, body, signal);

        if (!isPricingEditor()) {
            return result;
        }

        if (isPricingAction(url, "PreviewPlan") && result?.success) {
            const fingerprint = createFingerprint(result.data?.items);
            if (fingerprint) {
                reviewedFingerprint = fingerprint;
            }

            return result;
        }

        if (isPricingAction(url, "SaveDraft") && result?.success) {
            const fingerprint = createFingerprint(result.data?.items);
            if (fingerprint) {
                savedFingerprint = fingerprint;
            }

            return result;
        }

        if (isPricingAction(url, "ConfirmDraft") && result?.success) {
            reviewedFingerprint = null;
            savedFingerprint = null;
        }

        return result;
    }

    window.FastBuyHttp = Object.freeze({
        ...originalHttp,
        postJson,
        __pricingConfirmationIntegrity: true
    });
})();
