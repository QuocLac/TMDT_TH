document.addEventListener("click", (event) => {
    const button = event.target.closest("[data-product-image]");
    if (!button) {
        return;
    }

    const mainImage = document.getElementById("product-main-image");
    const nextImage = button.dataset.productImage;
    if (mainImage && nextImage) {
        mainImage.src = nextImage;
    }
});
