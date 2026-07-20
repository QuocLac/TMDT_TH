using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Returns;

namespace WebApplication2.Services.Reviews;

public sealed class ProductReviewService : IProductReviewService
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ProductReviewService(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<CustomerReviewableItemSnapshot>>
        GetReviewableItemsAsync(
            int customerId,
            CancellationToken cancellationToken)
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .AsSplitQuery()
            .Where(order =>
                order.CustomerId == customerId
                && order.OrderStatus == OrderStatus.Completed
                && order.FulfillmentStatus == FulfillmentStatus.Delivered)
            .Include(order => order.Items)
                .ThenInclude(item => item.Product)
            .Include(order => order.Items)
                .ThenInclude(item => item.Review)
                    .ThenInclude(review => review!.Reply)
            .Include(order => order.Shipments)
            .Include(order => order.CancellationRequests)
                .ThenInclude(request => request.Items)
            .Include(order => order.ReturnRequests)
                .ThenInclude(request => request.Items)
            .OrderByDescending(order => order.CompletedAt ?? order.CreatedAt)
            .Take(100)
            .ToArrayAsync(cancellationToken);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = new List<CustomerReviewableItemSnapshot>();

        foreach (var order in orders)
        {
            var deliveredAt = GetDeliveredAt(order);
            if (!deliveredAt.HasValue)
            {
                continue;
            }

            var deadline = ProductReviewPolicy.GetDeadlineUtc(deliveredAt.Value);

            foreach (var item in order.Items.OrderBy(item => item.Id))
            {
                var state = EvaluateLine(
                    order,
                    item,
                    deliveredAt.Value,
                    nowUtc);

                result.Add(new CustomerReviewableItemSnapshot(
                    item.Id,
                    order.Code,
                    item.ProductId,
                    item.ProductName,
                    item.Product.Slug,
                    item.Sku,
                    item.VariantDescription,
                    item.ImageUrl ?? "/images/no-image.png",
                    deliveredAt.Value,
                    deadline,
                    item.Review is not null,
                    item.Review?.Status,
                    item.Review is null && state.CanSubmit,
                    item.Review is not null
                        && state.CanSubmit
                        && CanEditReview(item.Review, nowUtc, deadline),
                    item.Review is null
                        ? state.Message
                        : BuildExistingReviewMessage(
                            item.Review,
                            state,
                            nowUtc,
                            deadline)));
            }
        }

        return result
            .OrderByDescending(item => item.DeliveredAt)
            .ThenBy(item => item.OrderItemId)
            .ToArray();
    }

    public async Task<ProductReviewEditorSnapshot?> GetEditorAsync(
        int customerId,
        int orderItemId,
        CancellationToken cancellationToken)
    {
        var item = await LoadOrderItemAggregateAsync(
            customerId,
            orderItemId,
            tracked: false,
            cancellationToken);

        if (item is null)
        {
            return null;
        }

        var deliveredAt = GetDeliveredAt(item.Order);
        if (!deliveredAt.HasValue)
        {
            return BuildEditor(
                item,
                DateTime.MinValue,
                DateTime.MinValue,
                canSubmit: false,
                "Chưa có xác nhận giao hàng thành công.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var deadline = ProductReviewPolicy.GetDeadlineUtc(deliveredAt.Value);
        var state = EvaluateLine(
            item.Order,
            item,
            deliveredAt.Value,
            nowUtc);

        var canSubmit = state.CanSubmit
            && (item.Review is null
                || CanEditReview(item.Review, nowUtc, deadline));

        var message = item.Review is null
            ? state.Message
            : BuildExistingReviewMessage(
                item.Review,
                state,
                nowUtc,
                deadline);

        return BuildEditor(
            item,
            deliveredAt.Value,
            deadline,
            canSubmit,
            message);
    }

    public async Task<long> SubmitAsync(
        int customerId,
        SubmitProductReviewCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rating = ValidateRating(command.Rating);
        var title = NormalizeOptional(
            command.Title,
            ProductReviewPolicy.MaximumTitleLength);
        var content = NormalizeContent(command.Content);
        var media = NormalizeMedia(command.Media);

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var item = await LoadOrderItemAggregateAsync(
                customerId,
                command.OrderItemId,
                tracked: true,
                cancellationToken)
                ?? throw new ProductReviewRuleException(
                    "REVIEW_ORDER_ITEM_NOT_FOUND",
                    "Không tìm thấy sản phẩm đã mua trong tài khoản hiện tại.");

            var deliveredAt = GetDeliveredAt(item.Order)
                ?? throw new ProductReviewRuleException(
                    "REVIEW_ORDER_NOT_DELIVERED",
                    "Đơn hàng chưa được đơn vị vận chuyển xác nhận giao thành công.");

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var deadline = ProductReviewPolicy.GetDeadlineUtc(deliveredAt);
            var state = EvaluateLine(
                item.Order,
                item,
                deliveredAt,
                nowUtc);

            if (!state.CanSubmit)
            {
                throw new ProductReviewRuleException(
                    state.ErrorCode ?? "REVIEW_NOT_ELIGIBLE",
                    state.Message);
            }

            var review = item.Review;

            if (review is null)
            {
                review = new ProductReview
                {
                    ProductId = item.ProductId,
                    OrderItemId = item.Id,
                    CustomerId = customerId,
                    SubmittedAt = nowUtc,
                    CreatedAt = nowUtc,
                    IsVerifiedPurchase = true
                };

                item.Review = review;
                _context.Set<ProductReview>().Add(review);
            }
            else
            {
                if (!CanEditReview(review, nowUtc, deadline))
                {
                    throw new ProductReviewRuleException(
                        "REVIEW_EDIT_NOT_ALLOWED",
                        review.Reply is not null
                            ? "Đánh giá đã có phản hồi từ hệ thống nên không thể chỉnh sửa."
                            : "Đánh giá không còn trong thời hạn chỉnh sửa.");
                }

                _context.Entry(review)
                    .Property(item => item.RowVersion)
                    .OriginalValue = DecodeRowVersion(command.RowVersion);
                review.EditedAt = nowUtc;
                review.UpdatedAt = nowUtc;
                review.PublishedAt = null;
                review.ModeratedAt = null;
                review.ModeratedBy = null;
                review.ModerationNote = null;

                _context.Set<ProductReviewMedia>()
                    .RemoveRange(review.Media);
                review.Media.Clear();
            }

            review.Rating = rating;
            review.Title = title;
            review.Content = content;
            review.Status = ProductReviewStatus.Pending;

            for (var index = 0; index < media.Count; index++)
            {
                review.Media.Add(new ProductReviewMedia
                {
                    Type = media[index].Type,
                    Url = media[index].Url,
                    DisplayOrder = index,
                    CreatedAt = nowUtc
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return review.Id;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<CustomerProductReviewSnapshot>>
        GetCustomerReviewsAsync(
            int customerId,
            CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var reviews = await _context.Set<ProductReview>()
            .AsNoTracking()
            .AsSplitQuery()
            .Where(review => review.CustomerId == customerId)
            .Include(review => review.Product)
            .Include(review => review.OrderItem)
                .ThenInclude(item => item.Order)
                    .ThenInclude(order => order.Shipments)
            .Include(review => review.Media)
            .Include(review => review.Reply)
            .OrderByDescending(review => review.SubmittedAt)
            .Take(250)
            .ToArrayAsync(cancellationToken);

        return reviews.Select(review =>
        {
            var deliveredAt = GetDeliveredAt(review.OrderItem.Order);
            var deadline = deliveredAt.HasValue
                ? ProductReviewPolicy.GetDeadlineUtc(deliveredAt.Value)
                : DateTime.MinValue;

            return new CustomerProductReviewSnapshot(
                review.Id,
                review.OrderItemId,
                review.ProductId,
                review.Product.Name,
                review.Product.Slug,
                review.OrderItem.Order.Code,
                review.OrderItem.Sku,
                review.OrderItem.VariantDescription,
                review.OrderItem.ImageUrl ?? "/images/no-image.png",
                review.Rating,
                review.Title,
                review.Content,
                review.Status,
                review.SubmittedAt,
                review.EditedAt,
                review.ModerationNote,
                review.Reply?.Content,
                review.Reply?.RepliedAt,
                deliveredAt.HasValue
                    && CanEditReview(review, nowUtc, deadline),
                review.Media
                    .OrderBy(item => item.DisplayOrder)
                    .Select(ToMediaSnapshot)
                    .ToArray());
        }).ToArray();
    }

    public async Task<ProductReviewSummarySnapshot> GetProductSummaryAsync(
        int productId,
        int take,
        CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 0, 50);

        var query = _context.Set<ProductReview>()
            .AsNoTracking()
            .Where(review =>
                review.ProductId == productId
                && review.Status == ProductReviewStatus.Published);

        var ratingCounts = await query
            .GroupBy(review => review.Rating)
            .Select(group => new
            {
                Rating = (int)group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(
                item => item.Rating,
                item => item.Count,
                cancellationToken);

        var totalCount = ratingCounts.Values.Sum();
        var average = totalCount == 0
            ? 0d
            : ratingCounts.Sum(item => item.Key * item.Value)
              / (double)totalCount;

        var items = take == 0
            ? []
            : await LoadPublicReviewsAsync(
                query,
                skip: 0,
                take,
                cancellationToken);

        return new ProductReviewSummarySnapshot(
            productId,
            totalCount,
            average,
            Enumerable.Range(1, 5)
                .ToDictionary(
                    rating => rating,
                    rating => ratingCounts.GetValueOrDefault(rating)),
            items);
    }

    public async Task<ProductReviewPageSnapshot?> GetProductPageAsync(
        int productId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);

        var product = await _context.Products
            .AsNoTracking()
            .Where(item => item.Id == productId && item.IsActive)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Slug
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        var summary = await GetProductSummaryAsync(
            productId,
            take: 0,
            cancellationToken);

        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(summary.TotalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var query = _context.Set<ProductReview>()
            .AsNoTracking()
            .Where(review =>
                review.ProductId == productId
                && review.Status == ProductReviewStatus.Published);

        var items = await LoadPublicReviewsAsync(
            query,
            (page - 1) * pageSize,
            pageSize,
            cancellationToken);

        return new ProductReviewPageSnapshot(
            product.Id,
            product.Name,
            product.Slug,
            page,
            totalPages,
            summary,
            items);
    }

    public async Task ModerateAsync(
        long reviewId,
        ReviewModerationAction action,
        byte[] rowVersion,
        string actor,
        string note,
        CancellationToken cancellationToken)
    {
        actor = NormalizeRequired(actor, 100, "Người xử lý");
        note = NormalizeOptional(note, 500) ?? string.Empty;

        if ((action is ReviewModerationAction.Hide
                or ReviewModerationAction.Reject)
            && string.IsNullOrWhiteSpace(note))
        {
            throw new ProductReviewRuleException(
                "REVIEW_MODERATION_NOTE_REQUIRED",
                "Ẩn hoặc từ chối đánh giá phải có ghi chú.");
        }

        var review = await _context.Set<ProductReview>()
            .SingleOrDefaultAsync(
                item => item.Id == reviewId,
                cancellationToken)
            ?? throw new ProductReviewRuleException(
                "REVIEW_NOT_FOUND",
                "Không tìm thấy đánh giá.");

        if (rowVersion is not { Length: > 0 })
        {
            throw new ProductReviewRuleException(
                "REVIEW_ROW_VERSION_REQUIRED",
                "Phiên bản dữ liệu đánh giá không hợp lệ.");
        }

        _context.Entry(review)
            .Property(item => item.RowVersion)
            .OriginalValue = rowVersion;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        review.Status = action switch
        {
            ReviewModerationAction.Publish => ProductReviewStatus.Published,
            ReviewModerationAction.Hide => ProductReviewStatus.Hidden,
            ReviewModerationAction.Reject => ProductReviewStatus.Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        review.PublishedAt = action == ReviewModerationAction.Publish
            ? review.PublishedAt ?? nowUtc
            : null;
        review.ModeratedAt = nowUtc;
        review.ModeratedBy = actor;
        review.ModerationNote = string.IsNullOrWhiteSpace(note)
            ? null
            : note;
        review.UpdatedAt = nowUtc;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertReplyAsync(
        long reviewId,
        string actor,
        string content,
        CancellationToken cancellationToken)
    {
        actor = NormalizeRequired(actor, 100, "Người phản hồi");
        content = NormalizeRequired(
            content,
            ProductReviewPolicy.MaximumReplyLength,
            "Nội dung phản hồi");

        var review = await _context.Set<ProductReview>()
            .Include(item => item.Reply)
            .SingleOrDefaultAsync(
                item => item.Id == reviewId,
                cancellationToken)
            ?? throw new ProductReviewRuleException(
                "REVIEW_NOT_FOUND",
                "Không tìm thấy đánh giá.");

        if (review.Status != ProductReviewStatus.Published)
        {
            throw new ProductReviewRuleException(
                "REVIEW_REPLY_REQUIRES_PUBLISHED",
                "Chỉ đánh giá đang hiển thị mới được phản hồi.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        if (review.Reply is null)
        {
            review.Reply = new ProductReviewReply
            {
                Content = content,
                RepliedBy = actor,
                RepliedAt = nowUtc,
                CreatedAt = nowUtc
            };
        }
        else
        {
            review.Reply.Content = content;
            review.Reply.RepliedBy = actor;
            review.Reply.RepliedAt = nowUtc;
            review.Reply.UpdatedAt = nowUtc;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<OrderItem?> LoadOrderItemAggregateAsync(
        int customerId,
        int orderItemId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        IQueryable<OrderItem> query = _context.OrderItems
            .AsSplitQuery()
            .Include(item => item.Product)
            .Include(item => item.Review)
                .ThenInclude(review => review!.Media)
            .Include(item => item.Review)
                .ThenInclude(review => review!.Reply)
            .Include(item => item.Order)
                .ThenInclude(order => order.Shipments)
            .Include(item => item.Order)
                .ThenInclude(order => order.CancellationRequests)
                    .ThenInclude(request => request.Items)
            .Include(item => item.Order)
                .ThenInclude(order => order.ReturnRequests)
                    .ThenInclude(request => request.Items);

        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(
            item =>
                item.Id == orderItemId
                && item.Order.CustomerId == customerId,
            cancellationToken);
    }

    private async Task<IReadOnlyList<PublicProductReviewSnapshot>>
        LoadPublicReviewsAsync(
            IQueryable<ProductReview> query,
            int skip,
            int take,
            CancellationToken cancellationToken)
    {
        var reviews = await query
            .AsSplitQuery()
            .Include(review => review.Customer)
                .ThenInclude(customer => customer.Account)
            .Include(review => review.OrderItem)
            .Include(review => review.Media)
            .Include(review => review.Reply)
            .OrderByDescending(review => review.SubmittedAt)
            .ThenByDescending(review => review.Id)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);

        return reviews.Select(review =>
            new PublicProductReviewSnapshot(
                review.Id,
                BuildReviewerName(review.Customer.Account.Username),
                review.Rating,
                review.Title,
                review.Content,
                review.OrderItem.VariantDescription,
                review.IsVerifiedPurchase,
                review.SubmittedAt,
                review.Media
                    .OrderBy(item => item.DisplayOrder)
                    .Select(ToMediaSnapshot)
                    .ToArray(),
                review.Reply?.Content,
                review.Reply?.RepliedBy,
                review.Reply?.RepliedAt))
            .ToArray();
    }

    private ProductReviewEditorSnapshot BuildEditor(
        OrderItem item,
        DateTime deliveredAt,
        DateTime deadline,
        bool canSubmit,
        string message)
    {
        return new ProductReviewEditorSnapshot(
            item.Id,
            item.Order.Code,
            item.ProductId,
            item.ProductName,
            item.Product.Slug,
            item.Sku,
            item.VariantDescription,
            item.ImageUrl ?? "/images/no-image.png",
            deliveredAt,
            deadline,
            item.Review?.Id,
            item.Review?.Rating ?? 5,
            item.Review?.Title,
            item.Review?.Content ?? string.Empty,
            item.Review?.Status,
            item.Review is null
                ? null
                : Convert.ToBase64String(item.Review.RowVersion),
            canSubmit,
            message,
            item.Review?.Media
                .OrderBy(media => media.DisplayOrder)
                .Select(ToMediaSnapshot)
                .ToArray()
                ?? []);
    }

    private LineEvaluation EvaluateLine(
        Order order,
        OrderItem item,
        DateTime deliveredAt,
        DateTime nowUtc)
    {
        if (order.OrderStatus != OrderStatus.Completed
            || order.FulfillmentStatus != FulfillmentStatus.Delivered)
        {
            return new LineEvaluation(
                false,
                "REVIEW_ORDER_NOT_COMPLETED",
                "Chỉ đơn đã hoàn tất và giao thành công mới được đánh giá.");
        }

        var cancelledQuantity = order.CancellationRequests
            .Where(request =>
                request.Status == OrderCancellationStatus.Approved)
            .SelectMany(request => request.Items)
            .Where(line => line.OrderItemId == item.Id)
            .Sum(line => line.ApprovedQuantity);

        if (item.Quantity - cancelledQuantity <= 0)
        {
            return new LineEvaluation(
                false,
                "REVIEW_ITEM_FULLY_CANCELLED",
                "Sản phẩm đã bị hủy toàn bộ nên không thể đánh giá.");
        }

        if (!ProductReviewPolicy.IsWithinWindow(deliveredAt, nowUtc))
        {
            return new LineEvaluation(
                false,
                "REVIEW_WINDOW_EXPIRED",
                $"Thời hạn đánh giá {ProductReviewPolicy.ReviewWindowDays} ngày đã kết thúc.");
        }

        var hasOpenReturn = order.ReturnRequests.Any(request =>
            ProductReviewPolicy.BlockingReturnStatuses.Contains(request.Status)
            && request.Items.Any(line => line.OrderItemId == item.Id));

        if (hasOpenReturn)
        {
            return new LineEvaluation(
                false,
                "REVIEW_BLOCKED_BY_OPEN_RETURN",
                "Sản phẩm đang trong quy trình hoàn trả. Có thể đánh giá sau khi yêu cầu hoàn trả kết thúc nếu vẫn còn thời hạn.");
        }

        return new LineEvaluation(
            true,
            null,
            $"Có thể gửi đánh giá đến hết {ProductReviewPolicy.GetDeadlineUtc(deliveredAt):dd/MM/yyyy}.");
    }

    private static bool CanEditReview(
        ProductReview review,
        DateTime nowUtc,
        DateTime deadline)
    {
        return ProductReviewPolicy.NormalizeUtc(nowUtc) <= deadline
            && review.Reply is null
            && review.Status != ProductReviewStatus.Hidden;
    }

    private static string BuildExistingReviewMessage(
        ProductReview review,
        LineEvaluation state,
        DateTime nowUtc,
        DateTime deadline)
    {
        if (!state.CanSubmit)
        {
            return state.Message;
        }

        if (review.Reply is not null)
        {
            return "Đánh giá đã có phản hồi nên không thể chỉnh sửa.";
        }

        if (ProductReviewPolicy.NormalizeUtc(nowUtc) > deadline)
        {
            return "Đánh giá đã hết thời hạn chỉnh sửa.";
        }

        return review.Status switch
        {
            ProductReviewStatus.Pending =>
                "Đánh giá đang chờ kiểm duyệt và vẫn có thể chỉnh sửa.",
            ProductReviewStatus.Published =>
                "Đánh giá đang hiển thị và vẫn có thể chỉnh sửa trong thời hạn.",
            ProductReviewStatus.Rejected =>
                "Đánh giá bị từ chối; có thể chỉnh sửa và gửi lại trong thời hạn.",
            ProductReviewStatus.Hidden =>
                "Đánh giá đã bị ẩn và không thể tự chỉnh sửa.",
            _ => "Đánh giá đã được ghi nhận."
        };
    }

    private static DateTime? GetDeliveredAt(Order order) =>
        ReturnPolicy.GetDeliveredOutboundShipment(order)?.DeliveredAt;

    private static ProductReviewMediaSnapshot ToMediaSnapshot(
        ProductReviewMedia media) =>
        new(media.Type, media.Url);

    private static byte ValidateRating(byte rating)
    {
        if (rating is < 1 or > 5)
        {
            throw new ProductReviewRuleException(
                "REVIEW_RATING_INVALID",
                "Số sao phải từ 1 đến 5.");
        }

        return rating;
    }

    private static string NormalizeContent(string value)
    {
        var content = NormalizeRequired(
            value,
            ProductReviewPolicy.MaximumContentLength,
            "Nội dung đánh giá");

        if (content.Length < ProductReviewPolicy.MinimumContentLength)
        {
            throw new ProductReviewRuleException(
                "REVIEW_CONTENT_TOO_SHORT",
                $"Nội dung đánh giá cần ít nhất {ProductReviewPolicy.MinimumContentLength} ký tự.");
        }

        if (ContainsExternalLink(content))
        {
            throw new ProductReviewRuleException(
                "REVIEW_CONTENT_EXTERNAL_LINK",
                "Nội dung đánh giá không được chứa liên kết bên ngoài.");
        }

        return content;
    }

    private static IReadOnlyList<ProductReviewMediaCommand> NormalizeMedia(
        IReadOnlyCollection<ProductReviewMediaCommand> media)
    {
        if (media is null || media.Count == 0)
        {
            return [];
        }

        if (media.Count > ProductReviewPolicy.MaximumMediaCount)
        {
            throw new ProductReviewRuleException(
                "REVIEW_MEDIA_LIMIT_EXCEEDED",
                $"Mỗi đánh giá được tối đa {ProductReviewPolicy.MaximumMediaCount} hình ảnh hoặc video.");
        }

        var normalized = new List<ProductReviewMediaCommand>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in media)
        {
            var url = item.Url?.Trim() ?? string.Empty;

            if (url.Length == 0 || url.Length > 500)
            {
                throw new ProductReviewRuleException(
                    "REVIEW_MEDIA_URL_INVALID",
                    "Đường dẫn hình ảnh hoặc video không hợp lệ.");
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ProductReviewRuleException(
                    "REVIEW_MEDIA_HTTPS_REQUIRED",
                    "Hình ảnh và video đánh giá phải dùng đường dẫn HTTPS.");
            }

            if (seen.Add(url))
            {
                normalized.Add(new ProductReviewMediaCommand(
                    item.Type,
                    url));
            }
        }

        return normalized;
    }

    private static byte[] DecodeRowVersion(
        string? encodedRowVersion)
    {
        if (string.IsNullOrWhiteSpace(encodedRowVersion))
        {
            throw new ProductReviewRuleException(
                "REVIEW_ROW_VERSION_REQUIRED",
                "Phiên bản dữ liệu đánh giá là bắt buộc.");
        }

        try
        {
            var rowVersion = Convert.FromBase64String(encodedRowVersion);
            if (rowVersion.Length == 0)
            {
                throw new FormatException();
            }

            return rowVersion;
        }
        catch (FormatException)
        {
            throw new ProductReviewRuleException(
                "REVIEW_ROW_VERSION_INVALID",
                "Phiên bản dữ liệu đánh giá không hợp lệ.");
        }
    }

    private static string BuildReviewerName(string username)
    {
        var normalized = string.IsNullOrWhiteSpace(username)
            ? "Khách hàng"
            : username.Trim();

        if (normalized.Length <= 2)
        {
            return normalized[0] + "***";
        }

        return normalized[..2] + "***";
    }

    private static bool ContainsExternalLink(string value)
    {
        return value.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || value.Contains("https://", StringComparison.OrdinalIgnoreCase)
            || value.Contains("www.", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRequired(
        string? value,
        int maxLength,
        string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;

        if (normalized.Length == 0)
        {
            throw new ProductReviewRuleException(
                "REVIEW_FIELD_REQUIRED",
                $"{fieldName} là bắt buộc.");
        }

        if (normalized.Length > maxLength)
        {
            throw new ProductReviewRuleException(
                "REVIEW_FIELD_TOO_LONG",
                $"{fieldName} vượt quá {maxLength} ký tự.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        int maxLength)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new ProductReviewRuleException(
                "REVIEW_FIELD_TOO_LONG",
                $"Nội dung vượt quá {maxLength} ký tự.");
        }

        return normalized;
    }

    private sealed record LineEvaluation(
        bool CanSubmit,
        string? ErrorCode,
        string Message);
}
