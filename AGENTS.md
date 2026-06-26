# AGENTS.md

## 1. Mục đích

File này là bộ quy tắc làm việc lâu dài dành cho Codex và các coding agent trong repository.

Đây là một project thương mại điện tử mới, phát triển theo hướng sàn bán nhiều loại sản phẩm khác nhau.

Hệ thống có thể mở rộng cho nhiều nhóm hàng như:

- Điện tử.
- Thời trang.
- Mỹ phẩm.
- Gia dụng.
- Sách.
- Thực phẩm đóng gói.
- Phụ kiện.
- Sản phẩm số.
- Các nhóm hàng khác trong tương lai.

Không được thiết kế hệ thống chỉ phù hợp với một loại sản phẩm cụ thể.

Mục tiêu của agent không chỉ là viết code chạy được, mà phải giúp project:

- Đúng nghiệp vụ.
- Có cấu trúc rõ ràng.
- Dễ bảo trì.
- Dễ kiểm thử.
- Bảo toàn dữ liệu.
- Dễ mở rộng trong thời gian dài.
- Hạn chế tạo nợ kỹ thuật.
- Giúp người phát triển hiểu được code và luồng dữ liệu.

---

## 2. Nguyên tắc quan trọng nhất

- Luôn đọc code hiện tại trước khi đưa ra giải pháp.
- Không giả định kiến trúc khi chưa kiểm tra repository.
- Không áp dụng máy móc kiến trúc từ project khác.
- Không thêm công nghệ hoặc design pattern chỉ vì chúng phổ biến.
- Không sửa rộng hơn phạm vi nhiệm vụ nếu chưa được yêu cầu.
- Không coi build thành công là bằng chứng chức năng đúng.
- Không che lỗi bằng hardcode, dữ liệu giả hoặc comment code.
- Không phá dữ liệu cũ để làm cho migration đơn giản hơn.
- Không để frontend quyết định nghiệp vụ quan trọng.
- Không để nhiều nơi cùng chứa một business rule.
- Mọi dữ liệu quan trọng phải có một nguồn sự thật rõ ràng.
- Tính đúng đắn và bảo toàn dữ liệu quan trọng hơn tốc độ triển khai.

---

## 3. Ngôn ngữ và cách giao tiếp

- Luôn giải thích kế hoạch, thay đổi, lỗi và kết quả bằng tiếng Việt.
- Tên class, method, variable, property, table, column và route dùng tiếng Anh.
- Không tự ý đổi tên các định danh đang tồn tại.
- Không dịch tên thư viện, lệnh terminal, exception hoặc log message.
- Comment trong code chỉ thêm khi thật sự cần thiết.
- Comment kỹ thuật trong code ưu tiên viết bằng tiếng Anh.
- Khi có thuật ngữ khó, giải thích ngắn gọn bằng tiếng Việt.
- Không báo cáo chung chung như “đã tối ưu” hoặc “đã sửa xong”.
- Phải mô tả rõ đã thay đổi gì và vì sao.

---

## 4. Cách agent bắt đầu một nhiệm vụ

Trước khi sửa code, agent phải:

1. Đọc file `AGENTS.md`.
2. Kiểm tra `git status`.
3. Xác định stack công nghệ thực tế từ repository.
4. Xác định entry point của chức năng.
5. Đọc các file liên quan.
6. Vẽ luồng dữ liệu hiện tại.
7. Kiểm tra database, migration và test liên quan.
8. Nêu kế hoạch trước khi thay đổi.
9. Xác định phạm vi được phép sửa.
10. Xác định rủi ro có thể phát sinh.

Các file cần kiểm tra tùy project có thể gồm:

- Entity hoặc Model.
- ViewModel hoặc DTO.
- Controller hoặc Endpoint.
- Service.
- Repository nếu project thực sự đang dùng.
- DbContext.
- Entity configuration.
- Migration.
- View hoặc frontend component.
- JavaScript hoặc TypeScript.
- CSS.
- Test.
- Configuration.
- Integration adapter.
- Background job.

Không sửa code khi chưa hiểu:

- Dữ liệu đến từ đâu.
- Dữ liệu được xử lý ở đâu.
- Dữ liệu được lưu ở đâu.
- Dữ liệu được trả về đâu.
- Business rule đang nằm ở đâu.

---

## 5. Làm việc trong project dài hạn

Project được phát triển trong thời gian dài. Vì vậy agent phải:

- Ưu tiên thay đổi nhỏ và có thể review.
- Không tạo solution tạm thời nếu biết sẽ gây lỗi về sau.
- Không tạo nhiều cách khác nhau để xử lý cùng một loại nghiệp vụ.
- Tôn trọng convention hiện có nếu convention đó hợp lý.
- Nếu convention hiện tại có vấn đề, phải nêu rõ trước khi thay đổi.
- Không refactor toàn bộ project trong một nhiệm vụ nhỏ.
- Không thay đổi public API hoặc database schema tùy tiện.
- Không xóa code chưa hiểu rõ.
- Không làm mất khả năng rollback.
- Mọi thay đổi lớn phải có kế hoạch migration và rollback.
- Mọi quyết định kiến trúc quan trọng phải được ghi lại.
- Không để kiến thức chỉ tồn tại trong cuộc trò chuyện với agent.
- Khi tạo rule mới có giá trị lâu dài, đề xuất bổ sung vào tài liệu.

---

## 6. Phạm vi thay đổi

Agent chỉ được sửa những gì cần thiết để hoàn thành nhiệm vụ.

Không được tự ý:

- Đổi kiến trúc toàn hệ thống.
- Thêm microservice.
- Thêm CQRS.
- Thêm Event Sourcing.
- Thêm MediatR.
- Thêm Repository Pattern.
- Thay ORM.
- Thay framework frontend.
- Thay hệ thống authentication.
- Đổi naming convention.
- Format toàn bộ repository.
- Di chuyển hàng loạt file.
- Đổi table hoặc column không liên quan.
- Sửa CSS global ngoài phạm vi.
- Thêm package mới nếu chưa giải thích nhu cầu.

Khi phát hiện vấn đề ngoài phạm vi:

- Không tự ý sửa.
- Ghi vào phần “Vấn đề phát hiện thêm”.
- Phân loại:
  - Critical.
  - High.
  - Medium.
  - Low.
- Chỉ sửa ngay nếu vấn đề gây mất dữ liệu, lỗi bảo mật nghiêm trọng hoặc chặn trực tiếp nhiệm vụ.

---

## 7. Kiến trúc

Agent phải làm theo kiến trúc thực tế của project.

Không được giả định project luôn dùng:

- MVC.
- Clean Architecture.
- Repository Pattern.
- API riêng.
- SPA.
- Microservice.

Sau khi khảo sát, agent phải mô tả luồng hiện tại.

Ví dụ với một project MVC:

`Browser -> Route -> Controller -> Service -> DbContext -> Database`

Khi trả giao diện:

`Database -> DbContext -> Service -> ViewModel -> Controller -> View -> HTML`

Quy tắc chung:

- UI chỉ thu thập và hiển thị dữ liệu.
- Business logic không được nằm trong View.
- Business logic quan trọng không được chỉ nằm trong JavaScript.
- Server là nơi xác minh cuối cùng.
- Không truy vấn database trực tiếp trong View.
- Không dùng Entity làm input model nếu có nguy cơ overposting.
- Dùng ViewModel hoặc DTO khi phù hợp.
- Không để Controller hoặc Endpoint chứa toàn bộ nghiệp vụ.
- Không tạo tầng mới nếu project chưa cần.

---

## 8. Quy tắc code chung

- Code phải dễ đọc hơn sau khi thay đổi.
- Method phải có một trách nhiệm rõ ràng.
- Không dùng tên biến mơ hồ.
- Không copy-paste business logic.
- Không để magic number hoặc magic string nếu có thể dùng constant hoặc enum.
- Không bắt exception rồi bỏ qua.
- Không sử dụng `catch { }`.
- Không log dữ liệu nhạy cảm.
- Không để dead code.
- Không thêm comment giải thích code khó thay vì làm code rõ hơn.
- Không tối ưu sớm nếu chưa có bằng chứng.
- Không làm giảm tính đúng đắn để lấy hiệu năng.

Nếu project dùng C#:

- Dùng `async/await` cho I/O.
- Không dùng `.Result` hoặc `.Wait()` trong request flow.
- Dùng `decimal` cho tiền.
- Không dùng `double` cho tiền.
- Không lạm dụng toán tử null-forgiving `!`.
- Tuân theo nullable reference types nếu project đã bật.
- Dùng dependency injection.
- Không phụ thuộc trực tiếp vào `DateTime.Now` cho logic thời gian cần test.
- Ưu tiên `TimeProvider` hoặc abstraction phù hợp.

---

## 9. Mô hình dữ liệu sản phẩm đa ngành hàng

Không được thiết kế Product chỉ phù hợp với một ngành hàng.

Cần phân biệt các khái niệm:

- Product: thông tin chung.
- ProductVariant: biến thể có thể bán.
- Category: nhóm sản phẩm.
- Brand: thương hiệu nếu có.
- Attribute: thuộc tính.
- AttributeValue: giá trị thuộc tính.
- SKU: mã bán hàng nội bộ.
- Barcode: mã quét nếu có.
- Media: hình ảnh hoặc video.
- Price: giá.
- Inventory: tồn kho.
- SupplierProduct: mapping với nhà cung cấp nếu có.

Ví dụ biến thể:

- Quần áo: màu, size.
- Điện thoại: màu, dung lượng.
- Mỹ phẩm: tone, dung tích.
- Gia dụng: màu, công suất.
- Thực phẩm: trọng lượng, quy cách đóng gói.

Quy tắc:

- Không hardcode thuộc tính riêng của một ngành hàng vào Product nếu có thể mô hình hóa linh hoạt hơn.
- SKU, giá và tồn kho thường thuộc ProductVariant.
- Không dùng tên sản phẩm hoặc tên biến thể làm khóa.
- Không cho phép hai biến thể trùng tổ hợp thuộc tính trong cùng Product.
- Không lưu dữ liệu cần query dưới dạng chuỗi tùy tiện.
- Không tạo schema quá tổng quát đến mức không còn constraint.
- Thiết kế phải cân bằng giữa tính linh hoạt và khả năng kiểm soát dữ liệu.

---

## 10. Giá và lịch sử giá

Mọi giá bán phải có nguồn sự thật rõ ràng.

Không được:

- Tin giá do frontend gửi lên.
- Ghi đè giá mà không lưu lịch sử nếu nghiệp vụ cần truy vết.
- Tính lại đơn hàng cũ bằng giá hiện tại.
- Để nhiều service cùng tự tính giá theo cách khác nhau.
- Cho phép khoảng giá chồng lấn mà không có rule ưu tiên.

Khi thiết kế giá, cần cân nhắc:

- ProductVariant.
- PriceType.
- Amount.
- Currency.
- ValidFrom.
- ValidTo.
- Status.
- Source.
- CreatedAt.
- CreatedBy.
- ApprovedAt.
- ApprovedBy.
- Reason.
- Concurrency token.

Các loại giá có thể gồm:

- Giá niêm yết.
- Giá bán.
- Giá khuyến mãi.
- Giá vốn.
- Giá theo kênh.
- Giá theo nhóm khách hàng.
- Giá nhà cung cấp.

Phải có một nơi duy nhất chịu trách nhiệm xác định giá hiệu lực.

---

## 11. Khuyến mãi

Khuyến mãi phải có:

- Thời gian bắt đầu.
- Thời gian kết thúc.
- Trạng thái.
- Phạm vi áp dụng.
- Điều kiện.
- Mức ưu tiên.
- Quy tắc cộng dồn.
- Hạn mức.
- Lịch sử thay đổi.

Không để frontend tự quyết định:

- Mức giảm.
- Mã hợp lệ.
- Tổng tiền sau giảm.
- Số lượng khuyến mãi còn lại.
- Thời gian hiệu lực.

Server phải kiểm tra lại toàn bộ điều kiện khi checkout.

Nếu có Flash Sale hoặc promotion giới hạn số lượng, phải xử lý:

- Concurrency.
- Overselling.
- Idempotency.
- Giới hạn khách hàng.
- Giữ hàng.
- Hết hạn giỏ hàng.
- Thanh toán trễ.
- Order snapshot.

---

## 12. Tồn kho và kho bãi

Không sửa tồn kho mà không có lịch sử.

Khi phù hợp, cần phân biệt:

- OnHand.
- Reserved.
- Available.
- Damaged.
- InTransit.
- PendingReceipt.

Mọi thay đổi tồn kho nên có stock movement:

- Nhập hàng.
- Xuất bán.
- Giữ hàng.
- Hủy giữ.
- Trả hàng.
- Điều chỉnh.
- Chuyển kho.
- Hàng hỏng.
- Kiểm kê.

Stock movement cần có:

- ProductVariant.
- Warehouse.
- Quantity.
- MovementType.
- ReferenceType.
- ReferenceId.
- Reason.
- CreatedAt.
- CreatedBy.
- Idempotency key nếu đến từ bên ngoài.

Không dùng check-then-update đơn giản cho tồn kho cạnh tranh mà không có transaction hoặc concurrency protection.

---

## 13. Đơn hàng

Đơn hàng phải lưu snapshot dữ liệu tại thời điểm mua.

Order item nên lưu tối thiểu:

- Product name.
- Variant name.
- SKU.
- Original unit price.
- Discount.
- Final unit price.
- Tax.
- Quantity.
- Line total.

Không phụ thuộc vào Product hiện tại để hiển thị lịch sử đơn hàng.

Trạng thái đơn hàng phải có transition hợp lệ.

Không được:

- Đổi trạng thái tùy ý.
- Xóa đơn đã phát sinh tài chính.
- Sửa số tiền gốc để làm khớp dữ liệu.
- Đánh dấu hoàn tất chỉ dựa vào dữ liệu frontend.

Mỗi thay đổi trạng thái quan trọng cần lưu:

- Trạng thái cũ.
- Trạng thái mới.
- Thời gian.
- Người hoặc hệ thống thực hiện.
- Lý do.
- Nguồn thay đổi.

---

## 14. Thanh toán

Tích hợp thanh toán phải dùng sandbox trước production.

Cần phân biệt:

- Order.
- Payment request hoặc intent.
- Payment attempt.
- Payment transaction.
- Refund.
- Settlement.
- Reconciliation.

Quy tắc:

- Không tin redirect từ trình duyệt.
- Phải xác minh callback hoặc webhook.
- Phải xác minh chữ ký nếu nhà cung cấp hỗ trợ.
- Webhook phải idempotent.
- Lưu external event id.
- Không xử lý cùng một event hai lần.
- Kiểm tra amount và currency.
- Tính tiền lại trên server.
- Không lưu số thẻ đầy đủ hoặc CVV.
- Không log secret.
- Không commit API key hoặc webhook secret.

---

## 15. Vận chuyển

Tích hợp shipping phải dùng sandbox nếu có.

Cần phân biệt:

- Shipping quote.
- Shipment.
- Package.
- Tracking event.
- Shipping fee.
- COD amount.
- Delivery status.
- Carrier settlement.

Quy tắc:

- Lưu external shipment id.
- Không tạo nhiều vận đơn do request lặp.
- Xử lý webhook lặp.
- Xử lý event đến sai thứ tự.
- Lưu lịch sử tracking.
- Có mapping trạng thái giữa hệ thống và carrier.
- Retry phải có giới hạn.
- Không retry vô hạn.
- COD phải được đối soát riêng.

---

## 16. Nhà cung cấp và bên thứ ba

Mọi integration phải có lớp adapter hoặc service riêng.

Không gọi trực tiếp API bên thứ ba từ View hoặc UI.

Mỗi integration cần:

- Sandbox configuration.
- Timeout.
- Retry có giới hạn.
- Idempotency.
- External id.
- Mapping dữ liệu.
- Mapping trạng thái.
- Logging có che dữ liệu nhạy cảm.
- Cơ chế chạy lại.
- Cơ chế xử lý lỗi.
- Cấu hình tách biệt theo environment.

Không:

- Gọi production API trong test.
- Commit secret.
- Gửi email hoặc SMS thật trong test nếu chưa được yêu cầu.
- Ghi đè dữ liệu nội bộ từ nguồn ngoài nếu chưa có rule ưu tiên.

---

## 17. Thuế và tài chính

Không hardcode một mức thuế ở nhiều nơi.

Thiết kế phải hỗ trợ thay đổi theo thời gian:

- Tax category.
- Tax rate.
- EffectiveFrom.
- EffectiveTo.
- Jurisdiction.
- Inclusive hoặc exclusive tax.
- Rounding rule.
- Tax snapshot.

Không được tuyên bố hệ thống “đúng luật” nếu chưa có:

- Nguồn pháp lý.
- Ngày hiệu lực.
- Xác nhận của người phụ trách.
- Test cho rule tương ứng.

Đơn hàng và hóa đơn phải lưu snapshot thuế.

Không tính lại thuế của giao dịch cũ bằng rule hiện tại.

---

## 18. Đối soát dòng tiền

Không chỉnh sửa giao dịch gốc để làm khớp số liệu.

Cần phân biệt:

- Gross amount.
- Discount.
- Shipping fee.
- Tax.
- Gateway fee.
- Refund.
- Chargeback.
- Adjustment.
- Net settlement.
- Actual received amount.

Bản ghi đối soát nên có:

- Provider.
- External transaction id.
- Internal payment id.
- Expected amount.
- Actual amount.
- Difference.
- Difference reason.
- Status.
- ReconciledAt.
- ReconciledBy.

Sai lệch phải được lưu dưới dạng discrepancy hoặc adjustment có lịch sử.

---

## 19. SEO và nội dung

SEO không chỉ là thêm meta tag.

Cần cân nhắc:

- Slug duy nhất.
- Canonical URL.
- Meta title.
- Meta description.
- Open Graph.
- Structured data.
- Sitemap.
- Robots directives.
- Alt text.
- Redirect khi đổi slug.
- Nội dung trùng lặp.
- Pagination.
- Sản phẩm hết hàng.
- Mobile responsive.
- Tốc độ tải trang.

Không:

- Dùng slug làm primary key.
- Dùng cùng title cho mọi trang.
- Xóa URL cũ mà không redirect.
- Hiển thị giá SEO khác giá server thực sự bán.
- Nhồi từ khóa.

---

## 20. Validation

Mọi dữ liệu từ người dùng hoặc hệ thống bên ngoài đều không đáng tin.

Phải kiểm tra:

- Required.
- Length.
- Format.
- Range.
- Enum.
- Foreign key existence.
- Ownership.
- Duplicate.
- Date range.
- Quantity.
- Price.
- File type.
- File size.
- Authorization.

Không chỉ dựa vào validation phía client.

Không cho client quyết định:

- Role.
- CustomerId của người khác.
- Price.
- Discount.
- Tax.
- Final total.
- Payment status.
- Order status.
- Inventory quantity.
- Approval status.

Nếu project là web form, phải dùng anti-forgery protection khi phù hợp.

---

## 21. Bảo mật

Mỗi chức năng phải kiểm tra:

- Authentication.
- Authorization.
- Role.
- Ownership.
- IDOR.
- CSRF.
- XSS.
- SQL injection.
- Overposting.
- File upload.
- Open redirect.
- Secret leakage.
- Sensitive logging.

Không:

- Nối chuỗi SQL với input người dùng.
- Trả exception chi tiết ra production.
- Commit secret.
- Log password, token hoặc dữ liệu thanh toán.
- Tin ID từ URL mà không kiểm tra quyền sở hữu.

---

## 22. Database và migration

Trước khi tạo migration, phải giải thích:

- Bảng nào thay đổi.
- Column nào thay đổi.
- Dữ liệu cũ bị ảnh hưởng thế nào.
- Có cần backfill không.
- Có nguy cơ mất dữ liệu không.
- Có cần index không.
- Có cần unique hoặc check constraint không.
- Rollback strategy là gì.

Không được:

- Sửa migration cũ đã được sử dụng.
- Tự chạy database update.
- Reset migration tùy tiện.
- Xóa database.
- Xóa column có dữ liệu mà chưa có kế hoạch.
- Thay đổi precision tiền gây mất dữ liệu.

Constraint cần cân nhắc:

- Primary key.
- Foreign key.
- Unique.
- Check.
- Required.
- Default.
- Decimal precision.
- Index.
- Concurrency token.

---

## 23. Truy vấn và hiệu năng

- Lọc trước khi load dữ liệu.
- Sort trước pagination.
- Không load toàn bộ bảng rồi mới filter.
- Tránh N+1 query.
- Tránh query trong loop.
- Chỉ lấy field cần thiết.
- Dùng no-tracking cho query chỉ đọc khi phù hợp.
- Không thêm Include dư thừa.
- Không thêm index khi chưa xác định query thực tế.
- Không cache dữ liệu quan trọng nếu chưa có invalidation strategy.
- Không đánh đổi tính đúng đắn để lấy tốc độ.

---

## 24. Concurrency, transaction và idempotency

Phải cân nhắc cho các luồng:

- Đổi giá.
- Trừ tồn kho.
- Giữ hàng.
- Tạo đơn.
- Thanh toán.
- Refund.
- Webhook.
- Tạo vận đơn.
- Đồng bộ bên thứ ba.
- Đối soát.

Công cụ có thể dùng tùy trường hợp:

- Database transaction.
- Isolation level.
- Optimistic concurrency.
- Row version.
- Unique constraint.
- Idempotency key.
- Retry có kiểm soát.

Hệ thống phải xử lý được:

- Request gửi lại.
- Callback đến trễ.
- Event lặp.
- Event sai thứ tự.
- Client timeout nhưng server đã xử lý thành công.

---

## 25. Testing

Mỗi thay đổi nghiệp vụ phải có test hoặc ghi rõ lý do chưa có test.

Ưu tiên test:

- Giá.
- Khuyến mãi.
- Kho.
- Đơn hàng.
- Thuế.
- Thanh toán.
- Refund.
- Shipping.
- Webhook lặp.
- Authorization.
- Ownership.
- Validation.
- Migration dữ liệu.
- Concurrency.
- Idempotency.

Với thời gian:

- Không phụ thuộc vào thời gian thật trong test.
- Dùng TimeProvider hoặc clock abstraction nếu phù hợp.
- Test trước, đúng và sau thời điểm hiệu lực.

Với tiền:

- Test rounding.
- Test discount.
- Test tax.
- Test refund một phần.
- Test currency nếu hệ thống hỗ trợ nhiều currency.

Không gọi production service trong test.

---

## 26. Frontend

Frontend có trách nhiệm:

- Hiển thị dữ liệu.
- Thu thập input.
- Hỗ trợ trải nghiệm người dùng.
- Gọi backend đúng contract.
- Hiển thị validation và lỗi.

Frontend không được là nơi duy nhất:

- Tính giá.
- Áp mã giảm giá.
- Kiểm tra tồn kho.
- Xác nhận thanh toán.
- Chuyển trạng thái đơn.
- Tính thuế.

Không lưu secret trong JavaScript hoặc source frontend.

Không tin hidden input cho dữ liệu tài chính hoặc quyền.

---

## 27. Git

Trước khi thay đổi lớn:

- Kiểm tra `git status`.
- Khuyến nghị tạo branch hoặc checkpoint.
- Không tự commit nếu chưa được yêu cầu.
- Không tự push.
- Không tự merge.
- Không rewrite history.
- Không commit secret.
- Không commit file build hoặc file tạm.

Diff phải:

- Nhỏ.
- Rõ mục đích.
- Không chứa format dư thừa.
- Không sửa file không liên quan.
- Có thể review độc lập.

---

## 28. Hỗ trợ người phát triển học code

Người phát triển dùng Codex vừa để xây project vừa để học.

Trước khi sửa, agent cần:

1. Giải thích luồng hiện tại.
2. Chỉ ra file liên quan.
3. Nêu kiến thức nền cần dùng.
4. Đưa kế hoạch.
5. Không viết toàn bộ ngay nếu người dùng muốn tự luyện.

Sau khi sửa, agent cần:

1. Giải thích từng file.
2. Giải thích dữ liệu đi từ frontend đến backend và database.
3. Chỉ ra business rule nằm ở đâu.
4. Chỉ ra phần người học có thể tự viết lại.
5. Giải thích cách debug.
6. Không chỉ nói “đã sửa xong”.

Khi review code:

- Phân loại lỗi:
  - Syntax.
  - Architecture.
  - Database.
  - Business logic.
  - Security.
  - Performance.
- Giải thích nguyên nhân.
- Đưa gợi ý trước khi đưa đáp án hoàn chỉnh nếu người dùng đang luyện.

---

## 29. Quy trình chuẩn cho mỗi nhiệm vụ

### Bước 1: Khảo sát

- Đọc code.
- Tìm entry point.
- Xác định luồng dữ liệu.
- Kiểm tra database.
- Kiểm tra migration.
- Kiểm tra test.
- Kiểm tra dependency.

### Bước 2: Phân tích

- Nêu vấn đề.
- Nêu nguyên nhân gốc.
- Nêu business rule.
- Nêu rủi ro.
- Nêu giải pháp tối thiểu.
- Nêu giải pháp dài hạn nếu khác.

### Bước 3: Triển khai

- Chỉ sửa đúng phạm vi.
- Không tạo logic trùng lặp.
- Bổ sung validation.
- Bổ sung constraint khi cần.
- Bổ sung test.
- Giữ backward compatibility nếu cần.

### Bước 4: Xác minh

- Build.
- Test.
- Review diff.
- Review query.
- Review migration.
- Kiểm tra quyền.
- Kiểm tra dữ liệu biên.
- Kiểm tra lỗi.
- Kiểm tra tác động đến module khác.

### Bước 5: Báo cáo

Báo cáo phải có:

- Tóm tắt.
- File đã thay đổi.
- Luồng dữ liệu.
- Business rule.
- Database thay đổi.
- Validation.
- Security.
- Lệnh đã chạy.
- Kết quả build/test.
- Rủi ro còn lại.
- Vấn đề phát hiện thêm.

---

## 30. Definition of Done

Một nhiệm vụ chỉ hoàn thành khi:

- Đúng yêu cầu.
- Đúng nghiệp vụ.
- Không phá chức năng cũ đã biết.
- Không tạo nguồn sự thật thứ hai.
- Có server-side validation.
- Có kiểm tra quyền.
- Có xử lý null và lỗi.
- Có transaction hoặc concurrency protection khi cần.
- Có idempotency khi cần.
- Không tin dữ liệu tài chính từ frontend.
- Không làm mất lịch sử quan trọng.
- Database có constraint phù hợp.
- Build thành công.
- Test thành công hoặc đã ghi rõ test còn thiếu.
- Diff không có file ngoài phạm vi.
- Có giải thích bằng tiếng Việt.
- Có danh sách rủi ro còn lại.

---

## 31. Lệnh kiểm tra mặc định

Agent phải kiểm tra stack thực tế trước khi chạy lệnh.

Với project .NET, các lệnh tham khảo:

```bash
dotnet restore
dotnet build
dotnet test
dotnet ef migrations list
git status
git diff
```

Không tự chạy:

```bash
dotnet ef database update
```

Không tự:

- Xóa database.
- Reset migration.
- Chạy seed phá hủy dữ liệu.
- Gọi production service.
- Thay đổi secret.

---

## 32. Mẫu báo cáo

```text
Tóm tắt:
- ...

File đã thay đổi:
- ...

Luồng dữ liệu:
- ...

Business rule:
- ...

Database:
- ...

Validation và bảo mật:
- ...

Lệnh đã chạy:
- ...

Kết quả:
- Build:
- Test:

Rủi ro còn lại:
- ...

Vấn đề phát hiện thêm:
- ...
```

---

## 33. Quy tắc cuối cùng

- Luôn khảo sát trước khi sửa.
- Không giả định dữ liệu.
- Không giả định nghiệp vụ.
- Không giả định kiến trúc.
- Không tạo giải pháp chỉ phù hợp với một ngành hàng.
- Không phá dữ liệu để sửa nhanh.
- Không để frontend quyết định nghiệp vụ.
- Không để trigger là nơi duy nhất chứa logic khó kiểm thử.
- Không thay đổi giao dịch tài chính mà không có audit.
- Không tin webhook hoặc dữ liệu bên thứ ba nếu chưa xác minh.
- Request lặp không được tạo nhiều giao dịch.
- Mọi thay đổi lớn phải có:
  - Implementation plan.
  - Migration plan.
  - Test plan.
  - Rollback plan.
