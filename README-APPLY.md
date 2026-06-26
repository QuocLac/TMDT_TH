# TMDT Auto SKU + Sample Catalog Data — 2026-07-10

## Mục tiêu

- Không cho người quản trị nhập hoặc sửa SKU thủ công.
- SKU của biến thể mới được cấp tự động từ cả `ProductId` và `ProductVariant.Id`.
- Giữ SKU hiện có của các biến thể cũ.
- Không thay đổi database schema và không cần migration.
- Cung cấp dữ liệu mẫu SQL; không tạo ảnh sản phẩm và `ProductVariant.ImageUrl = NULL`.

## Quy tắc SKU

Biến thể được chèn trước với mã tạm duy nhất trong transaction:

```text
TMP-<GUID>
```

Sau khi SQL Server cấp `Product.Id` và `ProductVariant.Id`, server đổi sang:

```text
SKU-P000001-V00000001
SKU-P000001-V00000002
SKU-P000002-V00000003
...
```

SKU được tạo trong backend. Frontend không gửi SKU và không có trường nhập SKU.

## File thay đổi

- `Services/Catalog/VariantSkuGenerator.cs`
- `Areas/Admin/Controllers/ProductsController.cs`
- `Areas/Admin/ViewModels/Products/ProductCreateViewModel.cs`
- `Areas/Admin/ViewModels/Products/ProductVariantRequests.cs`
- `Areas/Admin/Views/Products/Create.cshtml`
- `Areas/Admin/Views/Products/Edit.cshtml`
- `Areas/Admin/Views/Products/Index.cshtml`
- `wwwroot/js/pages/admin/products-editor.js`
- `wwwroot/js/pages/admin/products-index.js`
- `wwwroot/css/pages/admin/products-editor.css`

Bản vá cũng mang theo bản sửa validation tiền trước đó:

- `Areas/Admin/ViewModels/Validation/MoneyRangeAttribute.cs`
- `Areas/Admin/ViewModels/PriceCampaigns/SavePriceCampaignRequest.cs`

Điều này tránh ghi đè trở lại phiên bản có `Range(typeof(decimal), "0.01", ...)`.

## SQL dữ liệu mẫu

File:

```text
scripts/insert-sample-catalog-data.sql
```

Script:

- Có transaction và `XACT_ABORT`.
- Có thể chạy lại mà không nhân đôi sản phẩm/biến thể cùng thuộc tính.
- Tạo danh mục, thương hiệu, sản phẩm và biến thể.
- Không insert vào `dbo.ProductImage`.
- Đặt `ProductVariants.ImageUrl = NULL`.
- Cấp SKU theo cùng format của application.

## Cách áp dụng

1. Dừng ứng dụng hoặc IIS Express.
2. Backup/commit source hiện tại.
3. Giải nén ZIP vào thư mục chứa `WebApplication2.csproj`.
4. Cho phép ghi đè file.
5. Chạy:

```powershell
powershell -ExecutionPolicy Bypass -File .\Verify-TMDT-AutoSku.ps1
```

6. Khởi động:

```powershell
dotnet run
```

## Kiểm tra chức năng

- Tạo sản phẩm với nhiều biến thể: không còn ô SKU.
- Sau khi lưu, bảng biến thể hiển thị `SKU-P000001-V00000001` theo ProductId và VariantId thật.
- Thêm biến thể từ trang danh sách: không có ô SKU.
- Thêm biến thể từ trang chỉnh sửa: không có ô SKU.
- Chỉnh biến thể cũ: SKU chỉ hiển thị, không được sửa.
- Tìm kiếm theo SKU vẫn hoạt động.

## Chạy dữ liệu mẫu

Mở SQL Server Management Studio, chọn đúng database trong `DefaultConnection`,
kiểm tra lại tên database rồi chạy toàn bộ:

```text
scripts/insert-sample-catalog-data.sql
```

Không chạy script trên production hoặc database có dữ liệu quan trọng khi chưa backup.

## Rollback source

Khôi phục các file từ Git/backup trước khi giải nén. Bản vá không tự sửa database.

## Lưu ý dữ liệu cũ

Bản vá không đổi SKU hiện có. Chỉ biến thể được tạo mới sau khi áp dụng mới dùng
format tự động.


## Vì sao format v2 an toàn hơn

`ProductVariant.Id` vốn đã duy nhất toàn bảng, nhưng format v2 bổ sung `ProductId`
để SKU thể hiện rõ biến thể thuộc sản phẩm nào:

```text
SKU-P{ProductId:D6}-V{VariantId:D8}
```

Ví dụ hai sản phẩm khác nhau:

```text
SKU-P000001-V00000001
SKU-P000002-V00000002
```

Database vẫn giữ unique index trên cột SKU. Frontend không được nhập hoặc sửa SKU.
