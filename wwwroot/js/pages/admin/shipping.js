(() => {
    "use strict";

    const page = document.querySelector("[data-shipping-details]");
    const http = window.FastBuyHttp;
    if (!page || !http) return;

    const shipmentId = Number.parseInt(page.dataset.shipmentId ?? "", 10);
    const form = page.querySelector("[data-shipping-form]");
    const serviceSelect = page.querySelector("[data-service-select]");
    const resultBox = page.querySelector("[data-shipping-result]");

    function showResult(message, type = "info") {
        if (!resultBox) return;
        resultBox.hidden = false;
        resultBox.textContent = message;
        resultBox.dataset.type = type;
    }

    function setBusy(button, busy) {
        if (!button) return;
        button.disabled = busy;
        button.setAttribute("aria-busy", busy ? "true" : "false");
    }

    function readExecutionInput() {
        const data = new FormData(form);
        return {
            serviceId: Number.parseInt(data.get("serviceId") ?? "0", 10) || 0,
            serviceTypeId: Number.parseInt(data.get("serviceTypeId") ?? "2", 10),
            weightGram: Number.parseInt(data.get("weightGram") ?? "0", 10),
            lengthCm: Number.parseInt(data.get("lengthCm") ?? "0", 10),
            widthCm: Number.parseInt(data.get("widthCm") ?? "0", 10),
            heightCm: Number.parseInt(data.get("heightCm") ?? "0", 10),
            note: String(data.get("note") ?? "").trim()
        };
    }

    async function loadServices() {
        if (!serviceSelect || !Number.isInteger(shipmentId)) return;
        try {
            const payload = await http.getJson(`/Admin/Shipping/${shipmentId}/services`);
            serviceSelect.replaceChildren(new Option("Chọn dịch vụ", ""));
            (payload.data ?? []).forEach((service) => {
                const option = new Option(
                    `${service.name} · type ${service.serviceTypeId}`,
                    String(service.serviceId)
                );
                option.dataset.serviceTypeId = String(service.serviceTypeId);
                serviceSelect.add(option);
            });
            serviceSelect.disabled = serviceSelect.options.length <= 1;
        } catch (error) {
            serviceSelect.replaceChildren(new Option("Không tải được dịch vụ", ""));
            showResult(error.message, "error");
        }
    }

    serviceSelect?.addEventListener("change", () => {
        const selected = serviceSelect.selectedOptions[0];
        const serviceType = selected?.dataset.serviceTypeId;
        const typeSelect = form?.elements.namedItem("serviceTypeId");
        if (serviceType && typeSelect) typeSelect.value = serviceType;
    });

    page.querySelector("[data-quote-shipment]")?.addEventListener("click", async (event) => {
        const button = event.currentTarget;
        setBusy(button, true);
        try {
            const payload = await http.postJson(
                `/Admin/Shipping/${shipmentId}/quote`,
                readExecutionInput()
            );
            const quote = payload.data;
            showResult(
                `Phí dự kiến: ${Number(quote.totalFee).toLocaleString("vi-VN")} ₫`
                + ` · phí dịch vụ ${Number(quote.serviceFee).toLocaleString("vi-VN")} ₫`,
                "success"
            );
        } catch (error) {
            showResult(error.message, "error");
        } finally {
            setBusy(button, false);
        }
    });

    page.querySelector("[data-create-shipment]")?.addEventListener("click", async (event) => {
        const button = event.currentTarget;
        if (!window.confirm("Xếp yêu cầu tạo vận đơn GHN vào outbox?")) return;
        setBusy(button, true);
        try {
            const payload = await http.postJson(
                `/Admin/Shipping/${shipmentId}/create`,
                readExecutionInput()
            );
            showResult(payload.message ?? "Đã xếp hàng tạo vận đơn.", "success");
            window.setTimeout(() => window.location.reload(), 900);
        } catch (error) {
            showResult(error.message, "error");
            setBusy(button, false);
        }
    });

    page.querySelector("[data-cancel-shipment]")?.addEventListener("click", async (event) => {
        const button = event.currentTarget;
        const reason = page.querySelector("[data-cancel-reason]")?.value?.trim() ?? "";
        if (reason.length < 3) {
            showResult("Lý do hủy phải có ít nhất 3 ký tự.", "error");
            return;
        }
        if (!window.confirm("Xếp yêu cầu hủy vận đơn GHN?")) return;
        setBusy(button, true);
        try {
            const payload = await http.postJson(
                `/Admin/Shipping/${shipmentId}/cancel`,
                { reason }
            );
            showResult(payload.message ?? "Đã xếp hàng hủy vận đơn.", "success");
            window.setTimeout(() => window.location.reload(), 900);
        } catch (error) {
            showResult(error.message, "error");
            setBusy(button, false);
        }
    });

    page.querySelector("[data-sync-shipment]")?.addEventListener("click", async (event) => {
        const button = event.currentTarget;
        setBusy(button, true);
        try {
            await http.postJson(`/Admin/Shipping/${shipmentId}/sync`, {});
            showResult("Đã đồng bộ chi tiết vận đơn từ GHN.", "success");
            window.setTimeout(() => window.location.reload(), 700);
        } catch (error) {
            showResult(error.message, "error");
            setBusy(button, false);
        }
    });

    loadServices();
})();
