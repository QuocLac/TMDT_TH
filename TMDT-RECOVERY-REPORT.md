# Báo cáo phục hồi project TMDT

**Ngày thực hiện:** 10/07/2026  
**Nguồn khảo sát:** thư mục Google Drive TMDT và manifest của bản vá Codex bị dừng  
**Phạm vi triển khai:** hoàn thiện chức năng sản phẩm đa biến thể, chiến dịch giá, storefront giá hiệu lực, tổ chức ViewModel và tài nguyên UI

## 1. Kết luận trạng thái ban đầu

Project không ở một revision đồng nhất. Ba nhóm file cùng tồn tại:

1. **Phần mới đã có:** `Program.cs`, `ProductVariant.CurrentPrice`, `RowVersion`, `EffectivePriceService`, `PriceCampaignWorker`, image storage abstraction và admin layout mới.
2. **Phần cũ chưa thay:** admin `ProductsController`, ViewModel sản phẩm cũ, một số Razor cũ và migration snapshot cũ.
3. **Phần bị thiếu hoàn toàn:** ViewModel page cho chiến dịch, ViewModel sản phẩm theo area, CSS/JS theo trang, `http.js`, `theme.js`, `admin-shell.js`, `home.js` và migration consistency.

README cũ nói 32 file còn nằm trong `TMDT_patch_payload.zip`, nhưng ZIP không còn trong Drive/File Library. Chỉ còn installer và manifest SHA-256. Vì vậy bản phục hồi được dựng lại từ source hiện có, contract controller, entity, service, View/Razor và manifest; không đoán bằng một project khác.

## 2. Nguyên nhân gốc đã phát hiện

### Critical / High

- Storefront và service giá dùng hai quy tắc khác nhau. Storefront tự chọn `MIN(NewPrice)`, trong khi `EffectivePriceService` chọn chiến dịch theo `StartDate`/`CampaignId`.
- Admin sản phẩm ghi trực tiếp cả `Price` và `CurrentPrice`, có thể phá giá campaign đang chạy.
- View/JavaScript chiến dịch không round-trip `RowVersion`, nên concurrency mới chỉ tồn tại ở backend.
- `DbContext` có `RowVersion` và constraint mới nhưng migration/snapshot chưa đồng bộ.
- `Program.cs` và admin layout tham chiếu `http.js`, `theme.js`, `admin-shell.js`, nhưng các thư mục tương ứng đang trống.
- Khu vực Admin chưa có authentication scheme và authorization policy hoàn chỉnh.

### Medium

- Upload ảnh nằm trực tiếp trong controller cũ, không kiểm tra chữ ký/nội dung file và không dọn file cũ.
- Exception chi tiết có nguy cơ bị trả về client ở controller cũ.
- ViewModel sản phẩm nằm sai vùng, DTO được khai báo lẫn trong controller.
- Checkbox biến thể động có thể bind sai sau khi xóa/reindex dòng.
- CSS trạng thái dùng màu cố định, không tương thích đầy đủ dark theme.
- `PriceHistory` đang phải ghi cả thay đổi giá niêm yết và giá hiệu lực nhưng chưa có trường phân loại.

## 3. Thay đổi trong bản phục hồi

### Backend và dữ liệu

- Storefront chỉ đọc `ProductVariant.CurrentPrice` làm giá bán hiệu lực.
- Campaign chỉ cung cấp metadata như thời gian kết thúc, không trở thành nguồn giá thứ hai ở UI.
- Admin sản phẩm dùng `IEffectivePriceService` sau khi đổi giá niêm yết.
- Chặn giá niêm yết mới nếu không còn lớn hơn giá của campaign đang còn hiệu lực/lên lịch.
- Dùng transaction cho đổi giá, tạo/sửa campaign và chỉnh biến thể.
- Dùng `RowVersion` cho:
  - Đổi giá biến thể.
  - Bật/tắt biến thể.
  - Chỉnh biến thể nhanh.
  - Chỉnh campaign.
  - “Áp dụng ngay” campaign.
- Không trả exception nội bộ ra client.
- Upload/xóa ảnh qua `IProductImageStorage`.
- File mới được dọn khi database thất bại; file cũ chỉ xóa sau commit.
- Danh sách sản phẩm có pagination và projection typed ViewModel.
- Form tạo sản phẩm hỗ trợ nhiều biến thể ngay từ đầu.
- Slug/SKU/reference category-brand-promotion được xác minh server-side.

### Migration

`20260710084500_CompletePriceCampaignConsistency` thực hiện:

- Backfill `CurrentPrice` đang bằng/nhỏ hơn 0 từ `Price`.
- Dừng an toàn nếu có `PriceCampaignItems.NewPrice <= 0`.
- Chuẩn hóa nhiều ảnh chính trước khi thêm filtered unique index.
- Thêm `RowVersion` cho `PriceCampaigns` và `ProductVariants`.
- Thêm check constraint cho `CurrentPrice > 0` và `NewPrice > 0`.
- Đổi index:
  - `ProductImage(ProductId, IsMain)` unique khi `IsMain = 1`.
  - `ProductVariants(ProductId, IsActive)`.
  - `PriceHistories(ProductVariantId, CreatedAt)`.
- Cập nhật designer và model snapshot tương ứng.

Migration **không được tự động áp dụng**.

### ViewModel

- `Areas/Admin/ViewModels/Products/...`
- `Areas/Admin/ViewModels/PriceCampaigns/...`
- `ViewModels/Storefront/Products/...`

Entity không còn được dùng làm input trực tiếp cho các form sản phẩm/giá mới.

### UI và tài nguyên frontend

- CSS theo feature:
  - `wwwroot/css/pages/admin/products-index.css`
  - `wwwroot/css/pages/admin/products-editor.css`
  - `wwwroot/css/pages/admin/price-campaigns.css`
- JavaScript dùng chung:
  - `wwwroot/js/core/http.js`
  - `wwwroot/js/layouts/theme.js`
  - `wwwroot/js/layouts/admin-shell.js`
- JavaScript theo trang:
  - `products-index.js`
  - `products-editor.js`
  - `price-campaigns.js`
  - `home.js`
- Không thêm inline `<style>` hoặc script nghiệp vụ vào Razor.
- CSS trạng thái dùng design token để hoạt động trên light/dark theme.
- Sidebar responsive, overlay, Escape-to-close và focus restoration.
- Theme được áp dụng trước khi render để giảm nháy sáng/tối.
- `http.js` gom CSRF header, credentials, JSON/form request và lỗi HTTP.

## 4. Luồng dữ liệu sau phục hồi

### Hiển thị storefront

`SQL Server -> EF Core projection -> ProductVariant.CurrentPrice -> Storefront ViewModel -> Razor`

Storefront không tính lại giá campaign.

### Đổi giá niêm yết

`Admin UI -> UpdateVariantPriceRequest + RowVersion -> ProductsController -> validate campaign conflict -> transaction -> update Price -> EffectivePriceService -> update CurrentPrice/PriceHistory -> commit`

### Lưu chiến dịch

`Campaign editor -> SavePriceCampaignRequest + RowVersion -> PriceCampaignsController -> ValidateCampaignAsync -> transaction -> PriceCampaign/Items -> RecalculateVariantsAsync -> commit`

### Worker

`PriceCampaignWorker -> tìm campaign hết hạn -> IsActive=false -> EffectivePriceService -> khôi phục/áp dụng CurrentPrice -> PriceHistory`

## 5. Kiểm tra đã thực hiện

Trong môi trường hiện tại:

- 14 file C# trong payload được parse bằng Tree-sitter C#: **không có lỗi cú pháp**.
- Tất cả file JavaScript chạy `node --check`: **đạt**.
- Tất cả CSS mới được parse bằng `tinycss2`: **đạt**.
- Không phát hiện `DateTime.Now` hoặc `DateTime.UtcNow` trong phần phục hồi.
- Không phát hiện trả `exception.Message` cho client.
- Không phát hiện inline style/script nghiệp vụ trong Razor mới.
- Migration/designer/snapshot đã được đối chiếu tĩnh cho RowVersion, constraint và index.

### Chưa thể thực hiện trong môi trường hiện tại

Máy thực thi hiện không có .NET 9 SDK, vì vậy chưa chạy được:

- `dotnet restore`
- `dotnet build`
- Razor compiler
- `dotnet test`
- `dotnet ef migrations list`
- `dotnet ef migrations script`

Gói kèm `Verify-TMDT-Recovery.ps1` để chạy các bước này trên máy phát triển.

## 6. Cách áp dụng an toàn

1. Tạo branch/checkpoint Git.
2. Đặt ZIP và `Apply-TMDT-Recovery.ps1` cùng cấp `WebApplication2.csproj`.
3. Chạy installer. Installer:
   - Kiểm tra SHA-256 của ZIP.
   - Kiểm tra prerequisite của phần patch đã có.
   - Kiểm tra SHA-256 từng file payload.
   - Backup file cũ vào `.recovery-backup-<timestamp>`.
   - Copy payload.
   - Backup và xóa hai ViewModel legacy.
   - Tự rollback file nếu copy thất bại.
4. Chạy `Verify-TMDT-Recovery.ps1`.
5. Review SQL migration được sinh ra.
6. Sao lưu database.
7. Chỉ sau đó mới chủ động chạy `dotnet ef database update`.

## 7. Rollback

### Source

Khôi phục từ `.recovery-backup-<timestamp>`. Installer không commit, push hay merge Git.

### Database

Không rollback migration tùy tiện nếu đã có dữ liệu phát sinh sau migration. Trước tiên phải xác định:

- Có cập nhật campaign/variant sau migration không.
- Có logic nào phụ thuộc RowVersion không.
- Có ảnh chính nào được chuẩn hóa không.

Nên dùng bản sao database để thử migration Up/Down trước.

## 8. Rủi ro còn lại

### Critical

- **Authentication/authorization Admin chưa hoàn chỉnh.** Không đưa hệ thống ra internet trước khi có scheme đăng nhập, role/policy, ownership và kiểm thử IDOR.

### High

- Tồn kho vẫn là `StockQuantity` chỉnh trực tiếp; chưa có `OnHand/Reserved/Available`, stock movement hoặc chống overselling.
- Chưa có test project; logic giá/concurrency mới cần integration test với SQL Server.

### Medium

- `PriceHistory` chưa có `PriceType`/`SourceType`; lịch sử giá niêm yết và giá hiệu lực đang phân biệt bằng `Note`.
- Product thông tin chung chưa có RowVersion.
- Chưa có redirect history khi đổi slug.
- SEO mới dừng ở slug/meta; chưa có canonical, structured data, sitemap và Open Graph đầy đủ.

## 9. Kế hoạch module mới sau khi xác minh bản phục hồi

### Phase 1 — Inventory Foundation (nên làm tiếp theo)

- Warehouse.
- InventoryBalance: OnHand, Reserved, Available, Damaged, InTransit.
- StockMovement bất biến.
- Reservation có thời hạn.
- Idempotency key.
- Optimistic concurrency/transaction.
- Luồng nhập, giữ, giải phóng, bán, hoàn và điều chỉnh.
- Test đồng thời để chống overselling.

### Phase 2 — Cart và Order Snapshot

- Cart server-side.
- Reprice khi checkout.
- Order state machine.
- Snapshot tên sản phẩm, SKU, giá gốc, discount, tax và line total.
- Reservation gắn với order draft/pending payment.

### Phase 3 — Payment Sandbox

- Payment intent/attempt/transaction/refund.
- Webhook signature, idempotency và event ordering.
- Không tin redirect browser là bằng chứng thanh toán.

### Phase 4 — Shipping Sandbox

- Quote, shipment, package, tracking event.
- Mapping trạng thái và webhook lặp.
- COD settlement riêng.

### Phase 5 — Reconciliation và rủi ro dòng tiền

- Expected/actual settlement.
- Gateway fee, refund, chargeback, adjustment và discrepancy.
- Audit bất biến.

### Phase 6 — SEO và merchandising

- Canonical/redirect khi đổi slug.
- JSON-LD đúng giá server.
- Sitemap/robots/Open Graph.
- Landing page/theme sự kiện dựa trên token, không fork toàn bộ CSS.

## 10. Phạm vi không thực hiện

- Không chạy database update.
- Không thay authentication.
- Không thêm Repository Pattern, CQRS, MediatR hoặc microservice.
- Không xóa `bin`, `obj`, `.vs`, `.git` trên Drive.
- Không sửa module khách hàng/khuyến mãi legacy ngoài những liên kết cần bảo toàn.
