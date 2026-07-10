(() => {
    "use strict";

    const originalHttp = window.FastBuyHttp;
    if (!originalHttp || originalHttp.__pricingConfirmationIntegrity) {
        return;
    }

    let reviewedFingerprint = null;
    let savedFingerprint = null;

    function getEditor() {
        return document.querySelector("[data-price-campaign-editor]");
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

    function normalizeText(value) {
        return String(value ?? "").trim();
    }

    function readEditorMetadata() {
        const root = getEditor();
        if (!root) return null;

        const value = (selector) => root.querySelector(selector)?.value ?? "";
        return {
            name: normalizeText(value("[data-campaign-name]")),
            description: normalizeText(value("[data-campaign-description]")),
            reason: normalizeText(value("[data-campaign-reason]")),
            sourceType: normalizeText(value("[data-campaign-source]"))
        };
    }

    function normalizeItems(items) {
        if (!Array.isArray(items)) return [];

        return items
            .map((item) => ({
                variantId: Number(item.variantId),
                rowVersion: String(item.rowVersion ?? item.variantRowVersion ?? ""),
                listPrice: normalizeNumber(item.listPrice),
                currentPrice: normalizeNumber(item.currentPrice),
                newPrice: normalizeNumber(item.newPrice),
                adjustmentType: normalizeText(item.adjustmentType),
                adjustmentValue: normalizeNumber(item.adjustmentValue)
            }))
            .sort((left, right) => left.variantId - right.variantId);
    }

    function createFingerprint(requestBody, responseItems) {
        const body = requestBody ?? {};
        return JSON.stringify({
            campaignId: Number(body.campaignId ?? body.id ?? 0),
            mode: normalizeText(body.mode),
            startDate: normalizeText(body.startDate),
            endDate: normalizeText(body.endDate),
            conflictPolicy: normalizeText(body.conflictPolicy),
            metadata: readEditorMetadata(),
            items: normalizeItems(responseItems)
        });
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
                "Thông tin kế hoạch, chính sách hoặc giá biến thể đã thay đổi sau bước xem trước. "
                + "Bản nháp mới nhất đã được lưu nhưng chưa được xác nhận. "
                + "Hãy kiểm tra lại danh sách chờ và mở xác nhận một lần nữa."
        };
    }

    async function postJson(url, body, signal) {
        if (getEditor()
            && isConfirmationOpen()
            && isPricingAction(url, "ConfirmDraft")
            && reviewedFingerprint
            && savedFingerprint
            && reviewedFingerprint !== savedFingerprint) {
            return requireReviewBeforeConfirmation();
        }

        const result = await originalHttp.postJson(url, body, signal);

        if (!getEditor()) {
            return result;
        }

        if (isPricingAction(url, "PreviewPlan") && result?.success) {
            reviewedFingerprint = createFingerprint(body, result.data?.items);
            return result;
        }

        if (isPricingAction(url, "SaveDraft") && result?.success) {
            savedFingerprint = createFingerprint(body, result.data?.items);
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
