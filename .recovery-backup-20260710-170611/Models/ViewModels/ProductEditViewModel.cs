using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using WebApplication2.Models;

namespace WebApplication2.Areas.Admin.ViewModels
{
    public class ProductEditViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập đường dẫn SEO")]
        public string Slug { get; set; }

        public string? Description { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn danh mục")]
        public int CategoryId { get; set; }

        public int? BrandId { get; set; }

        public bool IsActive { get; set; }

        public string? MetaTitle { get; set; }
        public string? MetaDescription { get; set; }

        // --- HÌNH ẢNH HIỆN TẠI ĐANG CÓ TRÊN DATABASE ---
        public string? CurrentMainImage { get; set; }
        public List<ProductImage>? CurrentGalleryImages { get; set; }

        // --- UPLOAD THÊM/THAY THẾ ẢNH MỚI ---
        public IFormFile? MainImage { get; set; }
        public List<IFormFile>? GalleryImages { get; set; }

        public List<int>? SelectedPromotionIds { get; set; } = new List<int>();
    }
}