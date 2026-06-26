namespace WebApplication2.Models.Enums
{
    public enum PromotionType
    {
        Sitewide,          // Toàn sàn (Tự động áp dụng)
        PublicClaim,       // Công khai (Khách tự lưu/nhập mã)
        TargetedSegment,   // Theo phân khúc (VD: Chỉ dành cho VIP)
        TargetedSpecific   // Đích danh (Chọn từng khách hàng cụ thể)
    }
}