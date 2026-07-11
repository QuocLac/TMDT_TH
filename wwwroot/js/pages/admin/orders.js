(() => {
    "use strict";

    document.querySelectorAll("[data-order-transition-form]").forEach((form) => {
        form.addEventListener("submit", (event) => {
            if (!form.reportValidity()) {
                event.preventDefault();
                return;
            }

            const select = form.querySelector("select[name='TargetStatus']");
            const label = form.dataset.transitionLabel ?? "trạng thái";
            const target = select?.selectedOptions?.[0]?.textContent?.trim() ?? "trạng thái mới";
            const confirmed = window.confirm(
                `Xác nhận cập nhật ${label} sang “${target}”?\n\nThao tác sẽ được ghi vào timeline và có kiểm tra concurrency.`
            );

            if (!confirmed) {
                event.preventDefault();
                return;
            }

            form.setAttribute("aria-busy", "true");
            const submitButton = form.querySelector("button[type='submit']");
            if (submitButton) {
                submitButton.disabled = true;
                submitButton.textContent = "Đang cập nhật...";
            }
        });
    });
})();
