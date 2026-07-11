(() => {
    "use strict";

    const page = document.querySelector("[data-order-details]");
    const api = window.FastBuyHttp;
    if (!page || !api) return;

    const match = window.location.pathname.match(/\/Admin\/Orders\/(\d+)\/?$/i);
    if (!match) return;

    const orderId = Number.parseInt(match[1], 10);
    if (!Number.isInteger(orderId) || orderId <= 0) return;

    const stylesheet = document.createElement("link");
    stylesheet.rel = "stylesheet";
    stylesheet.href = "/css/pages/admin/order-cancellations.css";
    document.head.appendChild(stylesheet);

    const section = document.createElement("section");
    section.className = "data-panel order-section cancellation-panel";
    section.dataset.orderCancellationPanel = "";
    section.innerHTML = `
        <div class="order-section-heading">
            <div>
                <p class="eyebrow">Cancellation workflow</p>
                <h3>Hủy đơn và hoàn kho</h3>
            </div>
            <span class="cancellation-phase-badge">Phase 4</span>
        </div>
        <div class="cancellation-feedback" data-cancellation-feedback role="status" aria-live="polite"></div>
        <div class="cancellation-loading" data-cancellation-content>Đang tải dữ liệu hủy đơn...</div>
    `;

    const operationPanel = page.querySelector(".order-operation-panel");
    const safetyNote = page.querySelector(".order-safety-note");
    if (safetyNote) {
        safetyNote.textContent =
            "Hủy đơn và hoàn kho phải đi qua panel Cancellation workflow. "
            + "Trả hàng và hoàn tiền thực tế vẫn được xử lý ở các phase riêng.";
    }
    operationPanel?.insertAdjacentElement("afterend", section);

    const content = section.querySelector("[data-cancellation-content]");
    const feedback = section.querySelector("[data-cancellation-feedback]");
    let summary = null;

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function formatMoney(value) {
        return new Intl.NumberFormat("vi-VN").format(Number(value ?? 0)) + " ₫";
    }

    function formatDate(value) {
        if (!value) return "—";
        const date = new Date(value);
        return Number.isNaN(date.getTime())
            ? "—"
            : new Intl.DateTimeFormat("vi-VN", {
                dateStyle: "short",
                timeStyle: "short"
            }).format(date);
    }

    function statusText(status) {
        switch (status) {
            case "Pending": return "Chờ duyệt";
            case "Approved": return "Đã duyệt";
            case "Rejected": return "Đã từ chối";
            default: return status ?? "—";
        }
    }

    function setFeedback(message, type = "info") {
        if (!feedback) return;
        feedback.textContent = message ?? "";
        feedback.className = "cancellation-feedback";
        if (message) feedback.classList.add(`is-${type}`);
    }

    function createIdempotencyKey() {
        if (window.crypto?.randomUUID) {
            return `cancel-${window.crypto.randomUUID().replaceAll("-", "")}`;
        }

        return `cancel-${Date.now()}-${Math.random().toString(16).slice(2)}`;
    }

    function render(data) {
        summary = data;
        if (!content) return;

        const eligibleItems = data.items.filter(item => item.cancellableQuantity > 0);
        const itemInputs = eligibleItems.map(item => `
            <label class="cancellation-line">
                <input type="checkbox"
                       data-cancellation-line
                       value="${item.orderItemId}" />
                <span>
                    <strong>${escapeHtml(item.productName)}</strong>
                    <small>${escapeHtml(item.sku)} · Đã đặt ${item.orderedQuantity}</small>
                    <small>Đã hủy ${item.approvedCancelledQuantity} · Đang chờ ${item.pendingQuantity}</small>
                </span>
                <input type="number"
                       min="1"
                       max="${item.cancellableQuantity}"
                       value="${item.cancellableQuantity}"
                       data-cancellation-quantity="${item.orderItemId}"
                       aria-label="Số lượng hủy ${escapeHtml(item.sku)}"
                       disabled />
            </label>
        `).join("");

        const requestCards = data.requests.length === 0
            ? `<div class="order-operation-empty">Chưa có yêu cầu hủy cho đơn này.</div>`
            : data.requests.map(request => {
                const lines = request.items.map(item => `
                    <li>
                        <span>${escapeHtml(item.sku)} · ${item.requestedQuantity} sản phẩm</span>
                        <strong>${request.status === "Approved"
                            ? formatMoney(item.refundAmount)
                            : "Chưa duyệt"}</strong>
                    </li>
                `).join("");

                const reviewActions = request.status === "Pending"
                    ? `
                        <div class="cancellation-review-actions">
                            <button type="button"
                                    class="btn btn-sm btn-outline-danger"
                                    data-review-cancellation="${request.id}"
                                    data-review-decision="reject"
                                    data-request-row-version="${escapeHtml(request.rowVersion)}">
                                Từ chối
                            </button>
                            <button type="button"
                                    class="btn btn-sm btn-primary"
                                    data-review-cancellation="${request.id}"
                                    data-review-decision="approve"
                                    data-request-row-version="${escapeHtml(request.rowVersion)}">
                                Duyệt và hoàn kho
                            </button>
                        </div>
                    `
                    : "";

                return `
                    <article class="cancellation-request-card">
                        <header>
                            <div>
                                <strong>Yêu cầu #${request.id}</strong>
                                <small>${escapeHtml(request.reasonCode)} · ${formatDate(request.requestedAt)}</small>
                            </div>
                            <span class="cancellation-status cancellation-status--${request.status.toLowerCase()}">
                                ${statusText(request.status)}
                            </span>
                        </header>
                        <p>${escapeHtml(request.reasonText)}</p>
                        <ul>${lines}</ul>
                        ${request.status === "Approved"
                            ? `<div class="cancellation-refund">
                                   Giá trị hoàn dự kiến:
                                   <strong>${formatMoney(request.refundAmount)}</strong>
                               </div>`
                            : ""}
                        ${request.reviewNote
                            ? `<small class="cancellation-review-note">
                                   Ghi chú duyệt: ${escapeHtml(request.reviewNote)}
                               </small>`
                            : ""}
                        ${reviewActions}
                    </article>
                `;
            }).join("");

        const eligibility = data.canRequest
            ? `<div class="cancellation-eligibility is-eligible">
                   Có thể tạo yêu cầu hủy trước khi bàn giao vận chuyển.
               </div>`
            : `<div class="cancellation-eligibility is-blocked">
                   ${escapeHtml(data.ineligibilityMessage
                       ?? "Không còn số lượng có thể yêu cầu hủy.")}
               </div>`;

        const createForm = data.canRequest && eligibleItems.length > 0
            ? `
                <form class="cancellation-create-form" data-create-cancellation-form>
                    <div class="cancellation-form-grid">
                        <label>
                            <span>Nhóm lý do</span>
                            <select name="reasonCode" required>
                                <option value="CustomerChangedMind">Khách đổi ý</option>
                                <option value="DuplicateOrder">Đặt trùng đơn</option>
                                <option value="IncorrectAddress">Sai địa chỉ</option>
                                <option value="PaymentIssue">Vấn đề thanh toán</option>
                                <option value="OutOfStock">Thiếu hàng thực tế</option>
                                <option value="FraudRisk">Rủi ro gian lận</option>
                                <option value="Other">Lý do khác</option>
                            </select>
                        </label>
                        <label>
                            <span>Mô tả chi tiết</span>
                            <textarea name="reasonText"
                                      minlength="3"
                                      maxlength="500"
                                      rows="3"
                                      required></textarea>
                        </label>
                    </div>
                    <div class="cancellation-lines">${itemInputs}</div>
                    <div class="cancellation-form-actions">
                        <small>
                            Tạo yêu cầu chưa làm thay đổi tồn kho. Hoàn kho chỉ xảy ra khi admin duyệt.
                        </small>
                        <button type="submit" class="btn btn-danger">
                            Tạo yêu cầu hủy
                        </button>
                    </div>
                </form>
            `
            : "";

        content.className = "cancellation-content";
        content.innerHTML = `
            ${eligibility}
            ${createForm}
            <div class="cancellation-request-list">
                <div class="order-section-heading cancellation-list-heading">
                    <div>
                        <p class="eyebrow">Audit requests</p>
                        <h4>Lịch sử yêu cầu hủy</h4>
                    </div>
                    <span>${data.requests.length} yêu cầu</span>
                </div>
                ${requestCards}
            </div>
        `;

        bindRenderedEvents();
    }

    function bindRenderedEvents() {
        content?.querySelectorAll("[data-cancellation-line]").forEach(checkbox => {
            checkbox.addEventListener("change", () => {
                const quantity = content.querySelector(
                    `[data-cancellation-quantity="${checkbox.value}"]`);
                if (quantity) quantity.disabled = !checkbox.checked;
            });
        });

        content?.querySelector("[data-create-cancellation-form]")
            ?.addEventListener("submit", createCancellation);

        content?.querySelectorAll("[data-review-cancellation]").forEach(button => {
            button.addEventListener("click", () => reviewCancellation(button));
        });
    }

    async function createCancellation(event) {
        event.preventDefault();
        const form = event.currentTarget;
        const selected = [...form.querySelectorAll(
            "[data-cancellation-line]:checked")];

        if (selected.length === 0) {
            setFeedback("Hãy chọn ít nhất một sản phẩm cần hủy.", "error");
            return;
        }

        const lines = selected.map(checkbox => {
            const orderItemId = Number.parseInt(checkbox.value, 10);
            const input = form.querySelector(
                `[data-cancellation-quantity="${orderItemId}"]`);
            return {
                orderItemId,
                quantity: Number.parseInt(input?.value ?? "0", 10)
            };
        });

        if (lines.some(item => !Number.isInteger(item.quantity) || item.quantity <= 0)) {
            setFeedback("Số lượng hủy không hợp lệ.", "error");
            return;
        }

        if (!window.confirm("Tạo yêu cầu hủy với các sản phẩm đã chọn?")) return;

        const submit = form.querySelector('button[type="submit"]');
        submit.disabled = true;
        setFeedback("Đang tạo yêu cầu hủy...", "info");

        try {
            const result = await api.postJson(
                `/Admin/Orders/${orderId}/cancellations`,
                {
                    orderRowVersion: summary.orderRowVersion,
                    reasonCode: form.elements.namedItem("reasonCode").value,
                    reasonText: form.elements.namedItem("reasonText").value.trim(),
                    idempotencyKey: createIdempotencyKey(),
                    lines
                });

            setFeedback(result.message, "success");
            render(result.data);
        } catch (error) {
            setFeedback(error.message, "error");
            await loadSummary();
        } finally {
            submit.disabled = false;
        }
    }

    async function reviewCancellation(button) {
        const requestId = Number.parseInt(button.dataset.reviewCancellation, 10);
        const approve = button.dataset.reviewDecision === "approve";
        const actionText = approve ? "duyệt và hoàn kho" : "từ chối";
        const note = window.prompt(
            `Nhập ghi chú để ${actionText} yêu cầu #${requestId}:`,
            approve ? "Đã kiểm tra điều kiện hủy trước giao hàng." : "");

        if (note === null) return;
        if (!window.confirm(`Xác nhận ${actionText} yêu cầu #${requestId}?`)) return;

        button.disabled = true;
        setFeedback(
            approve
                ? "Đang duyệt yêu cầu và hoàn kho..."
                : "Đang từ chối yêu cầu...",
            "info");

        try {
            const result = await api.postJson(
                `/Admin/Orders/${orderId}/cancellations/${requestId}/review`,
                {
                    orderRowVersion: summary.orderRowVersion,
                    cancellationRowVersion: button.dataset.requestRowVersion,
                    approve,
                    reviewNote: note.trim()
                });

            setFeedback(result.message, "success");
            render(result.data);
        } catch (error) {
            setFeedback(error.message, "error");
            await loadSummary();
        } finally {
            button.disabled = false;
        }
    }

    async function loadSummary() {
        if (!content) return;

        try {
            const result = await api.getJson(
                `/Admin/Orders/${orderId}/cancellations`);
            render(result.data);
        } catch (error) {
            content.className = "cancellation-loading is-error";
            content.textContent = error.message;
        }
    }

    loadSummary();
})();
