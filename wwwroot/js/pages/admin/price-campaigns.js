(() => {
    "use strict";

    const http = window.FastBuyHttp;
    if (!http) return;

    const currency = new Intl.NumberFormat("vi-VN", {
        style: "currency",
        currency: "VND",
        maximumFractionDigits: 0
    });

    const dateTime = new Intl.DateTimeFormat("vi-VN", {
        dateStyle: "short",
        timeStyle: "short"
    });

    function setFeedback(container, message, type = "") {
        if (!container) return;
        container.textContent = message ?? "";
        container.classList.remove("is-error", "is-success", "is-warning");
        if (type) container.classList.add(`is-${type}`);
    }

    function createText(tag, className, text) {
        const element = document.createElement(tag);
        if (className) element.className = className;
        element.textContent = text ?? "";
        return element;
    }

    function createButton(text, className, action) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = className;
        button.textContent = text;
        if (action) button.addEventListener("click", action);
        return button;
    }

    function formatRange(minimum, maximum) {
        if (minimum == null || maximum == null) return "Chưa có giá";
        return Number(minimum) === Number(maximum)
            ? currency.format(minimum)
            : `${currency.format(minimum)} – ${currency.format(maximum)}`;
    }

    function formatLocalDate(isoValue) {
        if (!isoValue) return "Không thời hạn";
        const value = new Date(isoValue);
        return Number.isNaN(value.getTime())
            ? "Không xác định"
            : dateTime.format(value);
    }

    function toLocalInput(isoValue) {
        if (!isoValue) return "";
        const date = new Date(isoValue);
        if (Number.isNaN(date.getTime())) return "";
        const offset = date.getTimezoneOffset() * 60000;
        return new Date(date.getTime() - offset)
            .toISOString()
            .slice(0, 16);
    }

    function createRequestId() {
        if (window.crypto?.randomUUID) {
            return window.crypto.randomUUID().replaceAll("-", "");
        }

        return `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    }

    function isValidRowVersion(value) {
        if (typeof value !== "string" || !value.trim()) return false;

        try {
            return window.atob(value.trim()).length === 8;
        } catch {
            return false;
        }
    }

    function initializeDefaultPlanDates(root) {
        if (Number(root.dataset.campaignId ?? 0) > 0) return;

        const startInput = root.querySelector("[data-campaign-start]");
        const endInput = root.querySelector("[data-campaign-end]");
        if (!startInput || !endInput || startInput.value) return;

        const start = new Date(Date.now() + 5 * 60 * 1000);
        start.setSeconds(0, 0);
        const end = new Date(start.getTime() + 24 * 60 * 60 * 1000);
        startInput.value = toLocalInput(start.toISOString());
        endInput.value = toLocalInput(end.toISOString());
    }

    function formatServerError(result, fallback) {
        const message = result?.message ?? fallback;
        return result?.correlationId
            ? `${message} (Mã tra cứu: ${result.correlationId})`
            : message;
    }

    function translateCampaignStatus(status) {
        return {
            Draft: "Bản nháp",
            Confirmed: "Đã xác nhận",
            Scheduled: "Đã lên lịch",
            Active: "Đang hiệu lực",
            Completed: "Đã kết thúc",
            Cancelled: "Đã hủy",
            Superseded: "Đã thay thế"
        }[status] ?? status;
    }

    function translateConflictPolicy(policy) {
        return {
            Reject: "Từ chối chồng lấn",
            ReplaceFromStart: "Thay thế từ lúc bắt đầu",
            SupersedeNow: "Thay thế ngay"
        }[policy] ?? policy;
    }

    function initializeIndex(root) {
        const feedback = root.querySelector("[data-page-feedback]");
        const lifecycleDialog = root.querySelector("[data-lifecycle-dialog]");
        const lifecycleForm = root.querySelector("[data-lifecycle-form]");
        const lifecycleTitle = root.querySelector("[data-lifecycle-title]");
        const lifecycleDescription = root.querySelector("[data-lifecycle-description]");
        const lifecycleSummary = root.querySelector("[data-lifecycle-summary]");
        const lifecycleWarning = root.querySelector("[data-lifecycle-warning]");
        const lifecycleReason = root.querySelector("[data-lifecycle-reason]");
        const lifecycleConfirm = root.querySelector("[data-lifecycle-confirm]");
        const timelineDialog = root.querySelector("[data-timeline-dialog]");
        const timelineTitle = root.querySelector("[data-timeline-title]");
        const timelineSubtitle = root.querySelector("[data-timeline-subtitle]");
        const timelineBody = root.querySelector("[data-timeline-body]");

        const lifecycleState = {
            operation: null,
            campaignId: 0,
            rowVersion: "",
            code: "",
            name: "",
            status: "",
            clientRequestId: ""
        };

        root.querySelectorAll("[data-local-datetime]")
            .forEach((element) => {
                const date = new Date(element.dateTime);
                if (!Number.isNaN(date.getTime())) {
                    element.textContent = dateTime.format(date);
                }
            });

        function openLifecycle(row, operation) {
            lifecycleState.operation = operation;
            lifecycleState.campaignId = Number(row.dataset.campaignId);
            lifecycleState.rowVersion = row.dataset.rowVersion ?? "";
            lifecycleState.code = row.dataset.campaignCode ?? "";
            lifecycleState.name = row.dataset.campaignName ?? "";
            lifecycleState.status = row.dataset.campaignStatus ?? "";
            lifecycleState.clientRequestId = operation === "recover"
                ? createRequestId()
                : "";
            lifecycleReason.value = "";
            lifecycleConfirm.disabled = false;
            lifecycleSummary.replaceChildren();
            lifecycleSummary.append(
                createText("strong", "", `${lifecycleState.code} · ${lifecycleState.name}`),
                createText("span", "", `Trạng thái hiện tại: ${translateCampaignStatus(lifecycleState.status)}`));

            const isApply = operation === "apply";
            const isCancel = operation === "cancel";
            lifecycleReason.closest(".form-field").hidden = isApply;
            lifecycleReason.required = !isApply;
            lifecycleConfirm.className = isCancel
                ? "btn btn-danger"
                : "btn btn-primary";

            if (isApply) {
                lifecycleTitle.textContent = "Kích hoạt kế hoạch ngay";
                lifecycleDescription.textContent = "Thời điểm bắt đầu sẽ được chuyển về hiện tại và server kiểm tra lại toàn bộ xung đột.";
                lifecycleWarning.textContent = "Giá storefront có thể thay đổi ngay sau khi transaction hoàn tất.";
                lifecycleConfirm.textContent = "Kích hoạt ngay";
            } else if (isCancel) {
                lifecycleTitle.textContent = "Dừng / hủy kế hoạch";
                lifecycleDescription.textContent = "Kế hoạch sẽ chuyển sang trạng thái đã hủy và giá hiệu lực được tính lại.";
                lifecycleWarning.textContent = "Biến thể sẽ quay về kế hoạch còn hiệu lực tiếp theo hoặc giá niêm yết.";
                lifecycleConfirm.textContent = "Xác nhận hủy";
                lifecycleReason.placeholder = "Ví dụ: Sai dữ liệu giá, dừng theo quyết định vận hành...";
            } else {
                lifecycleTitle.textContent = "Phục hồi giá trước kế hoạch";
                lifecycleDescription.textContent = "Hệ thống tạo một kế hoạch Recovery mới, áp dụng giá hiệu lực trước đó và thay thế giá đang chạy ngay.";
                lifecycleWarning.textContent = "Các biến thể trong phạm vi phục hồi sẽ được thay thế ngay; biến thể ngoài phạm vi được giữ bằng kế hoạch tiếp tục tự động.";
                lifecycleConfirm.textContent = "Tạo và áp dụng phục hồi";
                lifecycleReason.placeholder = "Ví dụ: Hoàn tác kế hoạch giá do sai cấu hình...";
            }

            lifecycleDialog.showModal();
            if (!isApply) lifecycleReason.focus();
        }

        async function submitLifecycle() {
            const reason = lifecycleReason.value.trim();
            if (lifecycleState.operation !== "apply" && !reason) {
                lifecycleReason.setCustomValidity("Vui lòng nhập lý do thao tác.");
                lifecycleReason.reportValidity();
                lifecycleReason.setCustomValidity("");
                return;
            }

            const endpoint = lifecycleState.operation === "apply"
                ? "/Admin/PriceCampaigns/ApplyNow"
                : lifecycleState.operation === "cancel"
                    ? "/Admin/PriceCampaigns/CancelCampaign"
                    : "/Admin/PriceCampaigns/RecoverCampaign";
            const payload = {
                id: lifecycleState.campaignId,
                rowVersion: lifecycleState.rowVersion
            };

            if (lifecycleState.operation !== "apply") {
                payload.reason = reason;
            }
            if (lifecycleState.operation === "recover") {
                payload.clientRequestId = lifecycleState.clientRequestId;
            }

            lifecycleConfirm.disabled = true;
            setFeedback(feedback, "Đang xử lý thao tác vòng đời...");

            try {
                const result = await http.postJson(endpoint, payload);
                if (!result.success) {
                    throw new Error(formatServerError(
                        result,
                        "Không thể thực hiện thao tác vòng đời."));
                }

                lifecycleDialog.close();
                setFeedback(feedback, result.message, "success");
                window.setTimeout(() => window.location.reload(), 650);
            } catch (error) {
                setFeedback(feedback, error.message, "error");
                lifecycleConfirm.disabled = false;
            }
        }

        function appendDefinition(container, label, value) {
            const item = document.createElement("div");
            item.append(
                createText("span", "", label),
                createText("strong", "", value ?? "—"));
            container.append(item);
        }

        function renderTimeline(data) {
            timelineBody.replaceChildren();
            const campaign = data.campaign;
            timelineTitle.textContent = campaign.name;
            timelineSubtitle.textContent = `${campaign.code} · ${translateCampaignStatus(campaign.status)}`;

            const overview = document.createElement("section");
            overview.className = "timeline-overview";
            appendDefinition(overview, "Nguồn giá", campaign.sourceType);
            appendDefinition(overview, "Chính sách", translateConflictPolicy(campaign.conflictPolicy));
            appendDefinition(overview, "Bắt đầu", formatLocalDate(campaign.startDateUtc));
            appendDefinition(overview, "Kết thúc", formatLocalDate(campaign.endDateUtc));
            appendDefinition(overview, "Người tạo", campaign.createdBy);
            appendDefinition(overview, "Lý do", campaign.reason);
            timelineBody.append(overview);

            if (data.supersededBy || data.supersededCampaigns?.length) {
                const relations = document.createElement("section");
                relations.className = "timeline-relations";
                relations.append(createText("h4", "", "Quan hệ thay thế"));
                if (data.supersededBy) {
                    relations.append(createText(
                        "p",
                        "",
                        `Bị thay thế bởi ${data.supersededBy.code} · ${data.supersededBy.name}`));
                }
                (data.supersededCampaigns ?? []).forEach((item) => {
                    relations.append(createText(
                        "p",
                        "",
                        `Đã thay thế ${item.code} · ${item.name}`));
                });
                timelineBody.append(relations);
            }

            const variantSection = document.createElement("section");
            variantSection.className = "timeline-variants";
            variantSection.append(createText("h4", "", `Biến thể (${data.variants?.length ?? 0})`));
            const tableShell = document.createElement("div");
            tableShell.className = "table-responsive";
            const table = document.createElement("table");
            table.className = "table table-sm align-middle";
            const thead = document.createElement("thead");
            const headRow = document.createElement("tr");
            ["SKU", "Sản phẩm", "Giá trước", "Giá kế hoạch", "Giá hiện tại"].forEach((label) => {
                headRow.append(createText("th", "", label));
            });
            thead.append(headRow);
            const tbody = document.createElement("tbody");
            (data.variants ?? []).forEach((variant) => {
                const row = document.createElement("tr");
                row.append(
                    createText("td", "", variant.sku),
                    createText("td", "", `${variant.productName}${variant.attributes ? ` · ${variant.attributes}` : ""}`),
                    createText("td", "", currency.format(variant.previousPrice)),
                    createText("td", "", currency.format(variant.campaignPrice)),
                    createText("td", "", currency.format(variant.currentPrice)));
                tbody.append(row);
            });
            table.append(thead, tbody);
            tableShell.append(table);
            variantSection.append(tableShell);
            timelineBody.append(variantSection);

            const eventSection = document.createElement("section");
            eventSection.className = "audit-timeline";
            eventSection.append(createText("h4", "", "Dòng thời gian kiểm toán"));
            const eventList = document.createElement("div");
            eventList.className = "audit-timeline__list";
            (data.events ?? []).forEach((entry) => {
                const card = document.createElement("article");
                card.className = `audit-event audit-event--${String(entry.kind ?? "event").toLowerCase()}`;
                const header = document.createElement("header");
                header.append(
                    createText("strong", "", entry.title),
                    createText("time", "", formatLocalDate(entry.occurredAtUtc)));
                card.append(header, createText("p", "", entry.description));
                if (entry.sku) {
                    card.append(createText(
                        "small",
                        "",
                        `${entry.sku}: ${currency.format(entry.oldPrice)} → ${currency.format(entry.newPrice)}`));
                }
                if (entry.changedBy || entry.correlationId) {
                    card.append(createText(
                        "small",
                        "",
                        [entry.changedBy, entry.correlationId ? `Mã ${entry.correlationId}` : null]
                            .filter(Boolean)
                            .join(" · ")));
                }
                eventList.append(card);
            });
            eventSection.append(eventList);
            timelineBody.append(eventSection);
        }

        async function openTimeline(row) {
            timelineTitle.textContent = "Chi tiết kế hoạch";
            timelineSubtitle.textContent = `${row.dataset.campaignCode ?? ""} · Đang tải`;
            timelineBody.replaceChildren(createText(
                "div",
                "pricing-loading-state",
                "Đang tải lịch sử kế hoạch..."));
            timelineDialog.showModal();

            try {
                const result = await http.getJson(
                    `/Admin/PriceCampaigns/GetTimeline/${Number(row.dataset.campaignId)}`);
                if (!result.success) {
                    throw new Error(result.message ?? "Không thể tải lịch sử kế hoạch.");
                }
                renderTimeline(result.data);
            } catch (error) {
                timelineBody.replaceChildren(createText(
                    "div",
                    "page-feedback is-error",
                    error.message));
            }
        }

        lifecycleForm?.addEventListener("submit", (event) => {
            event.preventDefault();
            submitLifecycle();
        });
        root.querySelectorAll("[data-close-lifecycle]").forEach((button) => {
            button.addEventListener("click", () => lifecycleDialog.close());
        });
        root.querySelectorAll("[data-close-timeline]").forEach((button) => {
            button.addEventListener("click", () => timelineDialog.close());
        });

        root.addEventListener("click", (event) => {
            const target = event.target instanceof Element
                ? event.target
                : null;
            const row = target?.closest("[data-campaign-row]");
            if (!row) return;

            if (target.closest("[data-view-timeline]")) {
                openTimeline(row);
                return;
            }
            if (target.closest("[data-apply-campaign]")) {
                openLifecycle(row, "apply");
                return;
            }
            if (target.closest("[data-cancel-campaign]")) {
                openLifecycle(row, "cancel");
                return;
            }
            if (target.closest("[data-recover-campaign]")) {
                openLifecycle(row, "recover");
            }
        });
    }

    function initializeEditor(root) {
        const feedback = root.querySelector("[data-page-feedback]");
        const productBody = root.querySelector("[data-product-table-body]");
        const stagingList = root.querySelector("[data-staging-list]");
        const configDialog = root.querySelector("[data-price-config-dialog]");
        const confirmDialog = root.querySelector("[data-confirm-dialog]");
        const configForm = root.querySelector("[data-price-config-form]");
        const confirmForm = root.querySelector("[data-confirm-form]");
        const rowVersionInput = root.querySelector("[data-campaign-row-version]");
        const clientRequestIdInput = root.querySelector("[data-client-request-id]");
        const statusInput = root.querySelector("[data-campaign-status]");
        const editorState = root.querySelector("[data-editor-state]");
        const modeInput = root.querySelector("[data-campaign-mode]");
        const conflictPolicyInput = root.querySelector("[data-conflict-policy]");
        const conflictPolicyHelp = root.querySelector("[data-conflict-policy-help]");
        const conflictPolicyNotice = root.querySelector("[data-conflict-policy-notice]");
        const startInput = root.querySelector("[data-campaign-start]");
        const endField = root.querySelector("[data-campaign-end-field]");
        const endInput = root.querySelector("[data-campaign-end]");
        const selectedCount = root.querySelector("[data-selected-count]");
        const configureSelectedButton = root.querySelector("[data-configure-selected]");
        const clearSelectionButton = root.querySelector("[data-clear-selection]");
        const clearStagingButton = root.querySelector("[data-clear-staging]");
        const saveDraftButton = root.querySelector("[data-save-draft]");
        const openConfirmButton = root.querySelector("[data-open-confirm]");
        const confirmButton = root.querySelector("[data-confirm-campaign]");
        const adjustmentTypeInput = root.querySelector("[data-adjustment-type]");
        const adjustmentValueInput = root.querySelector("[data-adjustment-value]");
        const adjustmentLabel = root.querySelector("[data-adjustment-label]");
        const configScope = root.querySelector("[data-config-scope]");
        const configurationPreview = root.querySelector("[data-configuration-preview]");
        const confirmationSummary = root.querySelector("[data-confirmation-summary]");
        const confirmationWarning = root.querySelector("[data-confirmation-warning]");

        const state = {
            campaignId: Number(root.dataset.campaignId ?? 0),
            campaignStatus: "Draft",
            currentPage: 1,
            totalPages: 1,
            totalItems: 0,
            products: [],
            variantsByProduct: new Map(),
            expandedProducts: new Set(),
            selectedVariants: new Map(),
            staging: new Map(),
            configTargetIds: [],
            canConfirmPreview: false,
            latestPreviewSummary: null,
            productAbortController: null,
            variantRequests: new Map(),
            campaignLoadState: Number(root.dataset.campaignId ?? 0) > 0
                ? "loading"
                : "ready",
            campaignLoadError: null
        };

        root.dataset.campaignLoadState = state.campaignLoadState;
        clientRequestIdInput.value =
            clientRequestIdInput.value || createRequestId();

        function readPlanTimes() {
            const mode = modeInput.value;
            if (conflictPolicyInput.value === "SupersedeNow") {
                const now = new Date();
                now.setSeconds(0, 0);
                startInput.value = toLocalInput(now.toISOString());
            }
            const startValue = startInput.value;
            const endValue = endInput.value;

            if (!startValue) {
                throw new Error("Vui lòng nhập thời gian bắt đầu trước khi cấu hình giá.");
            }

            const startDate = new Date(startValue);
            if (Number.isNaN(startDate.getTime())) {
                throw new Error("Thời gian bắt đầu không hợp lệ.");
            }

            let endDate = null;
            if (mode === "FixedWindow") {
                if (!endValue) {
                    throw new Error("Vui lòng nhập thời gian kết thúc.");
                }

                endDate = new Date(endValue);
                if (Number.isNaN(endDate.getTime()) || endDate <= startDate) {
                    throw new Error("Thời gian kết thúc phải sau thời gian bắt đầu.");
                }
            }

            return {
                mode,
                startDate: startDate.toISOString(),
                endDate: endDate ? endDate.toISOString() : null,
                conflictPolicy: root.querySelector("[data-conflict-policy]").value
            };
        }

        function readPlanForm() {
            const name = root.querySelector("[data-campaign-name]").value.trim();
            const reason = root.querySelector("[data-campaign-reason]").value.trim();

            if (state.campaignId > 0) {
                if (state.campaignLoadState === "loading") {
                    throw new Error("Dữ liệu bản nháp đang được tải. Vui lòng chờ trong giây lát.");
                }

                if (state.campaignLoadState !== "ready") {
                    throw new Error(
                        state.campaignLoadError
                            ?? "Không thể tải dữ liệu bản nháp. Hãy tải lại trang trước khi chỉnh sửa.");
                }

                if (!isValidRowVersion(rowVersionInput.value)) {
                    throw new Error(
                        "Không nhận được phiên bản dữ liệu của bản nháp. Hãy tải lại trang trước khi lưu.");
                }
            }

            const times = readPlanTimes();

            if (!name) throw new Error("Vui lòng nhập tên kế hoạch giá.");
            if (!reason) throw new Error("Vui lòng nhập lý do thay đổi giá.");
            if (!state.staging.size) {
                throw new Error("Danh sách chờ chưa có biến thể nào.");
            }

            return {
                id: state.campaignId,
                name,
                description: root.querySelector("[data-campaign-description]").value.trim(),
                sourceType: root.querySelector("[data-campaign-source]").value,
                reason,
                clientRequestId: clientRequestIdInput.value,
                rowVersion: rowVersionInput.value || null,
                ...times,
                items: [...state.staging.values()].map((item) => ({
                    variantId: item.variantId,
                    newPrice: item.newPrice,
                    adjustmentType: item.adjustmentType,
                    adjustmentValue: item.adjustmentValue,
                    variantRowVersion: item.rowVersion
                }))
            };
        }

        function updateConflictPolicyState(setImmediateStart = false) {
            const policy = conflictPolicyInput.value;
            conflictPolicyNotice.dataset.policy = policy;

            const noticeText = conflictPolicyNotice.querySelector("span");

            if (policy === "ReplaceFromStart") {
                conflictPolicyHelp.textContent =
                    "Kế hoạch chồng lấn sẽ được cắt hoặc thay thế từ thời điểm bắt đầu mới.";
                if (noticeText) {
                    noticeText.textContent =
                        "Phải chọn đủ toàn bộ biến thể của từng kế hoạch chồng lấn để giữ lịch giá nhất quán trước thời điểm thay thế.";
                }
            } else if (policy === "SupersedeNow") {
                conflictPolicyHelp.textContent =
                    "Kế hoạch chồng lấn sẽ dừng ngay khi xác nhận. Thời gian bắt đầu được đặt về hiện tại.";
                if (noticeText) {
                    noticeText.textContent =
                        "Các biến thể trong phạm vi mới bị thay thế ngay; biến thể còn lại được tách tự động sang một kế hoạch tiếp tục để không mất giá đang chạy.";
                }
                if (setImmediateStart) {
                    const now = new Date();
                    now.setSeconds(0, 0);
                    startInput.value = toLocalInput(now.toISOString());
                }
            } else {
                conflictPolicyHelp.textContent =
                    "Kế hoạch sẽ bị chặn nếu có khoảng giá chồng lấn.";
                if (noticeText) {
                    noticeText.textContent =
                        "Không có kế hoạch nào bị thay đổi; hãy điều chỉnh thời gian hoặc chọn chính sách thay thế khi phát hiện xung đột.";
                }
            }
        }

        function updateModeState() {
            const openEnded = modeInput.value === "OpenEnded";
            endField.hidden = openEnded;
            endInput.required = !openEnded;
            if (openEnded) endInput.value = "";
        }

        function updateAdjustmentLabel() {
            const labels = {
                FixedPrice: "Giá mới",
                PercentOff: "Phần trăm giảm",
                AmountOff: "Số tiền giảm"
            };
            adjustmentLabel.textContent = labels[adjustmentTypeInput.value] ?? "Giá trị";
            adjustmentValueInput.placeholder = adjustmentTypeInput.value === "PercentOff"
                ? "Ví dụ: 10"
                : "Nhập giá trị";
        }

        async function loadProducts(page = 1) {
            state.productAbortController?.abort();
            state.productAbortController = new AbortController();
            state.currentPage = page;

            productBody.replaceChildren(createTableMessage("Đang tải sản phẩm..."));

            const parameters = new URLSearchParams({
                page: String(page),
                pageSize: "10"
            });
            const keyword = root.querySelector("[data-filter-keyword]").value.trim();
            const categoryId = root.querySelector("[data-filter-category]").value;
            const brandId = root.querySelector("[data-filter-brand]").value;

            if (keyword) parameters.set("keyword", keyword);
            if (categoryId) parameters.set("categoryId", categoryId);
            if (brandId) parameters.set("brandId", brandId);

            try {
                const result = await http.getJson(
                    `/Admin/PriceCampaigns/GetProducts?${parameters}`,
                    state.productAbortController.signal);

                if (!result.success) {
                    throw new Error(result.message ?? "Không thể tải sản phẩm.");
                }

                state.products = result.data?.items ?? [];
                state.currentPage = Number(result.data?.page ?? 1);
                state.totalPages = Number(result.data?.totalPages ?? 1);
                state.totalItems = Number(result.data?.totalItems ?? 0);
                renderProducts();
                renderPagination();
            } catch (error) {
                if (error.name === "AbortError") return;
                productBody.replaceChildren(createTableMessage(error.message, true));
            }
        }

        function createTableMessage(message, isError = false) {
            const row = document.createElement("tr");
            const cell = document.createElement("td");
            cell.colSpan = 8;
            const box = createText(
                "div",
                `pricing-loading-state${isError ? " is-error" : ""}`,
                message);
            cell.append(box);
            row.append(cell);
            return row;
        }

        function renderProducts() {
            productBody.replaceChildren();

            if (!state.products.length) {
                productBody.append(createTableMessage("Không tìm thấy sản phẩm phù hợp."));
                return;
            }

            state.products.forEach((product) => {
                productBody.append(createProductRow(product));
                if (state.expandedProducts.has(product.id)) {
                    productBody.append(createVariantExpansionRow(product));
                }
            });
        }

        function createProductRow(product) {
            const row = document.createElement("tr");
            row.className = "pricing-product-row";
            row.dataset.productId = String(product.id);

            const selectCell = document.createElement("td");
            selectCell.className = "pricing-checkbox-column";
            const checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            checkbox.className = "form-check-input";
            checkbox.setAttribute("aria-label", `Chọn tất cả biến thể của ${product.name}`);
            updateProductCheckbox(product.id, checkbox);
            checkbox.addEventListener("change", async () => {
                checkbox.disabled = true;
                try {
                    const variants = await loadVariants(product);
                    variants.forEach((variant) => {
                        if (checkbox.checked) {
                            state.selectedVariants.set(variant.id, variant);
                        } else {
                            state.selectedVariants.delete(variant.id);
                        }
                    });
                    renderAllSelectionViews();
                } catch (error) {
                    setFeedback(feedback, error.message, "error");
                } finally {
                    checkbox.disabled = false;
                }
            });
            selectCell.append(checkbox);

            const productCell = document.createElement("td");
            const productInfo = document.createElement("div");
            productInfo.className = "pricing-product-info";
            const image = document.createElement("img");
            image.src = product.image || "/images/no-image.png";
            image.alt = "";
            image.loading = "lazy";
            image.addEventListener("error", () => {
                image.src = "/images/no-image.png";
            }, { once: true });
            const details = document.createElement("div");
            details.append(createText("strong", "", product.name));
            details.append(createText("small", "", `ID sản phẩm: ${product.id}`));
            productInfo.append(image, details);
            productCell.append(productInfo);

            const taxonomyCell = document.createElement("td");
            taxonomyCell.append(createText("strong", "d-block", product.category));
            taxonomyCell.append(createText("small", "", product.brand));

            const countCell = createText("td", "text-center", String(product.variantCount));
            const listPriceCell = createText(
                "td",
                "pricing-money-cell",
                formatRange(product.minListPrice, product.maxListPrice));
            const currentPriceCell = createText(
                "td",
                "pricing-money-cell pricing-money-cell--current",
                formatRange(product.minCurrentPrice, product.maxCurrentPrice));

            const statusCell = document.createElement("td");
            if (product.configuredVariantCount > 0) {
                statusCell.append(createText(
                    "span",
                    "pricing-mini-badge is-warning",
                    `${product.configuredVariantCount} biến thể đã có kế hoạch`));
            } else {
                statusCell.append(createText(
                    "span",
                    "pricing-mini-badge is-neutral",
                    "Chưa có kế hoạch"));
            }

            const actionCell = document.createElement("td");
            actionCell.className = "text-end";
            const actions = document.createElement("div");
            actions.className = "pricing-row-actions";
            const expandButton = createButton(
                state.expandedProducts.has(product.id) ? "Thu gọn" : "Biến thể",
                "btn btn-sm btn-outline-secondary",
                () => toggleProductExpansion(product));
            const configureButton = createButton(
                "Cấu hình giá",
                "btn btn-sm btn-primary",
                async () => {
                    try {
                        const variants = await loadVariants(product);
                        variants.forEach((variant) => {
                            state.selectedVariants.set(variant.id, variant);
                        });
                        renderAllSelectionViews();
                        openConfiguration(
                            variants.map((variant) => variant.id),
                            product.name);
                    } catch (error) {
                        setFeedback(feedback, error.message, "error");
                    }
                });
            actions.append(expandButton, configureButton);
            actionCell.append(actions);

            row.append(
                selectCell,
                productCell,
                taxonomyCell,
                countCell,
                listPriceCell,
                currentPriceCell,
                statusCell,
                actionCell);
            return row;
        }

        function updateProductCheckbox(productId, checkbox) {
            const variants = state.variantsByProduct.get(productId);
            if (!variants?.length) {
                checkbox.checked = false;
                checkbox.indeterminate = false;
                return;
            }

            const selected = variants.filter((variant) =>
                state.selectedVariants.has(variant.id)).length;
            checkbox.checked = selected === variants.length;
            checkbox.indeterminate = selected > 0 && selected < variants.length;
        }

        async function toggleProductExpansion(product) {
            if (state.expandedProducts.has(product.id)) {
                state.expandedProducts.delete(product.id);
                renderProducts();
                return;
            }

            state.expandedProducts.add(product.id);
            renderProducts();
            try {
                await loadVariants(product);
                renderProducts();
            } catch (error) {
                state.expandedProducts.delete(product.id);
                renderProducts();
                setFeedback(feedback, error.message, "error");
            }
        }

        async function loadVariants(product) {
            if (state.variantsByProduct.has(product.id)) {
                return state.variantsByProduct.get(product.id);
            }

            if (state.variantRequests.has(product.id)) {
                return state.variantRequests.get(product.id);
            }

            const request = http.getJson(
                `/Admin/PriceCampaigns/GetProductVariants?productId=${product.id}`)
                .then((result) => {
                    if (!result.success) {
                        throw new Error(result.message ?? "Không thể tải biến thể.");
                    }

                    const variants = (result.data?.variants ?? []).map((variant) => ({
                        ...variant,
                        productId: product.id,
                        productName: product.name,
                        listPrice: Number(variant.listPrice),
                        currentPrice: Number(variant.currentPrice),
                        stockQuantity: Number(variant.stockQuantity),
                        plans: variant.plans ?? []
                    }));
                    state.variantsByProduct.set(product.id, variants);
                    state.variantRequests.delete(product.id);
                    return variants;
                })
                .catch((error) => {
                    state.variantRequests.delete(product.id);
                    throw error;
                });

            state.variantRequests.set(product.id, request);
            return request;
        }

        function createVariantExpansionRow(product) {
            const row = document.createElement("tr");
            row.className = "pricing-variant-expansion-row";
            const cell = document.createElement("td");
            cell.colSpan = 8;

            const container = document.createElement("div");
            container.className = "pricing-variant-panel";
            const variants = state.variantsByProduct.get(product.id);

            if (!variants) {
                container.append(createText(
                    "div",
                    "pricing-loading-state",
                    "Đang tải bảng biến thể..."));
            } else if (!variants.length) {
                container.append(createText(
                    "div",
                    "pricing-loading-state",
                    "Sản phẩm chưa có biến thể đang hoạt động."));
            } else {
                const tableWrap = document.createElement("div");
                tableWrap.className = "table-responsive";
                const table = document.createElement("table");
                table.className = "table pricing-variant-table align-middle";

                const head = document.createElement("thead");
                const headerRow = document.createElement("tr");
                ["", "SKU / thuộc tính", "Giá niêm yết", "Giá hiện tại", "Nguồn giá", "Kế hoạch", "Giá chờ", "Thao tác"]
                    .forEach((label) => headerRow.append(createText("th", "", label)));
                head.append(headerRow);

                const body = document.createElement("tbody");
                variants.forEach((variant) => body.append(createVariantRow(variant)));
                table.append(head, body);
                tableWrap.append(table);
                container.append(tableWrap);
            }

            cell.append(container);
            row.append(cell);
            return row;
        }

        function createVariantRow(variant) {
            const row = document.createElement("tr");
            row.dataset.variantId = String(variant.id);

            const checkboxCell = document.createElement("td");
            const checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            checkbox.className = "form-check-input";
            checkbox.checked = state.selectedVariants.has(variant.id);
            checkbox.setAttribute("aria-label", `Chọn ${variant.sku}`);
            checkbox.addEventListener("change", () => {
                if (checkbox.checked) {
                    state.selectedVariants.set(variant.id, variant);
                } else {
                    state.selectedVariants.delete(variant.id);
                }
                renderAllSelectionViews();
            });
            checkboxCell.append(checkbox);

            const skuCell = document.createElement("td");
            skuCell.append(createText("strong", "d-block", variant.sku));
            skuCell.append(createText("small", "", variant.attributes || "Không có thuộc tính"));

            const listCell = createText("td", "pricing-money-cell", currency.format(variant.listPrice));
            const currentCell = createText(
                "td",
                "pricing-money-cell pricing-money-cell--current",
                currency.format(variant.currentPrice));

            const sourceCell = document.createElement("td");
            sourceCell.append(createText(
                "span",
                `pricing-mini-badge ${variant.priceSource === "Campaign" ? "is-active" : "is-neutral"}`,
                variant.priceSource === "Campaign" ? "Kế hoạch giá" : "Giá niêm yết"));

            const plansCell = document.createElement("td");
            if (variant.plans.length) {
                variant.plans.slice(0, 2).forEach((plan) => {
                    const badge = createText(
                        "span",
                        "pricing-plan-chip",
                        `${plan.name} · ${formatLocalDate(plan.startDateUtc)}`);
                    badge.title = `${plan.code} – ${plan.status}`;
                    plansCell.append(badge);
                });
                if (variant.plans.length > 2) {
                    plansCell.append(createText(
                        "small",
                        "d-block",
                        `+${variant.plans.length - 2} kế hoạch khác`));
                }
            } else {
                plansCell.append(createText("span", "text-muted", "Không có"));
            }

            const staged = state.staging.get(variant.id);
            const stagedCell = document.createElement("td");
            if (staged) {
                stagedCell.append(createText(
                    "strong",
                    staged.deltaAmount > 0 ? "pricing-price-up" : "pricing-price-down",
                    currency.format(staged.newPrice)));
                if (staged.conflicts.length) {
                    stagedCell.append(createText(
                        "small",
                        "d-block text-danger",
                        `${staged.conflicts.length} xung đột`));
                }
            } else {
                stagedCell.append(createText("span", "text-muted", "Chưa cấu hình"));
            }

            const actionCell = document.createElement("td");
            actionCell.className = "text-end";
            actionCell.append(createButton(
                staged ? "Chỉnh giá" : "Cấu hình",
                "btn btn-sm btn-outline-primary",
                () => {
                    state.selectedVariants.set(variant.id, variant);
                    renderAllSelectionViews();
                    openConfiguration([variant.id], variant.sku);
                }));

            row.append(
                checkboxCell,
                skuCell,
                listCell,
                currentCell,
                sourceCell,
                plansCell,
                stagedCell,
                actionCell);
            return row;
        }

        function renderPagination() {
            root.querySelector("[data-page-summary]").textContent =
                `Trang ${state.currentPage}/${state.totalPages} · ${state.totalItems} sản phẩm`;
            const previous = root.querySelector("[data-page-previous]");
            const next = root.querySelector("[data-page-next]");
            previous.disabled = state.currentPage <= 1;
            next.disabled = state.currentPage >= state.totalPages;
        }

        function renderSelectionToolbar() {
            selectedCount.textContent = String(state.selectedVariants.size);
            configureSelectedButton.disabled = state.selectedVariants.size === 0;
            clearSelectionButton.disabled = state.selectedVariants.size === 0;
        }

        function renderStaging() {
            stagingList.replaceChildren();
            const items = [...state.staging.values()]
                .sort((a, b) =>
                    a.productName.localeCompare(b.productName)
                    || a.sku.localeCompare(b.sku));

            const productGroups = new Map();
            items.forEach((item) => {
                if (!productGroups.has(item.productId)) {
                    productGroups.set(item.productId, {
                        productId: item.productId,
                        productName: item.productName,
                        items: []
                    });
                }
                productGroups.get(item.productId).items.push(item);
            });

            const increaseCount = items.filter((item) => item.deltaAmount > 0).length;
            const decreaseCount = items.filter((item) => item.deltaAmount < 0).length;
            const conflictCount = items.filter((item) => item.conflicts.length > 0).length;

            root.querySelector("[data-stage-product-count]").textContent = String(productGroups.size);
            root.querySelector("[data-stage-variant-count]").textContent = String(items.length);
            root.querySelector("[data-stage-increase-count]").textContent = String(increaseCount);
            root.querySelector("[data-stage-decrease-count]").textContent = String(decreaseCount);
            root.querySelector("[data-stage-conflict-count]").textContent = String(conflictCount);
            root.querySelector("[data-save-summary]").textContent = items.length
                ? `${items.length} biến thể thuộc ${productGroups.size} sản phẩm`
                : "Chưa có cấu hình giá";

            clearStagingButton.disabled = items.length === 0;
            updateActionAvailability();

            if (!items.length) {
                const empty = document.createElement("div");
                empty.className = "pricing-empty-state";
                empty.append(createText("strong", "", "Danh sách chờ đang trống"));
                empty.append(createText("span", "", "Chọn biến thể và cấu hình giá để tiếp tục."));
                stagingList.append(empty);
                return;
            }

            productGroups.forEach((group) => {
                const card = document.createElement("article");
                card.className = "pricing-staging-group";

                const header = document.createElement("header");
                const title = document.createElement("div");
                title.append(createText("strong", "", group.productName));
                title.append(createText("small", "", `${group.items.length} biến thể đã cấu hình`));
                header.append(title, createButton(
                    "Cấu hình lại sản phẩm",
                    "btn btn-sm btn-outline-primary",
                    () => openConfiguration(
                        group.items.map((item) => item.variantId),
                        group.productName)));

                const rows = document.createElement("div");
                rows.className = "pricing-staging-rows";
                group.items.forEach((item) => rows.append(createStagingRow(item)));
                card.append(header, rows);
                stagingList.append(card);
            });
        }

        function createStagingRow(item) {
            const row = document.createElement("div");
            row.className = "pricing-staging-row";

            const identity = document.createElement("div");
            identity.append(createText("strong", "", item.sku));
            identity.append(createText("small", "", item.attributes || "Không có thuộc tính"));

            const oldPrice = document.createElement("div");
            oldPrice.append(createText("span", "", "Giá hiện tại"));
            oldPrice.append(createText("strong", "", currency.format(item.currentPrice)));

            const newPrice = document.createElement("div");
            newPrice.append(createText("span", "", "Giá mới"));
            newPrice.append(createText(
                "strong",
                item.deltaAmount > 0
                    ? "pricing-price-up"
                    : item.deltaAmount < 0
                        ? "pricing-price-down"
                        : "",
                currency.format(item.newPrice)));
            newPrice.append(createText(
                "small",
                "",
                `${item.deltaAmount >= 0 ? "+" : ""}${currency.format(item.deltaAmount)} · ${item.deltaPercent >= 0 ? "+" : ""}${item.deltaPercent}%`));

            const method = document.createElement("div");
            method.append(createText("span", "", "Cấu hình"));
            method.append(createText(
                "strong",
                "",
                describeAdjustment(item.adjustmentType, item.adjustmentValue)));

            const conflict = document.createElement("div");
            if (item.conflicts.length) {
                const policy = conflictPolicyInput.value;
                const hasPartialCoverage = item.conflicts.some(
                    (entry) => entry.isFullyCovered === false);
                const isReplacePolicy = policy !== "Reject";
                const blocksReplacement = hasPartialCoverage
                    && policy === "ReplaceFromStart";
                conflict.append(createText(
                    "span",
                    blocksReplacement || !isReplacePolicy
                        ? "pricing-mini-badge is-danger"
                        : "pricing-mini-badge is-warning",
                    blocksReplacement
                        ? "Thiếu phạm vi thay thế"
                        : policy === "SupersedeNow" && hasPartialCoverage
                            ? "Sẽ tách kế hoạch cũ"
                            : isReplacePolicy
                                ? `${item.conflicts.length} kế hoạch sẽ thay`
                                : `${item.conflicts.length} xung đột`));
                conflict.title = item.conflicts
                    .map((entry) => `${entry.code}: ${entry.name} (${entry.coveredVariantCount ?? 0}/${entry.campaignVariantCount ?? 0} biến thể)`)
                    .join("\n");
            } else {
                conflict.append(createText(
                    "span",
                    "pricing-mini-badge is-success",
                    "Sẵn sàng"));
            }

            const actions = document.createElement("div");
            actions.className = "pricing-row-actions";
            actions.append(
                createButton(
                    "Sửa",
                    "btn btn-sm btn-outline-secondary",
                    () => openConfiguration([item.variantId], item.sku)),
                createButton(
                    "Xóa",
                    "btn btn-sm btn-outline-danger",
                    () => {
                        state.staging.delete(item.variantId);
                        renderAllSelectionViews();
                    }));

            row.append(identity, oldPrice, newPrice, method, conflict, actions);
            return row;
        }

        function describeAdjustment(type, value) {
            if (type === "PercentOff") return `Giảm ${value}%`;
            if (type === "AmountOff") return `Giảm ${currency.format(value)}`;
            return `Giá cố định ${currency.format(value)}`;
        }

        function renderAllSelectionViews() {
            renderSelectionToolbar();
            renderStaging();
            renderProducts();
        }

        function updateActionAvailability() {
            const hasStaging = state.staging.size > 0;
            const immutable = state.campaignStatus !== "Draft";
            const waitingForCampaign = state.campaignId > 0
                && state.campaignLoadState !== "ready";

            saveDraftButton.disabled = !hasStaging || immutable || waitingForCampaign;
            openConfirmButton.disabled = !hasStaging || immutable || waitingForCampaign;
            saveDraftButton.hidden = immutable;
            openConfirmButton.hidden = immutable;
            openConfirmButton.textContent = waitingForCampaign
                ? "Đang tải bản nháp..."
                : "Xác nhận kế hoạch";
            saveDraftButton.title = waitingForCampaign
                ? "Cần tải xong dữ liệu và RowVersion trước khi lưu."
                : "";
            openConfirmButton.title = waitingForCampaign
                ? "Cần tải xong dữ liệu và RowVersion trước khi xác nhận."
                : "";
        }

        function openConfiguration(variantIds, label) {
            const uniqueIds = [...new Set(variantIds)]
                .filter((id) => state.selectedVariants.has(id) || state.staging.has(id));
            if (!uniqueIds.length) {
                setFeedback(feedback, "Không có biến thể hợp lệ để cấu hình.", "error");
                return;
            }

            state.configTargetIds = uniqueIds;
            configScope.textContent = `${label} · ${uniqueIds.length} biến thể`;
            configurationPreview.textContent =
                "Giá sẽ được tính lại trên server trước khi đưa vào danh sách chờ.";

            const existing = uniqueIds.length === 1
                ? state.staging.get(uniqueIds[0])
                : null;
            adjustmentTypeInput.value = existing?.adjustmentType ?? "FixedPrice";
            adjustmentValueInput.value = existing?.adjustmentValue ?? "";
            updateAdjustmentLabel();
            configDialog.showModal();
            window.setTimeout(() => adjustmentValueInput.focus(), 0);
        }

        async function previewConfiguration(variantIds, adjustmentType, adjustmentValue) {
            const times = readPlanTimes();
            const items = variantIds.map((variantId) => {
                const variant = state.selectedVariants.get(variantId)
                    ?? state.staging.get(variantId);
                return {
                    variantId,
                    adjustmentType,
                    adjustmentValue,
                    variantRowVersion: variant.rowVersion
                };
            });

            return await http.postJson(
                "/Admin/PriceCampaigns/PreviewPlan",
                {
                    campaignId: state.campaignId,
                    ...times,
                    items
                });
        }

        function mergePreviewItems(items) {
            (items ?? []).forEach((item) => {
                const base = state.selectedVariants.get(item.variantId)
                    ?? state.staging.get(item.variantId)
                    ?? {};
                const merged = {
                    ...base,
                    productId: item.productId,
                    productName: item.productName,
                    variantId: item.variantId,
                    id: item.variantId,
                    sku: item.sku,
                    attributes: item.attributes,
                    rowVersion: item.rowVersion,
                    listPrice: Number(item.listPrice),
                    currentPrice: Number(item.currentPrice),
                    newPrice: Number(item.newPrice),
                    deltaAmount: Number(item.deltaAmount),
                    deltaPercent: Number(item.deltaPercent),
                    adjustmentType: item.adjustmentType,
                    adjustmentValue: Number(item.adjustmentValue),
                    isStale: Boolean(item.isStale),
                    conflicts: item.conflicts ?? []
                };
                state.selectedVariants.set(item.variantId, merged);
                state.staging.set(item.variantId, merged);
            });
        }

        async function refreshFullPreview() {
            if (!state.staging.size) {
                throw new Error("Danh sách chờ chưa có biến thể nào.");
            }

            const times = readPlanTimes();
            const result = await http.postJson(
                "/Admin/PriceCampaigns/PreviewPlan",
                {
                    campaignId: state.campaignId,
                    ...times,
                    items: [...state.staging.values()].map((item) => ({
                        variantId: item.variantId,
                        adjustmentType: item.adjustmentType,
                        adjustmentValue: item.adjustmentValue,
                        variantRowVersion: item.rowVersion
                    }))
                });

            if (!result.success) {
                throw new Error(formatServerError(
                    result,
                    "Không thể kiểm tra lại danh sách giá."));
            }

            mergePreviewItems(result.data?.items);
            state.canConfirmPreview = Boolean(result.canConfirm);
            state.latestPreviewSummary = result.data?.summary ?? null;
            renderAllSelectionViews();
            return result;
        }

        async function saveDraft(silent = false) {
            const payload = readPlanForm();
            saveDraftButton.disabled = true;
            if (!silent) setFeedback(feedback, "Đang lưu bản nháp...");

            try {
                const result = await http.postJson(
                    "/Admin/PriceCampaigns/SaveDraft",
                    payload);

                if (!result.success) {
                    throw new Error(formatServerError(
                        result,
                        "Không thể lưu bản nháp."));
                }

                state.campaignId = Number(result.campaignId);
                state.campaignStatus = result.status ?? "Draft";
                root.dataset.campaignId = String(state.campaignId);
                rowVersionInput.value = result.rowVersion ?? rowVersionInput.value;
                clientRequestIdInput.value = result.clientRequestId
                    ?? clientRequestIdInput.value;
                statusInput.value = state.campaignStatus;
                editorState.textContent = result.code
                    ? `Bản nháp ${result.code}`
                    : "Bản nháp đã lưu";
                mergePreviewItems(result.data?.items);
                state.canConfirmPreview = Boolean(result.canConfirm);
                state.latestPreviewSummary = result.data?.summary ?? state.latestPreviewSummary;
                renderAllSelectionViews();

                if (!silent) setFeedback(feedback, result.message, "success");
                return result;
            } finally {
                updateActionAvailability();
            }
        }

        function renderConfirmation(result) {
            confirmationSummary.replaceChildren();
            const summary = result.data?.summary ?? state.latestPreviewSummary;
            if (!summary) return;

            const rows = [
                ["Sản phẩm", summary.productCount],
                ["Biến thể", summary.variantCount],
                ["Tăng giá", summary.increaseCount],
                ["Giảm giá", summary.decreaseCount],
                ["Không đổi", summary.unchangedCount],
                ["Chính sách", translateConflictPolicy(conflictPolicyInput.value)],
                ["Biến thể xung đột", summary.conflictCount],
                ["Kế hoạch bị tác động", summary.conflictCampaignCount ?? 0],
                ["Kế hoạch tách một phần", summary.partialConflictCampaignCount ?? 0],
                ["Tổng giá hiện tại", currency.format(summary.currentTotal)],
                ["Tổng giá mới", currency.format(summary.newTotal)]
            ];

            rows.forEach(([label, value]) => {
                const item = document.createElement("div");
                item.append(createText("span", "", String(label)));
                item.append(createText("strong", "", String(value)));
                confirmationSummary.append(item);
            });

            confirmationWarning.hidden = !result.message && Boolean(result.canConfirm);
            confirmationWarning.textContent = result.message
                ?? (result.canConfirm
                    ? ""
                    : "Kế hoạch còn xung đột và chưa thể xác nhận.");
            confirmButton.disabled = !result.canConfirm;
        }

        async function loadCampaign() {
            if (!state.campaignId) {
                state.campaignLoadState = "ready";
                root.dataset.campaignLoadState = "ready";
                return;
            }

            state.campaignLoadState = "loading";
            state.campaignLoadError = null;
            root.dataset.campaignLoadState = "loading";
            root.setAttribute("aria-busy", "true");
            rowVersionInput.value = "";
            editorState.textContent = "Đang tải bản nháp...";
            setFeedback(feedback, "Đang tải dữ liệu kế hoạch...");
            updateActionAvailability();

            try {
                const result = await http.getJson(
                    `/Admin/PriceCampaigns/GetCampaign/${state.campaignId}`);

                if (!result.success) {
                    throw new Error(result.message ?? "Không thể tải kế hoạch.");
                }

                const data = result.data;
                if (Number(data?.id) !== state.campaignId) {
                    throw new Error("Dữ liệu kế hoạch trả về không khớp với trang đang mở.");
                }

                if (!isValidRowVersion(data?.rowVersion)) {
                    throw new Error(
                        "Kế hoạch không có RowVersion hợp lệ. Hãy kiểm tra migration và tải lại trang.");
                }

                root.querySelector("[data-campaign-name]").value = data.name ?? "";
                root.querySelector("[data-campaign-description]").value = data.description ?? "";
                root.querySelector("[data-campaign-reason]").value = data.reason ?? "";
                root.querySelector("[data-campaign-source]").value = data.sourceType ?? "Manual";
                conflictPolicyInput.value = data.conflictPolicy ?? "Reject";
                modeInput.value = data.mode ?? "FixedWindow";
                root.querySelector("[data-campaign-start]").value = toLocalInput(data.startDateUtc);
                endInput.value = toLocalInput(data.endDateUtc);
                rowVersionInput.value = data.rowVersion.trim();
                clientRequestIdInput.value = data.clientRequestId
                    ?? clientRequestIdInput.value;
                state.campaignStatus = data.status ?? "Draft";
                statusInput.value = state.campaignStatus;
                editorState.textContent = `${data.code} · ${translateCampaignStatus(state.campaignStatus)}`;

                state.selectedVariants.clear();
                state.staging.clear();
                (data.items ?? []).forEach((item) => {
                    const currentPrice = Number(item.currentPrice);
                    const newPrice = Number(item.newPrice);
                    const staged = {
                        productId: item.productId,
                        productName: item.productName ?? item.name,
                        variantId: item.variantId,
                        id: item.variantId,
                        sku: item.sku,
                        attributes: item.attributes,
                        rowVersion: item.variantRowVersion,
                        listPrice: Number(item.originalPrice),
                        currentPrice,
                        newPrice,
                        deltaAmount: newPrice - currentPrice,
                        deltaPercent: currentPrice
                            ? Number((((newPrice - currentPrice) / currentPrice) * 100).toFixed(2))
                            : 0,
                        adjustmentType: item.adjustmentType ?? "FixedPrice",
                        adjustmentValue: Number(item.adjustmentValue ?? item.newPrice),
                        conflicts: [],
                        isStale: false
                    };
                    state.selectedVariants.set(item.variantId, staged);
                    state.staging.set(item.variantId, staged);
                });

                state.campaignLoadState = "ready";
                root.dataset.campaignLoadState = "ready";
                updateModeState();
                updateConflictPolicyState();
                renderAllSelectionViews();
                setFeedback(feedback, "");
            } catch (error) {
                const message = error instanceof Error
                    ? error.message
                    : "Không thể tải dữ liệu bản nháp.";
                state.campaignLoadState = "failed";
                state.campaignLoadError = message;
                root.dataset.campaignLoadState = "failed";
                rowVersionInput.value = "";
                editorState.textContent = "Không tải được bản nháp";
                setFeedback(
                    feedback,
                    `${message} Không thể lưu hoặc xác nhận cho đến khi tải lại trang thành công.`,
                    "error");
                throw error;
            } finally {
                root.removeAttribute("aria-busy");
                updateActionAvailability();
            }
        }

        function translateStatus(status) {
            return {
                Draft: "Bản nháp",
                Confirmed: "Đã xác nhận",
                Scheduled: "Đã lên lịch",
                Active: "Đang hiệu lực",
                Completed: "Đã kết thúc",
                Cancelled: "Đã hủy",
                Superseded: "Đã thay thế"
            }[status] ?? status;
        }

        root.querySelector("[data-product-filter]")
            .addEventListener("submit", (event) => {
                event.preventDefault();
                loadProducts(1);
            });

        root.querySelector("[data-page-previous]")
            .addEventListener("click", () => loadProducts(state.currentPage - 1));
        root.querySelector("[data-page-next]")
            .addEventListener("click", () => loadProducts(state.currentPage + 1));

        configureSelectedButton.addEventListener("click", () => {
            openConfiguration(
                [...state.selectedVariants.keys()],
                "Các biến thể đã chọn");
        });

        clearSelectionButton.addEventListener("click", () => {
            state.selectedVariants.clear();
            renderAllSelectionViews();
        });

        clearStagingButton.addEventListener("click", () => {
            if (!window.confirm("Xóa toàn bộ cấu hình khỏi danh sách chờ?")) return;
            state.staging.clear();
            renderAllSelectionViews();
        });

        modeInput.addEventListener("change", updateModeState);
        conflictPolicyInput.addEventListener("change", () => {
            updateConflictPolicyState(true);
            state.canConfirmPreview = false;
            renderAllSelectionViews();
        });
        adjustmentTypeInput.addEventListener("change", updateAdjustmentLabel);

        root.querySelectorAll("[data-close-config]").forEach((button) => {
            button.addEventListener("click", () => configDialog.close());
        });
        root.querySelectorAll("[data-close-confirm]").forEach((button) => {
            button.addEventListener("click", () => confirmDialog.close());
        });

        configForm.addEventListener("submit", async (event) => {
            event.preventDefault();
            const value = Number(adjustmentValueInput.value);
            if (!Number.isFinite(value) || value <= 0) {
                configurationPreview.textContent = "Giá trị điều chỉnh phải lớn hơn 0.";
                return;
            }

            const applyButton = root.querySelector("[data-apply-configuration]");
            applyButton.disabled = true;
            configurationPreview.textContent = "Đang tính giá và kiểm tra xung đột trên server...";

            try {
                const result = await previewConfiguration(
                    state.configTargetIds,
                    adjustmentTypeInput.value,
                    value);

                if (!result.success) {
                    throw new Error(formatServerError(
                        result,
                        "Không thể xem trước giá."));
                }

                mergePreviewItems(result.data?.items);
                state.canConfirmPreview = Boolean(result.canConfirm);
                state.latestPreviewSummary = result.data?.summary ?? null;
                renderAllSelectionViews();
                configDialog.close();
                setFeedback(
                    feedback,
                    result.canConfirm
                        ? "Đã thêm cấu hình vào danh sách chờ."
                        : result.message ?? "Đã thêm cấu hình nhưng còn xung đột.",
                    result.canConfirm ? "success" : "warning");
            } catch (error) {
                configurationPreview.textContent = error.message;
            } finally {
                applyButton.disabled = false;
            }
        });

        saveDraftButton.addEventListener("click", async () => {
            try {
                await refreshFullPreview();
                await saveDraft(false);
            } catch (error) {
                setFeedback(feedback, error.message, "error");
            }
        });

        openConfirmButton.addEventListener("click", async () => {
            openConfirmButton.disabled = true;
            setFeedback(feedback, "Đang kiểm tra lại toàn bộ danh sách giá...");
            try {
                readPlanForm();
                const result = await refreshFullPreview();
                renderConfirmation(result);
                confirmDialog.showModal();
                setFeedback(
                    feedback,
                    result.canConfirm
                        ? "Kế hoạch đã vượt qua bước preview."
                        : result.message,
                    result.canConfirm ? "success" : "warning");
            } catch (error) {
                setFeedback(feedback, error.message, "error");
            } finally {
                updateActionAvailability();
            }
        });

        confirmForm.addEventListener("submit", async (event) => {
            event.preventDefault();
            if (!state.canConfirmPreview) return;

            confirmButton.disabled = true;
            setFeedback(feedback, "Đang xác nhận kế hoạch trong transaction...");

            try {
                if (state.campaignStatus !== "Draft") {
                    throw new Error("Chỉ bản nháp mới có thể xác nhận từ màn hình này.");
                }

                await saveDraft(true);
                const result = await http.postJson(
                    "/Admin/PriceCampaigns/ConfirmDraft",
                    {
                        id: state.campaignId,
                        rowVersion: rowVersionInput.value
                    });

                if (!result.success) {
                    throw new Error(formatServerError(
                        result,
                        "Không thể xác nhận kế hoạch."));
                }

                confirmDialog.close();
                setFeedback(feedback, result.message, "success");
                window.setTimeout(() => {
                    window.location.href = "/Admin/PriceCampaigns";
                }, 700);
            } catch (error) {
                setFeedback(feedback, error.message, "error");
                confirmButton.disabled = false;
            }
        });

        initializeDefaultPlanDates(root);
        updateModeState();
        updateConflictPolicyState();
        updateAdjustmentLabel();
        renderSelectionToolbar();
        renderStaging();

        Promise.all([loadProducts(1), loadCampaign()])
            .catch((error) => setFeedback(feedback, error.message, "error"));
    }

    const indexRoot = document.querySelector("[data-price-campaign-index]");
    if (indexRoot) initializeIndex(indexRoot);

    const editorRoot = document.querySelector("[data-price-campaign-editor]");
    if (editorRoot) initializeEditor(editorRoot);
})();
