(() => {
    "use strict";

    function getDialog(id) {
        const element = document.getElementById(id);
        return element instanceof HTMLDialogElement ? element : null;
    }

    document.addEventListener("click", (event) => {
        const opener = event.target.closest("[data-order-dialog-open]");
        if (opener) {
            const dialog = getDialog(opener.dataset.orderDialogOpen);
            if (dialog && !dialog.open) {
                dialog.showModal();
            }
            return;
        }

        const closeButton = event.target.closest("[data-order-dialog-close]");
        if (closeButton) {
            const dialog = closeButton.closest("dialog");
            if (dialog instanceof HTMLDialogElement) {
                dialog.close();
            }
        }
    });

    document.querySelectorAll("[data-order-dialog]").forEach((dialog) => {
        dialog.addEventListener("click", (event) => {
            if (event.target === dialog) {
                dialog.close();
            }
        });
    });

    document.querySelectorAll("[data-order-transition-form]").forEach((form) => {
        form.addEventListener("submit", (event) => {
            if (!form.reportValidity()) {
                event.preventDefault();
                return;
            }

            form.setAttribute("aria-busy", "true");

            const submitButton = form.querySelector("button[type='submit']");
            if (submitButton) {
                submitButton.disabled = true;
                submitButton.dataset.originalText = submitButton.textContent?.trim() ?? "";
                submitButton.textContent = "Đang cập nhật...";
            }
        });
    });
})();
