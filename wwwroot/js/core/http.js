(() => {
    "use strict";

    const token = document.querySelector('meta[name="request-verification-token"]')?.content ?? "";

    async function request(url, options = {}) {
        const headers = new Headers(options.headers ?? {});
        if (token && !headers.has("RequestVerificationToken")) {
            headers.set("RequestVerificationToken", token);
        }

        const response = await fetch(url, {
            credentials: "same-origin",
            ...options,
            headers
        });

        const contentType = response.headers.get("content-type") ?? "";
        const payload = contentType.includes("application/json")
            ? await response.json()
            : await response.text();

        if (!response.ok) {
            const message = typeof payload === "object" && payload?.message
                ? payload.message
                : `Yêu cầu thất bại (${response.status}).`;
            throw new Error(message);
        }

        return payload;
    }

    function getJson(url, signal) {
        return request(url, { method: "GET", signal });
    }

    function postJson(url, body, signal) {
        return request(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
            signal
        });
    }

    function postForm(url, formData, signal) {
        return request(url, {
            method: "POST",
            body: formData,
            signal
        });
    }

    window.FastBuyHttp = Object.freeze({ request, getJson, postJson, postForm });
})();
