(() => {
    "use strict";

    function initialize(root) {
        if (root.dataset.reviewExperienceReady === "true") return;
        root.dataset.reviewExperienceReady = "true";

        const feed = root.querySelector("[data-review-feed]");
        const feedUrl = root.dataset.feedUrl;
        const loadMore = root.querySelector("[data-review-load-more]");
        const loading = root.querySelector("[data-review-loading]");
        const resultCount = root.querySelector("[data-review-result-count]");
        const mediaFilter = root.querySelector("[data-review-media-filter]");
        const lightbox = root.querySelector("[data-review-lightbox]");
        const lightboxContent = root.querySelector("[data-review-lightbox-content]");

        if (!feed || !feedUrl) return;

        const state = {
            page: 1,
            rating: "",
            mediaOnly: false,
            busy: false
        };

        function lastBatch() {
            const batches = feed.querySelectorAll("[data-review-batch]");
            return batches.length ? batches[batches.length - 1] : null;
        }

        function syncMeta() {
            const batch = lastBatch();
            if (!batch) return;

            state.page = Number.parseInt(batch.dataset.page ?? "1", 10);
            const totalPages = Number.parseInt(
                batch.dataset.totalPages ?? "0",
                10
            );
            const filteredCount = Number.parseInt(
                batch.dataset.filteredCount ?? "0",
                10
            );

            if (resultCount) {
                resultCount.textContent = `${filteredCount} đánh giá`;
            }

            if (loadMore) {
                loadMore.hidden = totalPages === 0 || state.page >= totalPages;
            }
        }

        function setBusy(busy) {
            state.busy = busy;
            if (loading) loading.hidden = !busy;
            if (loadMore) loadMore.disabled = busy;
            root.querySelectorAll("[data-review-rating], [data-review-media-filter]")
                .forEach(button => {
                    button.disabled = busy;
                });
        }

        function buildUrl(page) {
            const url = new URL(feedUrl, window.location.origin);
            url.searchParams.set("page", String(page));
            if (state.rating) {
                url.searchParams.set("rating", state.rating);
            }
            if (state.mediaOnly) {
                url.searchParams.set("mediaOnly", "true");
            }
            return url;
        }

        async function load(page, append) {
            if (state.busy) return;
            setBusy(true);

            try {
                const response = await fetch(buildUrl(page), {
                    method: "GET",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    },
                    credentials: "same-origin"
                });

                if (!response.ok) {
                    throw new Error("Không thể tải đánh giá lúc này.");
                }

                const html = await response.text();

                if (append) {
                    feed.insertAdjacentHTML("beforeend", html);
                } else {
                    feed.innerHTML = html;
                }

                syncMeta();
            } catch (error) {
                const message = document.createElement("div");
                message.className = "review-experience__load-error";
                message.textContent =
                    error instanceof Error
                        ? error.message
                        : "Không thể tải đánh giá.";
                feed.prepend(message);
            } finally {
                setBusy(false);
            }
        }

        root.addEventListener("click", event => {
            const ratingButton = event.target.closest("[data-review-rating]");
            if (ratingButton) {
                state.rating = ratingButton.dataset.reviewRating ?? "";
                state.page = 1;

                root.querySelectorAll("[data-review-rating]")
                    .forEach(button => {
                        button.classList.toggle(
                            "is-active",
                            (button.dataset.reviewRating ?? "") === state.rating
                        );
                    });

                load(1, false);
                return;
            }

            const mediaButton = event.target.closest("[data-review-media-filter]");
            if (mediaButton) {
                state.mediaOnly = !state.mediaOnly;
                mediaButton.classList.toggle("is-active", state.mediaOnly);
                mediaButton.setAttribute(
                    "aria-pressed",
                    state.mediaOnly ? "true" : "false"
                );
                state.page = 1;
                load(1, false);
                return;
            }

            const expandButton = event.target.closest("[data-review-expand]");
            if (expandButton) {
                const card = expandButton.closest("[data-review-card]");
                const copy = card?.querySelector("[data-review-copy]");
                if (!copy) return;

                const expanded = expandButton.getAttribute("aria-expanded") === "true";
                copy.classList.toggle("is-collapsed", expanded);
                expandButton.setAttribute(
                    "aria-expanded",
                    expanded ? "false" : "true"
                );
                expandButton.textContent =
                    expanded ? "Xem thêm nội dung" : "Thu gọn";
                return;
            }

            const mediaButtonOpen = event.target.closest("[data-review-media-src]");
            if (mediaButtonOpen) {
                const src = mediaButtonOpen.dataset.reviewMediaSrc;
                const type = mediaButtonOpen.dataset.reviewMediaType;
                if (!src) return;

                if (!lightbox || typeof lightbox.showModal !== "function") {
                    window.open(src, "_blank", "noopener,noreferrer");
                    return;
                }

                lightboxContent.replaceChildren();
                const element = type === "Video"
                    ? document.createElement("video")
                    : document.createElement("img");

                element.src = src;
                if (element instanceof HTMLVideoElement) {
                    element.controls = true;
                    element.autoplay = true;
                } else {
                    element.alt = "Nội dung đánh giá của khách hàng";
                }

                lightboxContent.append(element);
                lightbox.showModal();
            }
        });

        loadMore?.addEventListener("click", () => {
            load(state.page + 1, true);
        });

        root.querySelector("[data-review-lightbox-close]")
            ?.addEventListener("click", () => lightbox?.close());

        lightbox?.addEventListener("click", event => {
            if (event.target === lightbox) lightbox.close();
        });

        syncMeta();
    }

    document.querySelectorAll("[data-review-experience]")
        .forEach(initialize);
})();
