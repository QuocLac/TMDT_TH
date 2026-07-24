(() => {
    "use strict";

    const version = "2026.07.25.1";

    if (window.FastBuyIcons?.version === version) {
        return;
    }

    const currentScript = document.currentScript;
    const stylesheetUrl = currentScript?.src
        ? new URL("../../css/components/icons.css", currentScript.src)
        : new URL("/css/components/icons.css", window.location.origin);

    stylesheetUrl.searchParams.set("v", version);

    function ensureStylesheet() {
        if (document.querySelector("[data-fastbuy-icon-styles]")) {
            return;
        }

        const link = document.createElement("link");
        link.rel = "stylesheet";
        link.href = stylesheetUrl.href;
        link.dataset.fastbuyIconStyles = "true";
        document.head.append(link);
    }

    const nonIconClasses = new Set([
        "fa",
        "fas",
        "far",
        "fab",
        "fa-solid",
        "fa-regular",
        "fa-brands",
        "fa-thin",
        "fa-light",
        "fa-duotone",
        "fa-sharp",
        "fa-fw",
        "fa-spin",
        "fa-spin-pulse",
        "fa-pulse",
        "fa-beat",
        "fa-bounce",
        "fa-fade",
        "fa-flip",
        "fa-shake",
        "fa-xs",
        "fa-sm",
        "fa-lg",
        "fa-xl",
        "fa-2xl",
        "fa-1x",
        "fa-2x",
        "fa-3x",
        "fa-4x",
        "fa-5x",
        "fa-6x",
        "fa-7x",
        "fa-8x",
        "fa-9x",
        "fa-10x"
    ]);

    const iconToShape = Object.freeze({
        "plus": "plus",
        "minus": "minus",
        "check": "check",
        "circle-check": "circle-check",
        "check-circle": "circle-check",
        "xmark": "xmark",
        "times": "xmark",
        "ban": "ban",
        "pen": "pen",
        "pen-to-square": "edit",
        "edit": "edit",
        "trash-can": "trash",
        "trash": "trash",
        "eye": "eye",
        "eye-slash": "eye-off",
        "magnifying-glass": "search",
        "search": "search",
        "magnifying-glass-chart": "search-chart",
        "filter": "filter",
        "arrow-left": "arrow-left",
        "arrow-right": "arrow-right",
        "arrow-up": "arrow-up",
        "arrow-down": "arrow-down",
        "arrow-up-right-from-square": "external",
        "external-link": "external",
        "chevron-left": "chevron-left",
        "chevron-right": "chevron-right",
        "chevron-up": "chevron-up",
        "chevron-down": "chevron-down",
        "rotate": "rotate",
        "rotate-left": "rotate-left",
        "reply": "reply",
        "paper-plane": "paper-plane",
        "play": "play",
        "pause": "pause",
        "download": "download",
        "upload": "upload",
        "spinner": "loader",

        "bag-shopping": "bag",
        "shopping-bag": "bag",
        "cart-shopping": "cart",
        "shopping-cart": "cart",
        "receipt": "receipt",
        "coins": "coins",
        "money-bill": "coins",
        "money-bill-transfer": "bank-transfer",
        "credit-card": "credit-card",
        "building-columns": "bank",
        "bank": "bank",
        "qrcode": "qrcode",

        "box": "box",
        "box-open": "box-open",
        "box-archive": "archive",
        "archive": "archive",
        "boxes-stacked": "boxes",
        "boxes-packing": "boxes-packing",
        "truck": "truck",
        "truck-fast": "truck-fast",
        "truck-ramp-box": "truck-ramp",
        "clipboard-check": "clipboard-check",
        "clipboard-list": "clipboard-list",
        "rectangle-list": "clipboard-list",
        "list": "clipboard-list",

        "tag": "tag",
        "tags": "tags",
        "gift": "gift",
        "star": "star",
        "heart": "heart",
        "comments": "comments",
        "comment-dots": "comment",
        "comment": "comment",
        "image": "image",
        "images": "images",
        "video": "video",
        "file": "file",
        "file-image": "image",
        "file-circle-plus": "file-plus",

        "hourglass-half": "hourglass",
        "clock": "clock",
        "calendar": "calendar",
        "calendar-days": "calendar",
        "location-dot": "location",
        "map-marker-alt": "location",
        "phone": "phone",
        "envelope": "envelope",
        "lock": "lock",
        "unlock": "unlock",
        "user": "user",
        "user-circle": "user",
        "circle-user": "user",
        "shield": "shield",
        "shield-halved": "shield",
        "shield-heart": "shield-heart",
        "triangle-exclamation": "alert",
        "circle-exclamation": "alert-circle",
        "circle-info": "info",
        "info-circle": "info",

        "folder": "folder",
        "folder-open": "folder-open",
        "layer-group": "layers",
        "border-all": "grid",
        "icons": "shapes",

        "microchip": "chip",
        "mobile-screen-button": "mobile",
        "mobile-alt": "mobile",
        "laptop": "laptop",
        "tablet-screen-button": "tablet",
        "tablet-alt": "tablet",
        "tv": "monitor",
        "camera": "camera",
        "headphones": "headphones",
        "gamepad": "gamepad",
        "keyboard": "keyboard",

        "shirt": "shirt",
        "shoe-prints": "shoe",
        "glasses": "glasses",
        "gem": "gem",
        "watch": "watch",
        "house": "house",
        "couch": "sofa",
        "kitchen-set": "utensils",
        "blender": "appliance",
        "lightbulb": "bulb",
        "screwdriver-wrench": "wrench",
        "palette": "palette",
        "wand-magic-sparkles": "sparkles",
        "heart-pulse": "heart-pulse",
        "spa": "flower",

        "dumbbell": "dumbbell",
        "person-running": "running",
        "bicycle": "bike",
        "campground": "tent",
        "baby": "baby",
        "puzzle-piece": "puzzle",
        "book-open": "book",
        "book": "book",
        "pen-ruler": "pen-ruler",
        "paw": "paw",

        "utensils": "utensils",
        "basket-shopping": "basket",
        "mug-hot": "cup",
        "car": "car",
        "motorcycle": "motorcycle",
        "briefcase": "briefcase",
        "print": "printer"
    });

    const shapeMarkup = Object.freeze({
        "plus": '<path d="M12 5v14M5 12h14"/>',
        "minus": '<path d="M5 12h14"/>',
        "check": '<path d="m5 12 4 4L19 6"/>',
        "circle-check":
            '<circle cx="12" cy="12" r="9"/>'
            + '<path d="m8 12 2.5 2.5L16.5 8"/>',
        "xmark": '<path d="M6 6l12 12M18 6 6 18"/>',
        "ban":
            '<circle cx="12" cy="12" r="9"/>'
            + '<path d="M5.7 5.7 18.3 18.3"/>',
        "pen":
            '<path d="m4 20 4.5-1 10-10-3.5-3.5-10 10z"/>'
            + '<path d="M13.5 6.5 17 10M4 20l1-4.5"/>',
        "edit":
            '<path d="M13 5H5a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8"/>'
            + '<path d="m11 13 8.5-8.5a2.1 2.1 0 0 1 3 3L14 16l-4 1z"/>',
        "trash":
            '<path d="M4 7h16M9 3h6l1 4H8zM6 7l1 14h10l1-14M10 11v6M14 11v6"/>',
        "eye":
            '<path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6z"/>'
            + '<circle cx="12" cy="12" r="2.7"/>',
        "eye-off":
            '<path d="M4.5 4.5 19.5 19.5"/>'
            + '<path d="M9.7 6.4A10 10 0 0 1 12 6c6 0 9.5 6 9.5 6a16 16 0 0 1-2.4 3.2M6.5 7.6A16 16 0 0 0 2.5 12s3.5 6 9.5 6a10 10 0 0 0 3-.5"/>'
            + '<path d="M10.2 10.2a2.7 2.7 0 0 0 3.6 3.6"/>',
        "search":
            '<circle cx="10.5" cy="10.5" r="6.5"/>'
            + '<path d="m15.5 15.5 5 5"/>',
        "search-chart":
            '<circle cx="10.5" cy="10.5" r="6.5"/>'
            + '<path d="m15.5 15.5 5 5M7.5 12.5V9M10.5 12.5V7M13.5 12.5v-2"/>',
        "filter":
            '<path d="M3 5h18l-7 8v6l-4 2v-8z"/>',
        "arrow-left": '<path d="M19 12H5m6-6-6 6 6 6"/>',
        "arrow-right": '<path d="M5 12h14m-6-6 6 6-6 6"/>',
        "arrow-up": '<path d="M12 19V5m-6 6 6-6 6 6"/>',
        "arrow-down": '<path d="M12 5v14m-6-6 6 6 6-6"/>',
        "external":
            '<path d="M14 4h6v6M20 4l-9 9"/>'
            + '<path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>',
        "chevron-left": '<path d="m15 18-6-6 6-6"/>',
        "chevron-right": '<path d="m9 18 6-6-6-6"/>',
        "chevron-up": '<path d="m6 15 6-6 6 6"/>',
        "chevron-down": '<path d="m6 9 6 6 6-6"/>',
        "rotate":
            '<path d="M20 6v5h-5"/>'
            + '<path d="M19 11a8 8 0 1 0-2 6"/>',
        "rotate-left":
            '<path d="M4 6v5h5"/>'
            + '<path d="M5 11a8 8 0 1 1 2 6"/>',
        "reply": '<path d="m10 7-6 5 6 5v-3h4c3 0 5 1 7 4-1-6-4-8-7-8h-4z"/>',
        "paper-plane": '<path d="m3 11 18-8-7 18-3-7zM11 14 21 3"/>',
        "play": '<path d="m8 5 11 7-11 7z"/>',
        "pause": '<path d="M8 5v14M16 5v14"/>',
        "download": '<path d="M12 3v12m-5-5 5 5 5-5M4 20h16"/>',
        "upload": '<path d="M12 16V4m-5 5 5-5 5 5M4 20h16"/>',
        "loader": '<path d="M12 3a9 9 0 1 1-6.4 2.6"/>',

        "bag":
            '<path d="M5 8h14l1 13H4z"/>'
            + '<path d="M8.5 9V6.5a3.5 3.5 0 0 1 7 0V9"/>',
        "cart":
            '<path d="M3 4h2l1.7 9.1a2 2 0 0 0 2 1.6h7.8a2 2 0 0 0 1.9-1.4L20 7H6.1"/>'
            + '<circle cx="9" cy="20" r="1"/>'
            + '<circle cx="17" cy="20" r="1"/>',
        "receipt":
            '<path d="M6 3h12v18l-3-2-3 2-3-2-3 2z"/>'
            + '<path d="M9 8h6M9 12h6M9 16h4"/>',
        "coins":
            '<ellipse cx="9" cy="7" rx="5" ry="2.5"/>'
            + '<path d="M4 7v4c0 1.4 2.2 2.5 5 2.5s5-1.1 5-2.5V7"/>'
            + '<path d="M10 16.5c.8 1 2.7 1.7 5 1.7 2.8 0 5-1.1 5-2.5v-4"/>'
            + '<ellipse cx="15" cy="11.7" rx="5" ry="2.5"/>',
        "bank-transfer":
            '<path d="M3 9h18M5 9v8M9 9v8M15 9v8M19 9v8M3 20h18M12 3l9 4H3z"/>'
            + '<path d="M8 13h8m-2-2 2 2-2 2"/>',
        "credit-card":
            '<rect x="3" y="5" width="18" height="14" rx="2"/>'
            + '<path d="M3 10h18M7 15h4"/>',
        "bank":
            '<path d="M3 9h18M5 9v8M9 9v8M15 9v8M19 9v8M3 20h18M12 3l9 4H3z"/>',
        "qrcode":
            '<rect x="3" y="3" width="7" height="7"/>'
            + '<rect x="14" y="3" width="7" height="7"/>'
            + '<rect x="3" y="14" width="7" height="7"/>'
            + '<path d="M14 14h3v3h-3zM18 18h3v3h-3zM14 20h2M20 14v2"/>',

        "box":
            '<path d="m4 7 8-4 8 4-8 4z"/>'
            + '<path d="M4 7v10l8 4 8-4V7M12 11v10"/>',
        "box-open":
            '<path d="m4 9 4-5 4 4 4-4 4 5-8 4z"/>'
            + '<path d="M4 9v8l8 4 8-4V9M12 13v8"/>',
        "archive":
            '<rect x="3" y="5" width="18" height="5" rx="1"/>'
            + '<path d="M5 10v10h14V10M9 14h6"/>',
        "boxes":
            '<rect x="3" y="4" width="7" height="6" rx="1"/>'
            + '<rect x="14" y="4" width="7" height="6" rx="1"/>'
            + '<rect x="3" y="14" width="7" height="6" rx="1"/>'
            + '<rect x="14" y="14" width="7" height="6" rx="1"/>',
        "boxes-packing":
            '<path d="M3 14h8v7H3zM13 11h8v10h-8zM6 8h8v6H6z"/>'
            + '<path d="M10 3v5M7 5l3-2 3 2"/>',
        "truck":
            '<path d="M3 6h11v11H3zM14 10h4l3 4v3h-7z"/>'
            + '<circle cx="7" cy="18" r="2"/>'
            + '<circle cx="18" cy="18" r="2"/>',
        "truck-fast":
            '<path d="M7 6h8v11H7M15 10h3l3 4v3h-6"/>'
            + '<circle cx="10" cy="18" r="2"/>'
            + '<circle cx="18" cy="18" r="2"/>'
            + '<path d="M2 9h4M1 12h5M3 15h3"/>',
        "truck-ramp":
            '<path d="M4 5h11v11H4zM15 9h3l3 4v3h-6"/>'
            + '<circle cx="8" cy="17" r="2"/>'
            + '<circle cx="18" cy="17" r="2"/>'
            + '<path d="M2 21h11l4-4"/>',
        "clipboard-check":
            '<path d="M9 4h6l1 3h3v14H5V7h3z"/>'
            + '<path d="m9 14 2 2 4-5"/>',
        "clipboard-list":
            '<path d="M9 4h6l1 3h3v14H5V7h3z"/>'
            + '<path d="M9 11h6M9 15h6M9 19h4"/>',

        "tag":
            '<path d="M20.5 13.2 13.2 20.5a2 2 0 0 1-2.8 0L3.5 13.6V4h9.6l7.4 7.4a1.3 1.3 0 0 1 0 1.8z"/>'
            + '<circle cx="8" cy="8" r="1.2"/>',
        "tags":
            '<path d="M19 13 12 20 4 12V4h8z"/>'
            + '<path d="m13 5 7 7-3 3M8 8h.01"/>',
        "gift":
            '<rect x="3.5" y="9" width="17" height="11.5" rx="1.5"/>'
            + '<path d="M12 9v11.5M3 13h18M12 9H7.7a2.2 2.2 0 1 1 0-4.4C10.6 4.6 12 9 12 9z"/>'
            + '<path d="M12 9h4.3a2.2 2.2 0 1 0 0-4.4C13.4 4.6 12 9 12 9z"/>',
        "star": '<path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9z"/>',
        "heart": '<path d="M20.5 5.7a5 5 0 0 0-7.1 0L12 7.1l-1.4-1.4a5 5 0 0 0-7.1 7.1L12 21l8.5-8.2a5 5 0 0 0 0-7.1z"/>',
        "comments":
            '<path d="M4 5h11v9H8l-4 3z"/>'
            + '<path d="M12 9h8v8h-3l-3 3v-3h-2"/>',
        "comment":
            '<path d="M4 5h16v12H9l-5 4z"/>'
            + '<path d="M8 10h.01M12 10h.01M16 10h.01"/>',
        "image":
            '<rect x="3" y="4" width="18" height="16" rx="2"/>'
            + '<circle cx="8" cy="9" r="2"/>'
            + '<path d="m4 18 5-5 3 3 3-4 5 6"/>',
        "images":
            '<rect x="6" y="6" width="15" height="14" rx="2"/>'
            + '<path d="M3 16V4a2 2 0 0 1 2-2h12"/>'
            + '<circle cx="11" cy="11" r="2"/>'
            + '<path d="m7 18 5-5 3 3 2-2 4 4"/>',
        "video":
            '<rect x="3" y="6" width="13" height="12" rx="2"/>'
            + '<path d="m16 10 5-3v10l-5-3z"/>',
        "file":
            '<path d="M6 3h8l4 4v14H6zM14 3v5h5"/>',
        "file-plus":
            '<path d="M6 3h8l4 4v14H6zM14 3v5h5M9 14h6M12 11v6"/>',

        "hourglass":
            '<path d="M6 3h12M6 21h12M7 3c0 5 2 6 5 9-3 3-5 4-5 9M17 3c0 5-2 6-5 9 3 3 5 4 5 9"/>',
        "clock":
            '<circle cx="12" cy="12" r="9"/>'
            + '<path d="M12 7v5l3 2"/>',
        "calendar":
            '<rect x="3" y="5" width="18" height="16" rx="2"/>'
            + '<path d="M7 3v4M17 3v4M3 10h18"/>',
        "location":
            '<path d="M20 10c0 6-8 11-8 11S4 16 4 10a8 8 0 1 1 16 0z"/>'
            + '<circle cx="12" cy="10" r="2.5"/>',
        "phone":
            '<path d="M7 3h3l1.5 4-2 1.5a16 16 0 0 0 6 6l1.5-2 4 1.5v3c0 2-1.5 4-4 4C9 21 3 15 3 7c0-2.5 2-4 4-4z"/>',
        "envelope":
            '<rect x="3" y="5" width="18" height="14" rx="2"/>'
            + '<path d="m4 7 8 6 8-6"/>',
        "lock":
            '<rect x="5" y="10" width="14" height="11" rx="2"/>'
            + '<path d="M8 10V7a4 4 0 0 1 8 0v3"/>',
        "unlock":
            '<rect x="5" y="10" width="14" height="11" rx="2"/>'
            + '<path d="M16 10V7a4 4 0 0 0-7.5-2"/>',
        "user":
            '<circle cx="12" cy="8" r="4"/>'
            + '<path d="M4 21a8 8 0 0 1 16 0"/>',
        "shield":
            '<path d="m12 3 8 4v5c0 5-3.4 8-8 9-4.6-1-8-4-8-9V7z"/>',
        "shield-heart":
            '<path d="m12 3 8 4v5c0 5-3.4 8-8 9-4.6-1-8-4-8-9V7z"/>'
            + '<path d="M15.5 9.5a2.2 2.2 0 0 0-3.1 0l-.4.4-.4-.4a2.2 2.2 0 0 0-3.1 3.1L12 16l3.5-3.4a2.2 2.2 0 0 0 0-3.1z"/>',
        "alert":
            '<path d="M12 3 2.5 20h19z"/>'
            + '<path d="M12 9v5M12 17.5h.01"/>',
        "alert-circle":
            '<circle cx="12" cy="12" r="9"/>'
            + '<path d="M12 7v6M12 16.5h.01"/>',
        "info":
            '<circle cx="12" cy="12" r="9"/>'
            + '<path d="M12 11v6M12 7.5h.01"/>',

        "folder":
            '<path d="M3 7.5h6l2 2H21v9.5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>'
            + '<path d="M3 7.5V6a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v1.5"/>',
        "folder-open":
            '<path d="M3 8V6a2 2 0 0 1 2-2h4l2 2h7a2 2 0 0 1 2 2v2"/>'
            + '<path d="M4.5 10h17l-2.2 9a2 2 0 0 1-2 1.5H5a2 2 0 0 1-2-2.5z"/>',
        "layers":
            '<path d="m12 3 9 5-9 5-9-5z"/>'
            + '<path d="m3 12 9 5 9-5M3 16l9 5 9-5"/>',
        "grid":
            '<rect x="3.5" y="3.5" width="7" height="7" rx="1.2"/>'
            + '<rect x="13.5" y="3.5" width="7" height="7" rx="1.2"/>'
            + '<rect x="3.5" y="13.5" width="7" height="7" rx="1.2"/>'
            + '<rect x="13.5" y="13.5" width="7" height="7" rx="1.2"/>',
        "shapes":
            '<circle cx="7" cy="7" r="3.5"/>'
            + '<rect x="13.5" y="3.5" width="7" height="7" rx="1"/>'
            + '<path d="m7 14 4 7H3zM16.5 14l4 7h-8z"/>',

        "chip":
            '<rect x="6" y="6" width="12" height="12" rx="2"/>'
            + '<rect x="9" y="9" width="6" height="6" rx="1"/>'
            + '<path d="M9 2.5v3M15 2.5v3M9 18.5v3M15 18.5v3M2.5 9h3M2.5 15h3M18.5 9h3M18.5 15h3"/>',
        "mobile":
            '<rect x="7" y="2.5" width="10" height="19" rx="2.2"/>'
            + '<path d="M10 5h4M11 18.5h2"/>',
        "laptop":
            '<rect x="4.5" y="4" width="15" height="11" rx="1.5"/>'
            + '<path d="M2.5 19h19l-2-4H4.5z"/>',
        "tablet":
            '<rect x="5.5" y="2.5" width="13" height="19" rx="2"/>'
            + '<circle cx="12" cy="18.3" r=".7" fill="currentColor" stroke="none"/>',
        "monitor":
            '<rect x="3" y="4" width="18" height="13" rx="2"/>'
            + '<path d="M8 21h8M12 17v4"/>',
        "camera":
            '<path d="M4 7.5h3l1.3-2h7.4l1.3 2h3a1.5 1.5 0 0 1 1.5 1.5v9A1.5 1.5 0 0 1 20 19.5H4A1.5 1.5 0 0 1 2.5 18V9A1.5 1.5 0 0 1 4 7.5z"/>'
            + '<circle cx="12" cy="13.5" r="3.5"/>',
        "headphones":
            '<path d="M4 14v-2a8 8 0 0 1 16 0v2"/>'
            + '<path d="M4 13h2.5v7H5a2 2 0 0 1-2-2v-3a2 2 0 0 1 1-1.7zM20 13h-2.5v7H19a2 2 0 0 0 2-2v-3a2 2 0 0 0-1-1.7z"/>',
        "gamepad":
            '<path d="M7 8h10a4 4 0 0 1 3.8 5.3l-1.5 4.2a2.2 2.2 0 0 1-3.6.9L13.8 17h-3.6l-1.9 1.4a2.2 2.2 0 0 1-3.6-.9l-1.5-4.2A4 4 0 0 1 7 8z"/>'
            + '<path d="M7.5 12v4M5.5 14h4M16.5 12.5h.01M18.5 15h.01"/>',
        "keyboard":
            '<rect x="2.5" y="6" width="19" height="12" rx="2"/>'
            + '<path d="M6 10h.01M9 10h.01M12 10h.01M15 10h.01M18 10h.01M6 14h.01M9 14h.01M12 14h6"/>',

        "shirt":
            '<path d="M8.5 4 5 5.5 2.8 10l3.4 1.7L7.5 9v11h9V9l1.3 2.7 3.4-1.7L19 5.5 15.5 4A4 4 0 0 1 12 6a4 4 0 0 1-3.5-2z"/>',
        "shoe":
            '<path d="M4 5.5c1.8 4.4 4.8 7.2 9 8.2l5.2 1.3a3.2 3.2 0 0 1 2.3 3v1H5.7A3.7 3.7 0 0 1 2 15.3V8.5A3 3 0 0 1 4 5.5z"/>'
            + '<path d="M8.5 11.5 10 9.8M11.3 13l1.5-1.7M4 16h16"/>',
        "glasses":
            '<circle cx="7" cy="13" r="4"/>'
            + '<circle cx="17" cy="13" r="4"/>'
            + '<path d="M11 13h2M3 11 2 8M21 11l1-3"/>',
        "gem":
            '<path d="m4 8 4-4h8l4 4-8 12z"/>'
            + '<path d="M4 8h16M8 4l4 4 4-4M8 8l4 12 4-12"/>',
        "watch":
            '<circle cx="12" cy="12" r="6"/>'
            + '<path d="M9 2h6l1 4M9 22h6l1-4M12 8.5V12l2.5 1.5"/>',
        "house":
            '<path d="m3 11 9-8 9 8"/>'
            + '<path d="M5 10v10h14V10M9 20v-6h6v6"/>',
        "sofa":
            '<path d="M5 12V8a3 3 0 0 1 3-3h8a3 3 0 0 1 3 3v4"/>'
            + '<path d="M4 11a2 2 0 0 0-2 2v5h20v-5a2 2 0 0 0-4 0v1H6v-1a2 2 0 0 0-2-2zM5 18v3M19 18v3"/>',
        "utensils":
            '<path d="M6 3v8M3.5 3v5a2.5 2.5 0 0 0 5 0V3M6 11v10"/>'
            + '<path d="M16 3v18M16 3c3 2 4 5 4 8h-4"/>',
        "appliance":
            '<rect x="4" y="3" width="16" height="18" rx="2"/>'
            + '<path d="M4 8h16M8 5.5h.01M11 5.5h.01"/>'
            + '<circle cx="12" cy="14.5" r="4"/>',
        "bulb":
            '<path d="M9 18h6M9.5 21h5"/>'
            + '<path d="M8.5 15.5A6 6 0 1 1 15.5 15.5c-.8.6-1.2 1.3-1.3 2.5h-4.4c-.1-1.2-.5-1.9-1.3-2.5z"/>',
        "wrench":
            '<path d="M14.5 6.5a5 5 0 0 0-6.8 6.8L3 18l3 3 4.7-4.7a5 5 0 0 0 6.8-6.8l-3 3-3-3z"/>',
        "palette":
            '<path d="M12 3a9 9 0 1 0 0 18h1.2a1.8 1.8 0 0 0 0-3.6h-.8a1.8 1.8 0 0 1 0-3.6H15A6 6 0 0 0 12 3z"/>'
            + '<circle cx="7.5" cy="10" r=".8" fill="currentColor" stroke="none"/>'
            + '<circle cx="9.5" cy="6.8" r=".8" fill="currentColor" stroke="none"/>'
            + '<circle cx="14" cy="6.5" r=".8" fill="currentColor" stroke="none"/>',
        "sparkles":
            '<path d="m12 3 1.2 3.3L16.5 7.5l-3.3 1.2L12 12l-1.2-3.3-3.3-1.2 3.3-1.2z"/>'
            + '<path d="m18.5 13 .8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8zM5 14l.8 2.2L8 17l-2.2.8L5 20l-.8-2.2L2 17l2.2-.8z"/>',
        "heart-pulse":
            '<path d="M20.5 5.7a5 5 0 0 0-7.1 0L12 7.1l-1.4-1.4a5 5 0 0 0-7.1 7.1L12 21l8.5-8.2a5 5 0 0 0 0-7.1z"/>'
            + '<path d="M7 12h3l1-2 2 5 1-3h3"/>',
        "flower":
            '<circle cx="12" cy="12" r="2"/>'
            + '<path d="M12 10c-4-1-5-5-2-7 3 1 4 4 2 7zM14 12c1-4 5-5 7-2-1 3-4 4-7 2zM12 14c4 1 5 5 2 7-3-1-4-4-2-7zM10 12c-1 4-5 5-7 2 1-3 4-4 7-2z"/>',

        "dumbbell": '<path d="M3 9v6M6 7v10M18 7v10M21 9v6M6 12h12"/>',
        "running":
            '<circle cx="14.5" cy="4.5" r="2"/>'
            + '<path d="m12 8 3 2 3 1M12 8l-2 5 4 2 2 5M10 13l-4 2-2 4M14 15l-4 5"/>',
        "bike":
            '<circle cx="6" cy="17" r="4"/>'
            + '<circle cx="18" cy="17" r="4"/>'
            + '<path d="m6 17 4-8h4l4 8M10 9l4 8M8 6h4M14 9l2-3h3"/>',
        "tent": '<path d="m3 20 9-16 9 16zM12 4v16M8 20l4-7 4 7"/>',
        "baby":
            '<circle cx="12" cy="13" r="7"/>'
            + '<path d="M9 5c0-2 1.5-3 3-3 1.2 0 2 .8 2 1.8 0 1.4-1.2 2.2-2.5 1.7M9.5 13h.01M14.5 13h.01M10 16c1.2 1 2.8 1 4 0"/>',
        "puzzle":
            '<path d="M4 4h6a2 2 0 1 0 4 0h6v6a2 2 0 1 1 0 4v6h-6a2 2 0 1 0-4 0H4v-6a2 2 0 1 0 0-4z"/>',
        "book":
            '<path d="M4 4h5a3 3 0 0 1 3 3v14a3 3 0 0 0-3-3H4zM20 4h-5a3 3 0 0 0-3 3v14a3 3 0 0 1 3-3h5z"/>',
        "pen-ruler":
            '<path d="m4 20 4.5-1 10-10-3.5-3.5-10 10zM13.5 6.5 17 10"/>'
            + '<path d="M15 20h6V8M18 17h3M18 13h3"/>',
        "paw":
            '<circle cx="7" cy="8" r="2"/>'
            + '<circle cx="17" cy="8" r="2"/>'
            + '<circle cx="4.5" cy="13" r="1.8"/>'
            + '<circle cx="19.5" cy="13" r="1.8"/>'
            + '<path d="M8 19c0-3 1.8-5 4-5s4 2 4 5c0 2-1.5 3-4 3s-4-1-4-3z"/>',

        "basket":
            '<path d="M3 10h18l-2 10H5zM8 10l4-7 4 7M7 14v3M12 14v3M17 14v3"/>',
        "cup":
            '<path d="M5 7h12v8a5 5 0 0 1-5 5H10a5 5 0 0 1-5-5z"/>'
            + '<path d="M17 9h1.5a3 3 0 0 1 0 6H17M8 3v2M12 3v2M16 3v2"/>',
        "car":
            '<path d="M5 17H3v-5l2-5h14l2 5v5h-2"/>'
            + '<path d="M5 17h14M6 12h12M7 17v2M17 17v2"/>'
            + '<circle cx="7" cy="15" r="1"/>'
            + '<circle cx="17" cy="15" r="1"/>',
        "motorcycle":
            '<circle cx="6" cy="17" r="4"/>'
            + '<circle cx="18" cy="17" r="4"/>'
            + '<path d="M6 17h5l3-6h3l1 6M10 11H7l-2 3M13 8h4l2 3"/>',
        "briefcase":
            '<rect x="3" y="7" width="18" height="13" rx="2"/>'
            + '<path d="M8 7V4h8v3M3 12h18M10 12v2h4v-2"/>',
        "printer":
            '<path d="M6 9V3h12v6M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/>'
            + '<rect x="6" y="14" width="12" height="7"/>'
            + '<path d="M18 12h.01"/>',

        "fallback":
            '<circle cx="12" cy="12" r="8"/>'
            + '<path d="M12 8v4M12 16h.01"/>'
    });

    function normalizeIconName(value) {
        return String(value ?? "")
            .trim()
            .toLowerCase()
            .replace(/^fa-/, "");
    }

    function resolveIconName(element) {
        const explicit = normalizeIconName(element.dataset.icon);
        if (explicit) {
            return explicit;
        }

        const classNames = [...element.classList]
            .filter(className =>
                className.startsWith("fa-")
                && !nonIconClasses.has(className));

        return normalizeIconName(classNames.at(-1));
    }

    function inferShape(iconName) {
        if (iconToShape[iconName]) {
            return iconToShape[iconName];
        }

        if (iconName.includes("arrow")) return "arrow-right";
        if (iconName.includes("chevron")) return "chevron-right";
        if (iconName.includes("truck")) return "truck";
        if (iconName.includes("box")) return "box";
        if (iconName.includes("cart")) return "cart";
        if (iconName.includes("bag")) return "bag";
        if (iconName.includes("file")) return "file";
        if (iconName.includes("image")) return "image";
        if (iconName.includes("comment")) return "comment";
        if (iconName.includes("user")) return "user";
        if (iconName.includes("shield")) return "shield";
        if (iconName.includes("calendar")) return "calendar";
        if (iconName.includes("clock") || iconName.includes("time")) return "clock";
        if (iconName.includes("money") || iconName.includes("coin")) return "coins";
        if (iconName.includes("search")) return "search";
        if (iconName.includes("check")) return "check";
        if (iconName.includes("close") || iconName.includes("times")) return "xmark";
        if (iconName.includes("warning") || iconName.includes("exclamation")) return "alert";
        if (iconName.includes("info")) return "info";
        if (iconName.includes("home") || iconName.includes("house")) return "house";
        if (iconName.includes("folder")) return "folder";
        if (iconName.includes("star")) return "star";

        return "fallback";
    }

    function createSvg(shape, iconName) {
        const svg = document.createElementNS(
            "http://www.w3.org/2000/svg",
            "svg");

        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("focusable", "false");
        svg.setAttribute("aria-hidden", "true");
        svg.classList.add("fastbuy-icon");
        svg.dataset.fastbuyIconShape = shape;
        svg.dataset.fastbuyIconName = iconName;
        svg.innerHTML = shapeMarkup[shape] ?? shapeMarkup.fallback;

        return svg;
    }

    function renderElement(element) {
        if (!(element instanceof HTMLElement)) {
            return;
        }

        const iconName = resolveIconName(element);
        if (!iconName) {
            return;
        }

        const existing = element.querySelector(
            ":scope > svg.fastbuy-icon");

        if (element.dataset.fastbuyIconReady === iconName && existing) {
            return;
        }

        const shape = inferShape(iconName);
        const svg = createSvg(shape, iconName);

        element.replaceChildren(svg);
        element.classList.add("fastbuy-icon-host");
        element.dataset.fastbuyIconReady = iconName;

        if (element.classList.contains("fa-spin")
            || element.classList.contains("fa-spin-pulse")
            || element.classList.contains("fa-pulse")) {
            element.classList.add("fastbuy-icon-host--spin");
        } else {
            element.classList.remove("fastbuy-icon-host--spin");
        }
    }

    const selector = [
        "i[class*='fa-']",
        "span[data-icon]",
        "i[data-icon]"
    ].join(",");

    function render(root = document) {
        if (root instanceof HTMLElement && root.matches(selector)) {
            renderElement(root);
        }

        root.querySelectorAll?.(selector).forEach(renderElement);
    }

    let observer;

    function start() {
        ensureStylesheet();
        render(document);

        observer = new MutationObserver(records => {
            for (const record of records) {
                if (record.type === "attributes") {
                    renderElement(record.target);
                    continue;
                }

                record.addedNodes.forEach(node => {
                    if (node instanceof HTMLElement) {
                        render(node);
                    }
                });
            }
        });

        observer.observe(document.documentElement, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ["class", "data-icon"]
        });
    }

    window.FastBuyIcons = Object.freeze({
        version,
        render,
        refresh: () => render(document)
    });

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            start,
            { once: true });
    } else {
        start();
    }
})();
