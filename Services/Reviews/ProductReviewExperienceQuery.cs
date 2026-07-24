using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Reviews;

public static class ProductReviewExperienceQuery
{
    public static async Task<ProductReviewExperiencePageSnapshot?> GetPageAsync(
        ApplicationDbContext context,
        int productId,
        int page,
        int pageSize,
        int? rating,
        bool mediaOnly,
        int? currentCustomerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 4, 20);
        rating = rating is >= 1 and <= 5 ? rating : null;

        var product = await context.Products
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

        // Transparency rule: every verified-purchase review is visible.
        // Historical Pending/Hidden/Rejected values are intentionally not used
        // as a storefront visibility filter.
        var baseQuery = context.Set<ProductReview>()
            .AsNoTracking()
            .Where(review =>
                review.ProductId == productId
                && review.IsVerifiedPurchase);

        var ratingRows = await baseQuery
            .GroupBy(review => review.Rating)
            .Select(group => new
            {
                Rating = (int)group.Key,
                Count = group.Count()
            })
            .ToArrayAsync(cancellationToken);

        var ratingCounts = Enumerable.Range(1, 5)
            .ToDictionary(
                value => value,
                value => ratingRows
                    .Where(row => row.Rating == value)
                    .Select(row => row.Count)
                    .SingleOrDefault());

        var totalCount = ratingCounts.Values.Sum();
        var averageRating = totalCount == 0
            ? 0d
            : ratingCounts.Sum(item => item.Key * item.Value)
                / (double)totalCount;

        var mediaReviewCount = await baseQuery
            .CountAsync(review => review.Media.Any(), cancellationToken);

        var filteredQuery = baseQuery;

        if (rating.HasValue)
        {
            var selectedRating = (byte)rating.Value;
            filteredQuery = filteredQuery.Where(
                review => review.Rating == selectedRating);
        }

        if (mediaOnly)
        {
            filteredQuery = filteredQuery.Where(
                review => review.Media.Any());
        }

        var filteredCount = await filteredQuery.CountAsync(cancellationToken);
        var totalPages = filteredCount == 0
            ? 0
            : (int)Math.Ceiling(filteredCount / (double)pageSize);

        if (totalPages > 0)
        {
            page = Math.Min(page, totalPages);
        }

        ProductReview[] reviews = filteredCount == 0
            ? []
            : await filteredQuery
                .AsSplitQuery()
                .Include(review => review.Customer)
                    .ThenInclude(customer => customer.Account)
                .Include(review => review.OrderItem)
                .Include(review => review.Media)
                .Include(review => review.Reply)
                .Include(review => review.Messages)
                .OrderByDescending(review => review.SubmittedAt)
                .ThenByDescending(review => review.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArrayAsync(cancellationToken);

        return new ProductReviewExperiencePageSnapshot(
            product.Id,
            product.Name,
            product.Slug,
            page,
            pageSize,
            totalPages,
            totalCount,
            filteredCount,
            mediaReviewCount,
            averageRating,
            ratingCounts,
            rating,
            mediaOnly,
            reviews.Select(review =>
                new ProductReviewExperienceItemSnapshot(
                    review.Id,
                    MaskReviewerName(review.Customer.Account.Username),
                    review.Rating,
                    review.Title,
                    review.Content,
                    review.OrderItem.VariantDescription,
                    review.IsVerifiedPurchase,
                    review.SubmittedAt,
                    review.EditedAt,
                    review.Media
                        .OrderBy(item => item.DisplayOrder)
                        .Select(item => new ProductReviewMediaSnapshot(
                            item.Type,
                            item.Url))
                        .ToArray(),
                    BuildThread(review),
                    currentCustomerId.HasValue
                        && review.CustomerId == currentCustomerId.Value))
                .ToArray());
    }

    public static IReadOnlyList<ProductReviewConversationMessageSnapshot>
        BuildThread(ProductReview review)
    {
        ArgumentNullException.ThrowIfNull(review);

        var result =
            new List<ProductReviewConversationMessageSnapshot>();

        if (review.Reply is not null)
        {
            result.Add(new ProductReviewConversationMessageSnapshot(
                -review.Reply.Id,
                ProductReviewMessageAuthorType.Admin,
                "FastBuy",
                review.Reply.Content,
                review.Reply.RepliedAt,
                IsLegacyReply: true));
        }

        result.AddRange(
            review.Messages
                .OrderBy(item => item.SentAt)
                .ThenBy(item => item.Id)
                .Select(item =>
                    new ProductReviewConversationMessageSnapshot(
                        item.Id,
                        item.AuthorType,
                        item.AuthorType == ProductReviewMessageAuthorType.Admin
                            ? "FastBuy"
                            : item.AuthorName,
                        item.Content,
                        item.SentAt)));

        return result
            .OrderBy(item => item.SentAt)
            .ThenBy(item => item.MessageId)
            .ToArray();
    }

    public static async Task PublishImmediatelyAsync(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        long reviewId,
        CancellationToken cancellationToken)
    {
        var review = await context.Set<ProductReview>()
            .SingleOrDefaultAsync(
                item => item.Id == reviewId,
                cancellationToken);

        if (review is null)
        {
            return;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        review.Status = ProductReviewStatus.Published;
        review.PublishedAt ??= nowUtc;
        review.ModeratedAt = null;
        review.ModeratedBy = null;
        review.ModerationNote = null;
        review.UpdatedAt = nowUtc;

        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task NormalizeCustomerVisibilityAsync(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        int customerId,
        int? orderItemId,
        CancellationToken cancellationToken)
    {
        var query = context.Set<ProductReview>()
            .Where(review =>
                review.CustomerId == customerId
                && review.Status != ProductReviewStatus.Published);

        if (orderItemId.HasValue)
        {
            query = query.Where(
                review => review.OrderItemId == orderItemId.Value);
        }

        var reviews = await query.ToArrayAsync(cancellationToken);
        if (reviews.Length == 0)
        {
            return;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var review in reviews)
        {
            review.Status = ProductReviewStatus.Published;
            review.PublishedAt ??= nowUtc;
            review.ModeratedAt = null;
            review.ModeratedBy = null;
            review.ModerationNote = null;
            review.UpdatedAt = nowUtc;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task<string?> GetOwnedReviewProductSlugAsync(
        ApplicationDbContext context,
        long reviewId,
        int customerId,
        CancellationToken cancellationToken)
    {
        return await context.Set<ProductReview>()
            .AsNoTracking()
            .Where(review =>
                review.Id == reviewId
                && review.CustomerId == customerId)
            .Select(review => review.Product.Slug)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public static string MaskReviewerName(string? username)
    {
        var normalized = string.IsNullOrWhiteSpace(username)
            ? "Khách hàng"
            : username.Trim();

        return normalized.Length switch
        {
            0 => "Khách hàng",
            1 => normalized + "***",
            2 => normalized + "***",
            _ => normalized[..2] + "***"
        };
    }
}

public static class ProductReviewConversationWorkflow
{
    public static async Task<string> AddCustomerMessageAsync(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        long reviewId,
        int customerId,
        string content,
        CancellationToken cancellationToken)
    {
        var review = await context.Set<ProductReview>()
            .AsSplitQuery()
            .Include(item => item.Product)
            .Include(item => item.Customer)
                .ThenInclude(customer => customer.Account)
            .Include(item => item.Messages)
            .SingleOrDefaultAsync(
                item => item.Id == reviewId
                    && item.CustomerId == customerId,
                cancellationToken)
            ?? throw new ProductReviewRuleException(
                "REVIEW_CONVERSATION_NOT_FOUND",
                "Không tìm thấy đánh giá thuộc tài khoản hiện tại.");

        var normalized = NormalizeMessage(content);
        EnsureThreadLimit(review.Messages.Count);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        review.Messages.Add(new ProductReviewMessage
        {
            AuthorType = ProductReviewMessageAuthorType.Customer,
            CustomerId = customerId,
            AuthorName = ProductReviewExperienceQuery.MaskReviewerName(
                review.Customer.Account.Username),
            Content = normalized,
            SentAt = nowUtc,
            CreatedAt = nowUtc
        });
        review.UpdatedAt = nowUtc;

        await context.SaveChangesAsync(cancellationToken);
        return review.Product.Slug;
    }

    public static async Task AddAdminMessageAsync(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        long reviewId,
        string actor,
        string content,
        CancellationToken cancellationToken)
    {
        var review = await context.Set<ProductReview>()
            .Include(item => item.Messages)
            .SingleOrDefaultAsync(
                item => item.Id == reviewId,
                cancellationToken)
            ?? throw new ProductReviewRuleException(
                "REVIEW_NOT_FOUND",
                "Không tìm thấy đánh giá.");

        var normalized = NormalizeMessage(content);
        EnsureThreadLimit(review.Messages.Count);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        review.Messages.Add(new ProductReviewMessage
        {
            AuthorType = ProductReviewMessageAuthorType.Admin,
            CustomerId = null,
            AuthorName = NormalizeActor(actor),
            Content = normalized,
            SentAt = nowUtc,
            CreatedAt = nowUtc
        });
        review.UpdatedAt = nowUtc;

        await context.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureThreadLimit(int messageCount)
    {
        if (messageCount >=
            ProductReviewTransparencyPolicy.MaximumConversationMessages)
        {
            throw new ProductReviewRuleException(
                "REVIEW_CONVERSATION_LIMIT_REACHED",
                "Cuộc trao đổi đã đạt giới hạn phản hồi. Vui lòng liên hệ hỗ trợ đơn hàng nếu cần xử lý thêm.");
        }
    }

    private static string NormalizeMessage(string? content)
    {
        var normalized = content?.Trim() ?? string.Empty;

        if (normalized.Length <
                ProductReviewTransparencyPolicy.MinimumMessageLength
            || normalized.Length >
                ProductReviewTransparencyPolicy.MaximumMessageLength)
        {
            throw new ProductReviewRuleException(
                "REVIEW_MESSAGE_LENGTH_INVALID",
                "Phản hồi cần từ 5 đến 1000 ký tự.");
        }

        if (normalized.Contains(
                "http://",
                StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(
                "https://",
                StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(
                "www.",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ProductReviewRuleException(
                "REVIEW_MESSAGE_EXTERNAL_LINK",
                "Phản hồi công khai không được chứa liên kết bên ngoài.");
        }

        return normalized;
    }

    private static string NormalizeActor(string? actor)
    {
        var normalized = string.IsNullOrWhiteSpace(actor)
            ? "Admin"
            : actor.Trim();

        return normalized.Length <= 100
            ? normalized
            : normalized[..100];
    }
}
