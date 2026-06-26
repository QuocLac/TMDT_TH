namespace WebApplication2.ViewModels.Shared;

public sealed class ErrorPageViewModel
{
    public int StatusCode { get; init; } = StatusCodes.Status500InternalServerError;

    public string Title { get; init; } = "Đã xảy ra lỗi";

    public string Message { get; init; } = "Hệ thống chưa thể xử lý yêu cầu. Vui lòng thử lại sau.";

    public string? RequestId { get; init; }
}
