# Static verification — 2026-07-10

Đã thực hiện trong môi trường phục hồi:

- Parse toàn bộ file C# trong payload bằng Tree-sitter C#: không có node lỗi cú pháp.
- Chạy `node --check` cho toàn bộ JavaScript trong payload: đạt.
- Parse toàn bộ CSS mới bằng `tinycss2`: đạt.
- Kiểm tra không có `DateTime.Now`/`DateTime.UtcNow` trong phần code phục hồi.
- Kiểm tra controller không trả `exception.Message` cho client.
- Kiểm tra Razor không chứa `<style>` hoặc business logic tính giá.
- Đối chiếu migration, designer và model snapshot cho `RowVersion`, constraint và index mới.

Giới hạn:

Môi trường phục hồi không có .NET 9 SDK nên chưa thể chạy compiler Razor, `dotnet build`, `dotnet test` hoặc sinh SQL bằng EF CLI. Phải chạy `Verify-TMDT-Recovery.ps1` trên máy phát triển trước khi cập nhật database.
