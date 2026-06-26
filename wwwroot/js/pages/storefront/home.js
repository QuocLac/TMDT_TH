(() => {
    "use strict";

    const countdowns = document.querySelectorAll("[data-countdown]");
    if (!countdowns.length) return;

    const pad = (value) => String(Math.max(0, value)).padStart(2, "0");

    function updateCountdown(element) {
        const endAt = Date.parse(element.dataset.countdownEnd ?? "");
        if (!Number.isFinite(endAt)) {
            element.hidden = true;
            return;
        }

        const remaining = Math.max(0, endAt - Date.now());
        const totalSeconds = Math.floor(remaining / 1000);
        const days = Math.floor(totalSeconds / 86400);
        const hours = Math.floor((totalSeconds % 86400) / 3600);
        const minutes = Math.floor((totalSeconds % 3600) / 60);
        const seconds = totalSeconds % 60;

        element.querySelector("[data-countdown-days]").textContent = pad(days);
        element.querySelector("[data-countdown-hours]").textContent = pad(hours);
        element.querySelector("[data-countdown-minutes]").textContent = pad(minutes);
        element.querySelector("[data-countdown-seconds]").textContent = pad(seconds);

        if (remaining === 0) {
            element.dataset.expired = "true";
        }
    }

    countdowns.forEach(updateCountdown);
    const timer = window.setInterval(() => {
        countdowns.forEach(updateCountdown);
        if ([...countdowns].every((item) => item.dataset.expired === "true")) {
            window.clearInterval(timer);
        }
    }, 1000);
})();
