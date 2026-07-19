(() => {
    "use strict";

    const form = document.querySelector("[data-definition-form]");
    if (!form) return;

    const typeSelect = form.querySelector("[data-definition-type]");
    const choiceField = form.querySelector("[data-choice-labels]");
    const unitField = form.querySelector("[data-number-unit]");

    function syncConditionalFields() {
        const value = typeSelect?.value ?? "";

        if (choiceField) {
            choiceField.hidden = value !== "SingleChoice";
        }

        if (unitField) {
            unitField.hidden = value !== "Number";
        }
    }

    function setCheckbox(name, value) {
        const input = form.querySelector(`[name="Input.${name}"]`);
        if (input) {
            input.checked = value === "true";
        }
    }

    document.addEventListener("click", (event) => {
        const editButton = event.target.closest("[data-edit-definition]");
        if (!editButton) return;

        const row = editButton.closest("[data-definition-row]");
        if (!row) return;

        form.querySelector("[data-definition-id]").value = row.dataset.id ?? "0";
        form.querySelector("[data-definition-code]").value =
            row.dataset.code ?? "";
        form.querySelector("[data-definition-name]").value =
            row.dataset.name ?? "";

        const helpInput = form.querySelector('[name="Input.HelpText"]');
        if (helpInput) helpInput.value = row.dataset.helpText ?? "";

        typeSelect.value = row.dataset.type ?? "ShortText";

        const unitInput = form.querySelector('[name="Input.Unit"]');
        if (unitInput) unitInput.value = row.dataset.unit ?? "";

        const choicesInput =
            form.querySelector('[name="Input.ChoiceLabels"]');
        if (choicesInput) choicesInput.value = row.dataset.options ?? "";

        setCheckbox("IsCustomerVisible", row.dataset.customerVisible);
        setCheckbox("IsFilterable", row.dataset.filterable);
        setCheckbox("IsComparable", row.dataset.comparable);
        setCheckbox("IsActive", row.dataset.active);

        syncConditionalFields();
        form.scrollIntoView({ behavior: "smooth", block: "start" });
    });

    form.querySelector("[data-definition-reset]")
        ?.addEventListener("click", () => {
            window.setTimeout(() => {
                form.querySelector("[data-definition-id]").value = "0";
                syncConditionalFields();
            }, 0);
        });

    typeSelect?.addEventListener("change", syncConditionalFields);
    syncConditionalFields();
})();
