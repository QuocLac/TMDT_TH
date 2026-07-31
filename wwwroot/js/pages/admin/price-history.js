(() => {
    "use strict";

    const page = document.querySelector(
        "[data-price-history-page]");

    if (!page) {
        return;
    }

    const form = page.querySelector(
        "[data-price-history-filter]");
    const productSelect = page.querySelector(
        "[data-price-history-product]");
    const variantSelect = page.querySelector(
        "[data-price-history-variant]");
    const fromInput = page.querySelector(
        "[data-price-history-from]");
    const toInput = page.querySelector(
        "[data-price-history-to]");

    function syncVariantOptions() {
        if (!productSelect || !variantSelect) {
            return;
        }

        const productId = productSelect.value;
        let firstVisible = null;
        let selectedVisible = false;

        [...variantSelect.options].forEach(option => {
            const visible =
                option.dataset.productId === productId;

            option.hidden = !visible;
            option.disabled = !visible;

            if (visible && firstVisible === null) {
                firstVisible = option;
            }

            if (visible && option.selected) {
                selectedVisible = true;
            }
        });

        if (!selectedVisible && firstVisible) {
            firstVisible.selected = true;
        }
    }

    function formatDate(date) {
        const year = date.getFullYear();
        const month = String(
            date.getMonth() + 1).padStart(2, "0");
        const day = String(
            date.getDate()).padStart(2, "0");

        return `${year}-${month}-${day}`;
    }

    function applyQuickRange(days) {
        if (!form || !fromInput || !toInput) {
            return;
        }

        const to = new Date();
        to.setHours(0, 0, 0, 0);

        const from = new Date(to);
        from.setDate(from.getDate() - (days - 1));

        fromInput.value = formatDate(from);
        toInput.value = formatDate(to);
        form.submit();
    }

    productSelect?.addEventListener(
        "change",
        syncVariantOptions);

    page.querySelectorAll(
        "[data-price-history-days]")
        .forEach(button => {
            button.addEventListener("click", () => {
                const days = Number.parseInt(
                    button.dataset.priceHistoryDays ?? "",
                    10);

                if (Number.isInteger(days)
                    && days > 0) {
                    applyQuickRange(days);
                }
            });
        });

    syncVariantOptions();
})();
