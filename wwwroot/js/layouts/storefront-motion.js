(() => {
    "use strict";

    const root = document.documentElement;
    const body = document.body;
    const loader = document.querySelector("[data-page-loader]");
    const header = document.querySelector("[data-storefront-header]");
    const progress = document.querySelector("[data-scroll-progress]");
    const backToTop = document.querySelector("[data-back-to-top]");
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    root.classList.add("has-storefront-motion");

    function finishLoading() {
        body.classList.add("is-storefront-ready");
        loader?.setAttribute("aria-hidden", "true");
        window.setTimeout(() => loader?.remove(), reduceMotion ? 0 : 700);
    }

    if (document.readyState === "complete") {
        finishLoading();
    } else {
        window.addEventListener("load", finishLoading, { once: true });
        window.setTimeout(finishLoading, 2200);
    }

    function updateScrollState() {
        const scrollTop = window.scrollY || document.documentElement.scrollTop;
        const scrollRange = Math.max(
            1,
            document.documentElement.scrollHeight - window.innerHeight
        );
        const ratio = Math.min(1, Math.max(0, scrollTop / scrollRange));

        header?.classList.toggle("is-scrolled", scrollTop > 24);
        backToTop?.classList.toggle("is-visible", scrollTop > 520);

        if (progress) {
            progress.style.transform = `scaleX(${ratio})`;
        }
    }

    let scrollFrame = 0;
    window.addEventListener("scroll", () => {
        if (scrollFrame) return;

        scrollFrame = window.requestAnimationFrame(() => {
            updateScrollState();
            scrollFrame = 0;
        });
    }, { passive: true });
    updateScrollState();

    backToTop?.addEventListener("click", () => {
        window.scrollTo({
            top: 0,
            behavior: reduceMotion ? "auto" : "smooth"
        });
    });

    const revealItems = [...document.querySelectorAll("[data-reveal]")];

    if (reduceMotion || !("IntersectionObserver" in window)) {
        revealItems.forEach((item) => item.classList.add("is-revealed"));
    } else {
        const observer = new IntersectionObserver((entries, currentObserver) => {
            entries.forEach((entry) => {
                if (!entry.isIntersecting) return;

                entry.target.classList.add("is-revealed");
                currentObserver.unobserve(entry.target);
            });
        }, {
            threshold: 0.12,
            rootMargin: "0px 0px -7% 0px"
        });

        revealItems.forEach((item, index) => {
            item.style.setProperty("--reveal-order", String(index % 6));
            observer.observe(item);
        });
    }

    document.querySelectorAll("[data-progressive-image]").forEach((image) => {
        const markLoaded = () => image.classList.add("is-loaded");

        if (image.complete) {
            markLoaded();
        } else {
            image.addEventListener("load", markLoaded, { once: true });
            image.addEventListener("error", markLoaded, { once: true });
        }
    });

    if (!reduceMotion && window.matchMedia("(pointer: fine)").matches) {
        document.querySelectorAll("[data-tilt-card]").forEach((card) => {
            let frame = 0;

            card.addEventListener("pointermove", (event) => {
                if (frame) return;

                frame = window.requestAnimationFrame(() => {
                    const bounds = card.getBoundingClientRect();
                    const x = (event.clientX - bounds.left) / bounds.width;
                    const y = (event.clientY - bounds.top) / bounds.height;

                    card.style.setProperty("--tilt-x", `${((0.5 - y) * 5).toFixed(2)}deg`);
                    card.style.setProperty("--tilt-y", `${((x - 0.5) * 5).toFixed(2)}deg`);
                    card.style.setProperty("--pointer-x", `${(x * 100).toFixed(1)}%`);
                    card.style.setProperty("--pointer-y", `${(y * 100).toFixed(1)}%`);
                    frame = 0;
                });
            });

            card.addEventListener("pointerleave", () => {
                card.style.setProperty("--tilt-x", "0deg");
                card.style.setProperty("--tilt-y", "0deg");
                card.style.setProperty("--pointer-x", "50%");
                card.style.setProperty("--pointer-y", "50%");
            });
        });

        const hero = document.querySelector("[data-hero]");
        hero?.addEventListener("pointermove", (event) => {
            const bounds = hero.getBoundingClientRect();

            hero.style.setProperty(
                "--hero-pointer-x",
                `${(((event.clientX - bounds.left) / bounds.width) * 100).toFixed(1)}%`
            );
            hero.style.setProperty(
                "--hero-pointer-y",
                `${(((event.clientY - bounds.top) / bounds.height) * 100).toFixed(1)}%`
            );
        });
    }

    document.addEventListener("click", (event) => {
        const link = event.target.closest("a[href]");
        if (!link) return;
        if (link.target === "_blank" || event.metaKey || event.ctrlKey || event.shiftKey) return;

        const destination = new URL(link.href, window.location.href);
        if (destination.origin !== window.location.origin) return;
        if (destination.pathname === window.location.pathname && destination.hash) return;

        body.classList.add("is-navigating");
    });

    document.querySelectorAll("form").forEach((form) => {
        form.addEventListener("submit", () => {
            body.classList.add("is-navigating");
        });
    });
})();
