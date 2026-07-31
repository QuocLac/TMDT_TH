(() => {
    "use strict";

    const page = document.querySelector(
        "[data-price-history-page]");

    if (!page) {
        return;
    }

    const form = page.querySelector(
        "[data-price-history-filter]");
    const fromInput = page.querySelector(
        "[data-price-history-from]");
    const toInput = page.querySelector(
        "[data-price-history-to]");
    const catalogSearch = page.querySelector(
        "[data-price-history-catalog-search]");
    const productList = page.querySelector(
        "[data-price-history-product-list]");
    const catalogEmpty = page.querySelector(
        "[data-price-history-catalog-empty]");
    const filterButtons = [
        ...page.querySelectorAll(
            "[data-price-history-catalog-filter]")
    ];

    let activeCatalogFilter = "all";

    function normalize(value) {
        return String(value ?? "")
            .trim()
            .toLocaleLowerCase("vi-VN");
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

    function matchesState(item) {
        if (activeCatalogFilter === "all") {
            return true;
        }

        const states = new Set(
            normalize(item.dataset.state).split(/\s+/));

        return states.has(activeCatalogFilter);
    }

    function refreshCatalog() {
        if (!productList) {
            return;
        }

        const query = normalize(catalogSearch?.value);
        let visibleProductCount = 0;

        productList
            .querySelectorAll(
                "[data-price-history-product-card]")
            .forEach(product => {
                const productText = normalize(
                    product.dataset.search);
                let visibleVariantCount = 0;

                product
                    .querySelectorAll(
                        "[data-price-history-variant-item]")
                    .forEach(variant => {
                        const variantText = normalize(
                            variant.dataset.search);
                        const queryMatches =
                            query.length === 0
                            || productText.includes(query)
                            || variantText.includes(query);
                        const stateMatches =
                            matchesState(variant);
                        const visible =
                            queryMatches && stateMatches;

                        variant.hidden = !visible;
                        if (visible) {
                            visibleVariantCount += 1;
                        }
                    });

                const productVisible =
                    visibleVariantCount > 0;
                product.hidden = !productVisible;

                if (productVisible) {
                    visibleProductCount += 1;

                    if (query.length > 0
                        || activeCatalogFilter !== "all") {
                        product.open = true;
                    }
                }
            });

        if (catalogEmpty) {
            catalogEmpty.hidden = visibleProductCount > 0;
        }
    }

    catalogSearch?.addEventListener(
        "input",
        refreshCatalog);

    filterButtons.forEach(button => {
        button.addEventListener("click", () => {
            activeCatalogFilter =
                button.dataset.priceHistoryCatalogFilter
                ?? "all";

            filterButtons.forEach(item => {
                item.classList.toggle(
                    "is-active",
                    item === button);
            });

            refreshCatalog();
        });
    });

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

    refreshCatalog();
})();
