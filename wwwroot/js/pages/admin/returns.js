(() => {
    "use strict";

    const page = document.querySelector("[data-return-details]");
    const http = window.FastBuyHttp;
    if (!page || !http) return;

    const returnId = Number.parseInt(page.dataset.returnId ?? "", 10);
    const form = page.querySelector("[data-return-shipping-form]");
    const service = page.querySelector("[data-return-service]");
    const result = page.querySelector("[data-return-provider-result]");

    const show = (message, type = "info") => {
        if (!result) return;
        result.hidden = false;
        result.textContent = message;
        result.dataset.type = type;
    };

    const read = () => {
        const data = new FormData(form);
        return {
            rowVersion: String(data.get("RowVersion") ?? ""),
            serviceId: Number.parseInt(data.get("ServiceId") ?? "0", 10) || 0,
            serviceTypeId: Number.parseInt(data.get("ServiceTypeId") ?? "2", 10),
            weightGram: Number.parseInt(data.get("WeightGram") ?? "0", 10),
            lengthCm: Number.parseInt(data.get("LengthCm") ?? "0", 10),
            widthCm: Number.parseInt(data.get("WidthCm") ?? "0", 10),
            heightCm: Number.parseInt(data.get("HeightCm") ?? "0", 10),
            note: String(data.get("Note") ?? "").trim()
        };
    };

    async function loadServices() {
        if (!service || !Number.isInteger(returnId)) return;
        try {
            const payload = await http.getJson(`/Admin/Returns/${returnId}/services`);
            service.replaceChildren(new Option("Chọn dịch vụ", ""));
            (payload.data ?? []).forEach((item) => {
                const option = new Option(
                    `${item.name} · type ${item.serviceTypeId}`,
                    String(item.serviceId)
                );
                option.dataset.serviceTypeId = String(item.serviceTypeId);
                service.add(option);
            });
        } catch (error) {
            service.replaceChildren(new Option("Không tải được dịch vụ", ""));
            show(error.message, "error");
        }
    }

    service?.addEventListener("change", () => {
        const type = service.selectedOptions[0]?.dataset.serviceTypeId;
        const input = form?.elements.namedItem("ServiceTypeId");
        if (type && input) input.value = type;
    });

    page.querySelector("[data-return-quote]")?.addEventListener("click", async (event) => {
        const button = event.currentTarget;
        button.disabled = true;
        try {
            const payload = await http.postJson(
                `/Admin/Returns/${returnId}/quote`,
                read()
            );
            const quote = payload.data;
            show(
                `Phí chiều về dự kiến: ${Number(quote.totalFee).toLocaleString("vi-VN")} ₫`,
                "success"
            );
        } catch (error) {
            show(error.message, "error");
        } finally {
            button.disabled = false;
        }
    });

    if (form) loadServices();
})();
