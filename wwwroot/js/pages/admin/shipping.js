(() => {
    "use strict";

    document.querySelectorAll("[data-shipping-transition-form]")
        .forEach(form => {
            form.addEventListener("submit", event => {
                const select = form.querySelector('select[name="TargetStatus"]');
                const label = select?.selectedOptions?.[0]?.textContent?.trim()
                    ?? "trạng thái mới";

                if (!window.confirm(`Xác nhận cập nhật giao hàng sang “${label}”?`)) {
                    event.preventDefault();
                }
            });
        });
})();
