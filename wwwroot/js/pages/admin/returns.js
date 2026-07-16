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


    const toNonNegativeInt = (value) => {
        const parsed = Number.parseInt(value ?? "0", 10);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
    };

    const synchronizeInspectionRow = (row, source) => {
        const accepted = row.querySelector("[data-return-accepted]");
        const rejected = row.querySelector("[data-return-rejected]");
        const restock = row.querySelector("[data-return-restock]");
        const writeOff = row.querySelector("[data-return-writeoff]");
        const condition = row.querySelector("[data-return-condition]");
        if (!accepted || !rejected || !restock || !writeOff || !condition) return;

        const received = toNonNegativeInt(accepted.max);
        let acceptedQuantity = Math.min(received, toNonNegativeInt(accepted.value));
        let rejectedQuantity = Math.min(received, toNonNegativeInt(rejected.value));

        if (source === accepted) {
            rejectedQuantity = received - acceptedQuantity;
        } else if (source === rejected) {
            acceptedQuantity = received - rejectedQuantity;
        } else if (acceptedQuantity + rejectedQuantity !== received) {
            rejectedQuantity = received - acceptedQuantity;
        }

        accepted.value = String(acceptedQuantity);
        rejected.value = String(rejectedQuantity);

        switch (condition.value) {
            case "Restockable":
                condition.setCustomValidity("");
                restock.value = String(acceptedQuantity);
                writeOff.value = "0";
                restock.readOnly = true;
                writeOff.readOnly = true;
                break;
            case "Mixed": {
                condition.setCustomValidity(
                    acceptedQuantity < 2
                        ? "Tình trạng hỗn hợp cần ít nhất 2 sản phẩm được chấp nhận."
                        : ""
                );
                restock.readOnly = false;
                writeOff.readOnly = false;
                let restockQuantity = Math.min(
                    acceptedQuantity,
                    toNonNegativeInt(restock.value)
                );
                let writeOffQuantity = Math.min(
                    acceptedQuantity,
                    toNonNegativeInt(writeOff.value)
                );

                if (source === restock) {
                    writeOffQuantity = acceptedQuantity - restockQuantity;
                } else if (source === writeOff) {
                    restockQuantity = acceptedQuantity - writeOffQuantity;
                } else if (restockQuantity <= 0 || writeOffQuantity <= 0
                    || restockQuantity + writeOffQuantity !== acceptedQuantity) {
                    restockQuantity = acceptedQuantity > 1
                        ? Math.max(1, acceptedQuantity - 1)
                        : acceptedQuantity;
                    writeOffQuantity = acceptedQuantity - restockQuantity;
                }

                restock.value = String(restockQuantity);
                writeOff.value = String(writeOffQuantity);
                break;
            }
            default:
                condition.setCustomValidity("");
                restock.value = "0";
                writeOff.value = String(acceptedQuantity);
                restock.readOnly = true;
                writeOff.readOnly = true;
                break;
        }
    };

    page.querySelectorAll("[data-return-inspection-row]").forEach((row) => {
        const controls = row.querySelectorAll(
            "[data-return-accepted], [data-return-rejected], [data-return-restock], "
            + "[data-return-writeoff], [data-return-condition]"
        );
        controls.forEach((control) => {
            control.addEventListener("input", () => synchronizeInspectionRow(row, control));
            control.addEventListener("change", () => synchronizeInspectionRow(row, control));
        });
        synchronizeInspectionRow(row, null);
    });

    if (form) loadServices();
})();
