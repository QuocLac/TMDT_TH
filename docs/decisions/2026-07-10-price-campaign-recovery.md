# Phục hồi module sản phẩm và chiến dịch giá — 2026-07-10

## Bối cảnh

Repository ở trạng thái bản vá dở dang: một số entity, service và controller đã chuyển sang mô hình giá mới, trong khi ViewModel, Razor, JavaScript, CSS và migration tương ứng chưa được áp dụng đầy đủ.

Phạm vi quyết định này chỉ hoàn thiện các chức năng đã bắt đầu:

- Sản phẩm đa biến thể.
- Giá niêm yết và giá bán hiệu lực.
- Chiến dịch giá theo thời gian.
- Lịch sử giá.
- Upload và dọn ảnh sản phẩm.
- Storefront đọc cùng một nguồn giá.
- Tổ chức ViewModel và tài nguyên giao diện theo vùng chức năng.

Không triển khai authentication, kho chuẩn, đơn hàng, thanh toán hoặc shipping trong bản vá này.

## Quyết định nghiệp vụ

### Một nguồn giá bán hiệu lực

- `ProductVariant.Price`: giá niêm yết.
- `ProductVariant.CurrentPrice`: giá bán hiệu lực duy nhất mà storefront được phép hiển thị.
- `EffectivePriceService`: nơi duy nhất tính lại `CurrentPrice` từ giá niêm yết và chiến dịch đang hiệu lực.
- Razor và JavaScript không tự chọn chiến dịch hay tự tính giá bán cuối cùng.

Khi dữ liệu cũ có chiến dịch chồng lấn, service chọn theo quy tắc xác định:

1. `StartDate` mới hơn thắng.
2. Nếu bằng nhau, `CampaignId` lớn hơn thắng.

Luồng tạo/sửa mới vẫn từ chối chồng lấn để không tiếp tục sinh dữ liệu mơ hồ.

### Concurrency

- `ProductVariant.RowVersion` bảo vệ đổi giá, trạng thái và chỉnh biến thể.
- `PriceCampaign.RowVersion` bảo vệ chỉnh sửa và thao tác “áp dụng ngay”.
- Client phải gửi lại token; server không tin token giá hoặc trạng thái từ hidden input ngoài mục đích concurrency.

### Transaction

Các luồng đổi giá và chiến dịch chạy trong database transaction. Giá hiệu lực và lịch sử giá được lưu cùng transaction với thay đổi nguồn.

### Ảnh

- File được kiểm tra qua `IProductImageStorage`.
- File mới bị xóa nếu database từ chối thao tác.
- File cũ chỉ bị xóa sau khi transaction database đã commit.
- Database chỉ cho phép tối đa một ảnh chính trên mỗi sản phẩm bằng filtered unique index.

## Cấu trúc giao diện

- ViewModel quản trị sản phẩm: `Areas/Admin/ViewModels/Products`.
- ViewModel quản trị chiến dịch: `Areas/Admin/ViewModels/PriceCampaigns`.
- ViewModel storefront: `ViewModels/Storefront/...`.
- CSS theo trang: `wwwroot/css/pages/{area}/{feature}.css`.
- JavaScript dùng chung: `wwwroot/js/core` và `wwwroot/js/layouts`.
- JavaScript theo trang: `wwwroot/js/pages/{area}/{feature}.js`.
- Theme sử dụng design token, không hardcode bảng màu sáng trong CSS theo trang.

## Migration

Migration `20260710084500_CompletePriceCampaignConsistency`:

- Backfill `CurrentPrice <= 0` từ `Price`.
- Dừng an toàn nếu tồn tại `PriceCampaignItems.NewPrice <= 0`.
- Chuẩn hóa dữ liệu có nhiều ảnh chính, giữ ảnh có `Id` nhỏ nhất làm ảnh chính.
- Thêm `RowVersion` cho chiến dịch và biến thể.
- Thêm check constraint cho `CurrentPrice` và `NewPrice`.
- Thêm/cập nhật index phục vụ ảnh chính, truy vấn biến thể và lịch sử giá.

Không tự chạy `dotnet ef database update`.

## Rollback

Installer tạo `.recovery-backup-<timestamp>` trước khi ghi file. Nếu copy thất bại, installer tự phục hồi file cũ. Rollback database chỉ được thực hiện sau khi đánh giá dữ liệu đã phát sinh từ lúc migration được áp dụng.

## Rủi ro còn lại

- Khu vực Admin chưa có authentication/authorization hoàn chỉnh.
- `StockQuantity` vẫn là số tồn đơn giản, chưa có reservation và stock movement.
- `PriceHistory` chưa phân loại rõ thay đổi giá niêm yết và giá hiệu lực.
- Product chưa có concurrency token riêng cho phần thông tin chung.
- Repository chưa có test project tự động.

## Module tiếp theo

Ưu tiên module **Inventory Foundation** trước giỏ hàng và đơn hàng:

1. Warehouse.
2. Inventory balance: OnHand, Reserved, Available, Damaged, InTransit.
3. Stock movement bất biến có reason/reference/idempotency key.
4. Reservation có thời hạn và optimistic concurrency.
5. API/service giữ hàng, giải phóng, xuất bán và hoàn kho.
6. Test concurrency chống overselling.

Sau đó mới triển khai Cart/Order Snapshot, Payment Sandbox, Shipping và Reconciliation.
