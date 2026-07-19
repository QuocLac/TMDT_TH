(() => {
    "use strict";

    const bulkForm = document.querySelector(
        "#catalog-publication-bulk-form");
    const selectAll = document.querySelector(
        "[data-catalog-select-all]");
    const productCheckboxes = [
        ...document.querySelectorAll(
            "[data-catalog-product-select]")
    ];
    const countElement = document.querySelector(
        "[data-catalog-selected-count]");
    const bulkButtons = [
        ...document.querySelectorAll(
            "[data-catalog-bulk-action]")
    ];

    function updateSelectionState() {
        const selectedCount = productCheckboxes.filter(
            (checkbox) => checkbox.checked
        ).length;

        if (countElement) {
            countElement.textContent = String(selectedCount);
        }

        bulkButtons.forEach((button) => {
            button.disabled = selectedCount === 0;
        });

        if (selectAll) {
            selectAll.checked =
                productCheckboxes.length > 0
                && selectedCount === productCheckboxes.length;
            selectAll.indeterminate =
                selectedCount > 0
                && selectedCount < productCheckboxes.length;
        }
    }

    selectAll?.addEventListener("change", () => {
        productCheckboxes.forEach((checkbox) => {
            checkbox.checked = selectAll.checked;
        });
        updateSelectionState();
    });

    productCheckboxes.forEach((checkbox) => {
        checkbox.addEventListener(
            "change",
            updateSelectionState);
    });

    document.addEventListener("submit", (event) => {
        const submitter = event.submitter;
        const message = submitter?.dataset.confirmMessage;

        if (message && !window.confirm(message)) {
            event.preventDefault();
            return;
        }

        if (event.target === bulkForm) {
            const selectedCount = productCheckboxes.filter(
                (checkbox) => checkbox.checked
            ).length;

            if (selectedCount === 0) {
                event.preventDefault();
            }
        }
    });

    updateSelectionState();
})();
