using System;
using System.Collections.Generic;

namespace WebApplication2.Areas.Admin.ViewModels
{
    // Class hứng dữ liệu Tổng quan của chiến dịch
    public class PriceCampaignDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // Danh sách các biến thể con được tích chọn
        public List<PriceCampaignItemDto> Items { get; set; }
    }

    // Class hứng dữ liệu từng dòng Biến thể
    public class PriceCampaignItemDto
    {
        public int VariantId { get; set; }
        public decimal NewPrice { get; set; }
    }
}