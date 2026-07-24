(() => {
    "use strict";

    document.querySelectorAll("[data-return-decision-form]")
        .forEach(form => {
            form.addEventListener("click", event => {
                const button = event.target.closest('button[type="submit"]');
                if (!button) return;

                const approved = button.value === "true";
                const message = approved
                    ? "Xác nhận chấp nhận hoàn trả toàn bộ đơn hàng?"
                    : "Xác nhận không chấp nhận yêu cầu hoàn trả?";

                if (!window.confirm(message)) {
                    event.preventDefault();
                }
            });
        });

    document.querySelectorAll("[data-refund-confirm-form]")
        .forEach(form => {
            form.addEventListener("submit", event => {
                if (!window.confirm(
                    "Xác nhận khoản tiền đã được chuyển đúng tài khoản của khách hàng?"
                )) {
                    event.preventDefault();
                }
            });
        });
})();
