# TMDT Codex continuation patch — 2026-07-10


## Trạng thái


Ba file nền đã được cập nhật trực tiếp trên Google Drive:


- `Program.cs`
- `WebApplication2.csproj`
- `appsettings.json`


Phần source còn lại được đóng gói trong `TMDT_patch_payload.zip` ở cuộc trò chuyện ChatGPT này. Gói vá chứa 32 file backend, ViewModel, Razor, CSS, JavaScript và migration nhằm hoàn thiện phần Codex bị dừng giữa chừng.


## Cách áp dụng


1. Tải `TMDT_patch_payload.zip` từ cuộc trò chuyện.
2. Đặt ZIP vào thư mục gốc project, cùng cấp với `WebApplication2.csproj` và `Apply-TMDT-Patch.ps1`.
3. Mở PowerShell tại thư mục project.
4. Chạy:


```powershell
powershell -ExecutionPolicy Bypass -File .\Apply-TMDT-Patch.ps1
```


Script sẽ:


- Kiểm tra SHA-256 của ZIP.
- Sao lưu file hiện tại vào `.patch-backup-<timestamp>`.
- Ghi file mới đúng cấu trúc thư mục.
- Chuyển ViewModel sản phẩm sang `Areas/Admin/ViewModels/Products`.
- Không tự chạy `dotnet ef database update`.


## Kiểm tra bắt buộc sau khi áp dụng


```powershell
dotnet restore
dotnet build
dotnet ef migrations list
dotnet ef migrations script
```


Hãy review migration `20260710084500_CompletePriceCampaignConsistency` trước khi cập nhật database.


## Nội dung chính của bản vá


- Một nguồn giá hiệu lực qua `EffectivePriceService` và `ProductVariant.CurrentPrice`.
- Đồng bộ chiến dịch giá, worker, storefront và màn quản trị.
- Optimistic concurrency bằng `RowVersion` cho chiến dịch và biến thể.
- Upload/xóa ảnh qua `IProductImageStorage` với kiểm tra loại và kích thước file.
- Tách ViewModel theo vùng chức năng.
- Tách CSS/JavaScript theo `foundation`, `components`, `layouts`, `pages`, `themes`.
- Bổ sung theme sáng/tối, admin shell, HTTP helper chống CSRF và script riêng theo trang.


## Chưa tự động thực hiện


- Không cập nhật database.
- Không thay đổi authentication/authorization vì project chưa có scheme xác thực hoàn chỉnh.
- Không xóa `bin`, `obj`, `.vs`, `.git` khỏi Drive; nên dọn riêng sau khi kiểm tra source.