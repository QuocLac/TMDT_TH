using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace WebApplication2.Areas.Admin.ViewModels
{
    public class ProductCreateViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập đường dẫn SEO")]
        public string Slug { get; set; }

        public string Description { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn danh mục")]
        public int CategoryId { get; set; }

        public bool IsActive { get; set; } = true;

        public string MetaTitle { get; set; }
        public string MetaDescription { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mã SKU")]
        public string SKU { get; set; }

        [Required]
        [Range(1, double.MaxValue, ErrorMessage = "Giá bán phải lớn hơn 0")]
        public decimal Price { get; set; }

        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "Tồn kho không được âm")]
        public int StockQuantity { get; set; }

        public string Color { get; set; }
        public string Size { get; set; }

        public List<int> SelectedPromotionIds { get; set; } = new List<int>();

        [Required(ErrorMessage = "Vui lòng chọn ảnh đại diện sản phẩm")]
        public IFormFile MainImage { get; set; }

        public List<IFormFile> GalleryImages { get; set; }

        public IFormFile VariantImage { get; set; }
    }
}