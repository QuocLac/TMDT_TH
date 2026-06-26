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

    function initializeIndex(root) {
        const feedback = root.querySelector("[data-page-feedback]");

        root.querySelectorAll("[data-local-datetime]")
            .forEach((element) => {
                const date = new Date(element.dateTime);
                if (!Number.isNaN(date.getTime())) {
                    element.textContent = dateTime.format(date);
                }
            });

        root.addEventListener("click", async (event) => {
            const target = event.target instanceof Element
                ? event.target
                : null;
            const button = target?.closest("[data-apply-campaign]");
            if (!button) return;

            const row = button.closest("[data-campaign-row]");
            if (!row) return;

            if (!window.confirm("Kích hoạt kế hoạch giá này ngay bây giờ?")) {
                return;
            }

            button.disabled = true;
            setFeedback(feedback, "Đang kích hoạt kế hoạch...");

            try {
                const result = await http.postJson(
                    "/Admin/PriceCampaigns/ApplyNow",
                    {
                        id: Number(row.dataset.campaignId),
                        rowVersion: row.dataset.rowVersion
                    });

                if (!result.success) {
                    throw new Error(formatServerError(
                        result,
                        "Không thể kích hoạt kế hoạch."));
                }

                setFeedback(feedback, result.message, "success");
                window.setTimeout(() => window.location.reload(), 450);
            } catch (error) {
                setFeedback(feedback, error.message, "error");
                button.disabled = false;
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
            variantRequests: new Map()
        };

        clientRequestIdInput.value =
            clientRequestIdInput.value || createRequestId();

        function readPlanTimes() {
            const mode = modeInput.value;
            const startValue = root.querySelector("[data-campaign-start]").value;
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
                conflict.append(createText(
                    "span",
                    "pricing-mini-badge is-danger",
                    `${item.conflicts.length} xung đột`));
                conflict.title = item.conflicts
                    .map((entry) => `${entry.code}: ${entry.name}`)
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
            const immutable = ["Active", "Completed", "Cancelled", "Superseded"]
                .includes(state.campaignStatus);
            saveDraftButton.disabled = !hasStaging || immutable;
            openConfirmButton.disabled = !hasStaging || immutable;
            saveDraftButton.hidden = state.campaignId > 0
                && state.campaignStatus !== "Draft";
            openConfirmButton.textContent = state.campaignStatus === "Draft"
                ? "Xác nhận kế hoạch"
                : "Lưu thay đổi kế hoạch";
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

        async function saveConfirmedPlanDirectly() {
            const payload = readPlanForm();
            const result = await http.postJson(
                "/Admin/PriceCampaigns/SaveCampaign",
                payload);

            if (!result.success) {
                throw new Error(formatServerError(
                    result,
                    "Không thể lưu thay đổi kế hoạch."));
            }

            return result;
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
                ["Xung đột", summary.conflictCount],
                ["Tổng giá hiện tại", currency.format(summary.currentTotal)],
                ["Tổng giá mới", currency.format(summary.newTotal)]
            ];

            rows.forEach(([label, value]) => {
                const item = document.createElement("div");
                item.append(createText("span", "", String(label)));
                item.append(createText("strong", "", String(value)));
                confirmationSummary.append(item);
            });

            confirmationWarning.hidden = Boolean(result.canConfirm);
            confirmationWarning.textContent = result.canConfirm
                ? ""
                : result.message ?? "Kế hoạch còn xung đột và chưa thể xác nhận.";
            confirmButton.disabled = !result.canConfirm;
        }

        async function loadCampaign() {
            if (!state.campaignId) return;

            setFeedback(feedback, "Đang tải dữ liệu kế hoạch...");
            const result = await http.getJson(
                `/Admin/PriceCampaigns/GetCampaign/${state.campaignId}`);

            if (!result.success) {
                throw new Error(result.message ?? "Không thể tải kế hoạch.");
            }

            const data = result.data;
            root.querySelector("[data-campaign-name]").value = data.name ?? "";
            root.querySelector("[data-campaign-description]").value = data.description ?? "";
            root.querySelector("[data-campaign-reason]").value = data.reason ?? "";
            root.querySelector("[data-campaign-source]").value = data.sourceType ?? "Manual";
            root.querySelector("[data-conflict-policy]").value = data.conflictPolicy ?? "Reject";
            modeInput.value = data.mode ?? "FixedWindow";
            root.querySelector("[data-campaign-start]").value = toLocalInput(data.startDateUtc);
            endInput.value = toLocalInput(data.endDateUtc);
            rowVersionInput.value = data.rowVersion ?? "";
            clientRequestIdInput.value = data.clientRequestId
                ?? clientRequestIdInput.value;
            state.campaignStatus = data.status ?? "Draft";
            statusInput.value = state.campaignStatus;
            editorState.textContent = `${data.code} · ${translateStatus(state.campaignStatus)}`;

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

            updateModeState();
            renderAllSelectionViews();
            setFeedback(feedback, "");
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
                let result;
                if (state.campaignStatus === "Draft") {
                    await saveDraft(true);
                    result = await http.postJson(
                        "/Admin/PriceCampaigns/ConfirmDraft",
                        {
                            id: state.campaignId,
                            rowVersion: rowVersionInput.value
                        });
                } else {
                    result = await saveConfirmedPlanDirectly();
                }

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
