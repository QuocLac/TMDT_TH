(() => {
    "use strict";

    const root = document.querySelector(
        "[data-price-risk-page]");
    const http = window.FastBuyHttp;

    if (!root || !http) {
        return;
    }

    const checkboxes = [
        ...root.querySelectorAll(
            "[data-drift-checkbox]:not(:disabled)")
    ];
    const selectAll = root.querySelector(
        "[data-select-all-drift]");
    const reconcileButton = root.querySelector(
        "[data-reconcile-selected]");
    const feedback = root.querySelector(
        "[data-price-risk-feedback]");

    function selectedIds() {
        return checkboxes
            .filter(input => input.checked)
            .map(input => Number(input.value))
            .filter(Number.isInteger);
    }

    function updateState() {
        const selected = selectedIds();

        if (reconcileButton) {
            reconcileButton.disabled =
                selected.length === 0;
            reconcileButton.textContent =
                selected.length === 0
                    ? "Đối soát SKU đã chọn"
                    : `Đối soát ${selected.length} SKU`;
        }

        if (selectAll) {
            const selectedCount = selected.length;
            selectAll.checked =
                checkboxes.length > 0
                && selectedCount === checkboxes.length;
            selectAll.indeterminate =
                selectedCount > 0
                && selectedCount < checkboxes.length;
        }
    }

    function setFeedback(message, tone = "") {
        if (!feedback) {
            return;
        }

        feedback.textContent = message ?? "";
        feedback.classList.remove(
            "is-error",
            "is-success");

        if (tone) {
            feedback.classList.add(`is-${tone}`);
        }
    }

    selectAll?.addEventListener("change", () => {
        checkboxes.forEach(input => {
            input.checked = selectAll.checked;
        });
        updateState();
    });

    checkboxes.forEach(input => {
        input.addEventListener(
            "change",
            updateState);
    });

    reconcileButton?.addEventListener(
        "click",
        async () => {
            const variantIds = selectedIds();
            if (!variantIds.length) {
                return;
            }

            const accepted = window.confirm(
                `Đối soát lại projection cho ${variantIds.length} SKU? `
                + "Hệ thống chỉ ghi lịch sử khi giá hiệu lực thực sự thay đổi.");

            if (!accepted) {
                return;
            }

            reconcileButton.disabled = true;
            setFeedback(
                "Đang tính lại giá hiệu lực và nguồn giá...");

            try {
                const result = await http.postJson(
                    "/Admin/PriceRisks/Reconcile",
                    {
                        variantIds,
                        reason:
                            "Đối soát thủ công từ trung tâm rủi ro giá"
                    });

                if (!result.success) {
                    const diagnostics = [
                        result.errorCode
                            ? `Mã lỗi: ${result.errorCode}`
                            : null,
                        result.correlationId
                            ? `Mã tra cứu: ${result.correlationId}`
                            : null
                    ].filter(Boolean);

                    throw new Error(
                        `${result.message ?? "Không thể đối soát giá."}`
                        + (diagnostics.length
                            ? ` (${diagnostics.join(" · ")})`
                            : ""));
                }

                setFeedback(
                    result.message,
                    "success");

                window.setTimeout(
                    () => window.location.reload(),
                    750);
            } catch (error) {
                setFeedback(
                    error instanceof Error
                        ? error.message
                        : "Không thể đối soát giá.",
                    "error");
                updateState();
            }
        });

    updateState();
})();
