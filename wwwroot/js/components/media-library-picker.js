(() => {
    "use strict";

    const uploadEndpoint = "/media/images";

    function initialize(root) {
        if (root.dataset.mediaPickerReady === "true") return;
        root.dataset.mediaPickerReady = "true";

        const input = root.querySelector("[data-media-file-input]");
        const hidden = root.querySelector("[data-media-values]");
        const preview = root.querySelector("[data-media-preview]");
        const message = root.querySelector("[data-media-message]");
        const choose = root.querySelector("[data-media-choose]");
        const maximum = Number.parseInt(root.dataset.mediaMaximum ?? "5", 10);

        if (!input || !hidden || !preview) return;

        const values = () => hidden.value
            .split(/[\r\n,]+/)
            .map(value => value.trim())
            .filter(Boolean);

        function render() {
            preview.replaceChildren();
            values().forEach((url, index) => {
                const card = document.createElement("article");
                card.className = "media-picker__item";

                const image = document.createElement("img");
                image.src = url;
                image.alt = `Ảnh đã chọn ${index + 1}`;
                image.loading = "lazy";

                const remove = document.createElement("button");
                remove.type = "button";
                remove.className = "media-picker__remove";
                remove.setAttribute("aria-label", `Bỏ ảnh ${index + 1}`);
                remove.innerHTML = '<i class="fa-solid fa-xmark" aria-hidden="true"></i>';
                remove.addEventListener("click", () => {
                    const current = values();
                    current.splice(index, 1);
                    hidden.value = current.join("\n");
                    render();
                });

                card.append(image, remove);
                preview.append(card);
            });

            root.classList.toggle("has-items", values().length > 0);
        }

        function setMessage(text, tone = "") {
            if (!message) return;
            message.textContent = text;
            message.dataset.tone = tone;
            message.hidden = !text;
        }

        function getAntiforgeryToken() {
            return root.closest("form")
                ?.querySelector('input[name="__RequestVerificationToken"]')
                ?.value ?? "";
        }

        async function upload(files) {
            const current = values();
            const remaining = maximum - current.length;

            if (remaining <= 0) {
                setMessage(`Bạn đã chọn đủ ${maximum} ảnh.`, "warning");
                return;
            }

            const selected = Array.from(files).slice(0, remaining);
            if (selected.length === 0) return;

            const formData = new FormData();
            selected.forEach(file => formData.append("files", file));

            root.classList.add("is-uploading");
            input.disabled = true;
            choose?.setAttribute("aria-disabled", "true");
            setMessage("Đang tải ảnh…", "info");

            try {
                const headers = {};
                const token = getAntiforgeryToken();
                if (token) headers.RequestVerificationToken = token;

                const response = await fetch(uploadEndpoint, {
                    method: "POST",
                    body: formData,
                    credentials: "same-origin",
                    headers
                });

                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.success) {
                    throw new Error(payload?.message ?? "Không thể tải ảnh.");
                }

                const uploaded = Array.isArray(payload.items)
                    ? payload.items.map(item => item.url).filter(Boolean)
                    : [];

                hidden.value = [...current, ...uploaded]
                    .slice(0, maximum)
                    .join("\n");
                render();
                setMessage(
                    `${uploaded.length} ảnh đã được thêm.`,
                    "success"
                );
            } catch (error) {
                setMessage(
                    error instanceof Error
                        ? error.message
                        : "Không thể tải ảnh.",
                    "danger"
                );
            } finally {
                root.classList.remove("is-uploading");
                input.disabled = false;
                choose?.removeAttribute("aria-disabled");
                input.value = "";
            }
        }

        choose?.addEventListener("click", () => input.click());
        input.addEventListener("change", event => upload(event.target.files));
        render();
    }

    document.querySelectorAll("[data-media-picker]").forEach(initialize);
})();
