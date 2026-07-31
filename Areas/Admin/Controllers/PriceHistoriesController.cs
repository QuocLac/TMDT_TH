using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.PriceHistories;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Pricing;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class PriceHistoriesController : Controller
{
    private const int PageSize = 40;
    private const int MaximumRangeDays = 366;
    private const int MaximumChartEvents = 600;
    private const decimal ChartWidth = 1000m;
    private const decimal ChartTop = 18m;
    private const decimal ChartBottom = 250m;

    private static readonly TimeZoneInfo BusinessTimeZone =
        ResolveBusinessTimeZone();

    private readonly ApplicationDbContext _context;
    private readonly IPriceHistoryReadService _priceHistoryReadService;
    private readonly TimeProvider _timeProvider;

    public PriceHistoriesController(
        ApplicationDbContext context,
        IPriceHistoryReadService priceHistoryReadService,
        TimeProvider timeProvider)
    {
        _context = context;
        _priceHistoryReadService = priceHistoryReadService;
        _timeProvider = timeProvider;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int? productId,
        int? variantId,
        DateTime? fromDate,
        DateTime? toDate,
        PriceHistoryKind? priceKind,
        PriceChangeSourceType? sourceType,
        string? search,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
                BusinessTimeZone));

        var range = ResolveRange(
            fromDate.HasValue
                ? DateOnly.FromDateTime(fromDate.Value)
                : null,
            toDate.HasValue
                ? DateOnly.FromDateTime(toDate.Value)
                : null,
            today);

        var normalizedSearch = search?.Trim() ?? string.Empty;
        page = Math.Max(page, 1);

        var productRows = await _context.Products
            .AsNoTracking()
            .Where(item => item.Variants.Any())
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Select(item => new PriceHistoryProductOptionViewModel(
                item.Id,
                item.Name))
            .ToListAsync(cancellationToken);

        var variantRows = await _context.ProductVariants
            .AsNoTracking()
            .OrderBy(item => item.Product.Name)
            .ThenBy(item => item.SKU)
            .Select(item => new VariantSelectionRow(
                item.Id,
                item.ProductId,
                item.Product.Name,
                item.SKU,
                item.Color,
                item.Size,
                item.IsActive,
                item.PriceHistories.Any(),
                item.Price,
                item.CurrentPrice,
                item.CurrentPriceSourceType,
                item.CurrentPriceSourceId))
            .ToListAsync(cancellationToken);

        var selected = variantRows.FirstOrDefault(item =>
                variantId.HasValue
                && item.Id == variantId.Value)
            ?? variantRows.FirstOrDefault(item =>
                productId.HasValue
                && item.ProductId == productId.Value)
            ?? variantRows.FirstOrDefault(item => item.HasHistory)
            ?? variantRows.FirstOrDefault();

        var variantOptions = variantRows
            .Select(item => new PriceHistoryVariantOptionViewModel(
                item.Id,
                item.ProductId,
                item.ProductName,
                item.Sku,
                BuildVariantDescription(item.Color, item.Size),
                item.IsActive,
                item.HasHistory))
            .ToArray();

        if (selected is null)
        {
            return View(new PriceHistoryIndexPageViewModel
            {
                GeneratedAtUtc = nowUtc,
                Filter = new PriceHistoryFilterViewModel
                {
                    ProductId = productId,
                    VariantId = variantId,
                    FromDate = range.FromDate,
                    ToDate = range.ToDate,
                    PriceKind = priceKind,
                    SourceType = sourceType,
                    Search = normalizedSearch,
                    Notice = range.Notice,
                    DayCount = range.DayCount
                },
                ProductOptions = productRows,
                VariantOptions = variantOptions,
                Page = 1,
                PageSize = PageSize
            });
        }

        productId = selected.ProductId;
        variantId = selected.Id;

        var fromUtc = ToUtc(range.FromDate);
        var toUtcExclusive = ToUtc(range.ToDate.AddDays(1));

        var baseQuery = _context.PriceHistories
            .AsNoTracking()
            .Where(item =>
                item.ProductVariantId == selected.Id
                && (item.EffectiveFrom ?? item.CreatedAt) >= fromUtc
                && (item.EffectiveFrom ?? item.CreatedAt) < toUtcExclusive);

        if (priceKind.HasValue)
        {
            baseQuery = baseQuery.Where(item =>
                item.PriceKind == priceKind.Value);
        }

        if (sourceType.HasValue)
        {
            baseQuery = baseQuery.Where(item =>
                item.SourceType == sourceType.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            baseQuery = baseQuery.Where(item =>
                item.ChangedBy.Contains(normalizedSearch)
                || (item.Reason != null
                    && item.Reason.Contains(normalizedSearch))
                || item.Note.Contains(normalizedSearch)
                || (item.CorrelationId != null
                    && item.CorrelationId.Contains(normalizedSearch)));
        }

        var totalEvents = await baseQuery
            .CountAsync(cancellationToken);

        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalEvents / (double)PageSize));
        page = Math.Min(page, totalPages);

        var eventRows = await baseQuery
            .OrderByDescending(item =>
                item.EffectiveFrom ?? item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(item => new LedgerProjection(
                item.Id,
                item.PriceKind,
                item.Currency,
                item.OldPrice,
                item.NewPrice,
                item.EventType,
                item.SourceType,
                item.SourceId,
                item.CorrelationId,
                item.Reason,
                item.EffectiveFrom ?? item.CreatedAt,
                item.EffectiveTo,
                item.CreatedAt,
                item.ChangedBy,
                item.Note))
            .ToListAsync(cancellationToken);

        var chartRows = await baseQuery
            .OrderBy(item =>
                item.EffectiveFrom ?? item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(MaximumChartEvents)
            .Select(item => new LedgerProjection(
                item.Id,
                item.PriceKind,
                item.Currency,
                item.OldPrice,
                item.NewPrice,
                item.EventType,
                item.SourceType,
                item.SourceId,
                item.CorrelationId,
                item.Reason,
                item.EffectiveFrom ?? item.CreatedAt,
                item.EffectiveTo,
                item.CreatedAt,
                item.ChangedBy,
                item.Note))
            .ToListAsync(cancellationToken);

        var campaignRows = await _context.PriceCampaigns
            .AsNoTracking()
            .Where(campaign =>
                campaign.CampaignItems.Any(item =>
                    item.VariantId == selected.Id)
                && campaign.StartDate < toUtcExclusive
                && (!campaign.EndDate.HasValue
                    || campaign.EndDate.Value > fromUtc))
            .OrderBy(campaign => campaign.StartDate)
            .ThenBy(campaign => campaign.Id)
            .Select(campaign => new CampaignProjection(
                campaign.Id,
                campaign.Code,
                campaign.Name,
                campaign.Status,
                campaign.StartDate,
                campaign.EndDate))
            .ToListAsync(cancellationToken);

        var campaignById = campaignRows
            .ToDictionary(item => item.Id);

        var summaryAsOfUtc = toUtcExclusive > nowUtc
            ? nowUtc
            : toUtcExclusive;

        var summary30 = await _priceHistoryReadService
            .GetVariantSummaryAsync(
                selected.Id,
                summaryAsOfUtc,
                30,
                cancellationToken);

        var summaryRange = await _priceHistoryReadService
            .GetVariantSummaryAsync(
                selected.Id,
                toUtcExclusive,
                range.DayCount,
                cancellationToken);

        campaignById.TryGetValue(
            selected.CurrentSourceId.GetValueOrDefault(),
            out var currentCampaign);

        var selectedVariant = new PriceHistorySelectedVariantViewModel
        {
            ProductId = selected.ProductId,
            ProductName = selected.ProductName,
            VariantId = selected.Id,
            Sku = selected.Sku,
            Description = BuildVariantDescription(
                selected.Color,
                selected.Size),
            IsActive = selected.IsActive,
            ListPrice = selected.ListPrice,
            CurrentPrice = selected.CurrentPrice,
            CurrentSourceType = selected.CurrentSourceType,
            CurrentSourceId = selected.CurrentSourceId,
            CurrentSourceLabel =
                selected.CurrentSourceType
                    == EffectivePriceSourceType.Campaign
                    ? "Kế hoạch giá"
                    : "Giá niêm yết",
            CurrentCampaignName = currentCampaign?.Name
        };

        var eventViewModels = eventRows
            .Select(item =>
                MapEvent(item, campaignById))
            .ToArray();

        var campaigns = campaignRows
            .Select(item => MapCampaignBand(
                item,
                fromUtc,
                toUtcExclusive))
            .ToArray();

        return View(new PriceHistoryIndexPageViewModel
        {
            GeneratedAtUtc = nowUtc,
            Filter = new PriceHistoryFilterViewModel
            {
                ProductId = selected.ProductId,
                VariantId = selected.Id,
                FromDate = range.FromDate,
                ToDate = range.ToDate,
                PriceKind = priceKind,
                SourceType = sourceType,
                Search = normalizedSearch,
                Notice = range.Notice,
                DayCount = range.DayCount
            },
            ProductOptions = productRows,
            VariantOptions = variantOptions,
            SelectedVariant = selectedVariant,
            Summary = BuildSummary(
                selected,
                summary30,
                summaryRange,
                totalEvents),
            Chart = BuildChart(
                selected,
                chartRows,
                fromUtc,
                toUtcExclusive,
                priceKind,
                totalEvents > MaximumChartEvents),
            Campaigns = campaigns,
            Events = eventViewModels,
            TotalEvents = totalEvents,
            Page = page,
            PageSize = PageSize,
            TotalPages = totalPages
        });
    }

    private static PriceHistorySummaryViewModel BuildSummary(
        VariantSelectionRow selected,
        VariantPriceLedgerSummary? summary30,
        VariantPriceLedgerSummary? summaryRange,
        int totalEvents)
    {
        var difference =
            selected.CurrentPrice - selected.ListPrice;
        var discountPercent =
            selected.ListPrice <= 0
                ? 0
                : Math.Max(
                    0,
                    (selected.ListPrice
                        - selected.CurrentPrice)
                    * 100m
                    / selected.ListPrice);

        return new PriceHistorySummaryViewModel
        {
            Currency =
                summary30?.Currency
                ?? summaryRange?.Currency
                ?? "VND",
            ListPrice = selected.ListPrice,
            CurrentPrice = selected.CurrentPrice,
            LowestEffectivePrice30Days =
                summary30?.LowestEffectivePrice
                ?? selected.CurrentPrice,
            HighestEffectivePrice30Days =
                summary30?.HighestEffectivePrice
                ?? selected.CurrentPrice,
            EffectiveChangeCount30Days =
                summary30?.EffectivePriceChangeCount
                ?? 0,
            LowestEffectivePriceInRange =
                summaryRange?.LowestEffectivePrice
                ?? selected.CurrentPrice,
            HighestEffectivePriceInRange =
                summaryRange?.HighestEffectivePrice
                ?? selected.CurrentPrice,
            EffectiveChangeCountInRange =
                summaryRange?.EffectivePriceChangeCount
                ?? 0,
            FilteredEventCount = totalEvents,
            DifferenceFromListPrice = difference,
            DiscountPercentFromListPrice =
                Math.Round(discountPercent, 1)
        };
    }

    private static PriceHistoryEventRowViewModel MapEvent(
        LedgerProjection item,
        IReadOnlyDictionary<int, CampaignProjection> campaignById)
    {
        var difference = item.NewPrice - item.OldPrice;
        var differencePercent = item.OldPrice <= 0
            ? 0
            : Math.Abs(difference)
                * 100m
                / item.OldPrice;

        campaignById.TryGetValue(
            item.SourceId.GetValueOrDefault(),
            out var campaign);

        return new PriceHistoryEventRowViewModel
        {
            Id = item.Id,
            PriceKind = item.PriceKind,
            PriceKindLabel = PriceKindLabel(item.PriceKind),
            PriceKindTone =
                item.PriceKind == PriceHistoryKind.ListPrice
                    ? "info"
                    : "success",
            Currency = item.Currency,
            OldPrice = item.OldPrice,
            NewPrice = item.NewPrice,
            Difference = difference,
            DifferencePercent =
                Math.Round(differencePercent, 1),
            DirectionTone = difference switch
            {
                > 0 => "increase",
                < 0 => "decrease",
                _ => "neutral"
            },
            EventType = item.EventType,
            EventLabel = EventLabel(item.EventType),
            SourceType = item.SourceType,
            SourceLabel = SourceLabel(item.SourceType),
            SourceId = item.SourceId,
            CampaignName = campaign?.Name,
            CampaignCode = campaign?.Code,
            EffectiveFromLocal =
                ToBusinessTime(item.EffectiveFromUtc),
            EffectiveToLocal =
                item.EffectiveToUtc.HasValue
                    ? ToBusinessTime(
                        item.EffectiveToUtc.Value)
                    : null,
            RecordedAtLocal =
                ToBusinessTime(item.RecordedAtUtc),
            ChangedBy = item.ChangedBy,
            Reason = item.Reason ?? string.Empty,
            Note = item.Note,
            CorrelationId = item.CorrelationId
        };
    }

    private static PriceHistoryCampaignBandViewModel
        MapCampaignBand(
            CampaignProjection campaign,
            DateTime fromUtc,
            DateTime toUtcExclusive)
    {
        var rangeTicks =
            Math.Max(
                1,
                (toUtcExclusive - fromUtc).Ticks);
        var clippedStart =
            campaign.StartDateUtc < fromUtc
                ? fromUtc
                : campaign.StartDateUtc;
        var rawEnd =
            campaign.EndDateUtc
            ?? toUtcExclusive;
        var clippedEnd =
            rawEnd > toUtcExclusive
                ? toUtcExclusive
                : rawEnd;

        var left =
            (clippedStart - fromUtc).Ticks
            * 100m
            / rangeTicks;
        var width =
            Math.Max(
                0.35m,
                (clippedEnd - clippedStart).Ticks
                * 100m
                / rangeTicks);

        return new PriceHistoryCampaignBandViewModel
        {
            CampaignId = campaign.Id,
            Code = campaign.Code,
            Name = campaign.Name,
            Status = campaign.Status,
            StatusLabel =
                CampaignStatusLabel(campaign.Status),
            Tone = CampaignStatusTone(campaign.Status),
            StartLocal =
                ToBusinessTime(campaign.StartDateUtc),
            EndLocal =
                campaign.EndDateUtc.HasValue
                    ? ToBusinessTime(
                        campaign.EndDateUtc.Value)
                    : null,
            LeftPercent = Math.Clamp(left, 0, 100),
            WidthPercent = Math.Clamp(
                width,
                0.35m,
                100 - Math.Clamp(left, 0, 100))
        };
    }

    private static PriceHistoryChartViewModel BuildChart(
        VariantSelectionRow selected,
        IReadOnlyList<LedgerProjection> events,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        PriceHistoryKind? selectedKind,
        bool isTruncated)
    {
        PriceHistoryKind[] kinds = selectedKind.HasValue
            ? [selectedKind.Value]
            :
            [
                PriceHistoryKind.ListPrice,
                PriceHistoryKind.EffectivePrice
            ];

        var seriesInputs = kinds
            .Select(kind =>
            {
                var kindEvents = events
                    .Where(item =>
                        item.PriceKind == kind)
                    .OrderBy(item =>
                        item.EffectiveFromUtc)
                    .ThenBy(item => item.Id)
                    .ToArray();

                var fallback =
                    kind == PriceHistoryKind.ListPrice
                        ? selected.ListPrice
                        : selected.CurrentPrice;
                var baseline =
                    kindEvents.FirstOrDefault()?.OldPrice
                    ?? fallback;

                return new SeriesInput(
                    kind,
                    baseline,
                    kindEvents);
            })
            .ToArray();

        var observedValues = seriesInputs
            .SelectMany(input =>
                input.Events
                    .SelectMany(item => new[]
                    {
                        item.OldPrice,
                        item.NewPrice
                    })
                    .Prepend(input.Baseline))
            .ToArray();

        if (observedValues.Length == 0)
        {
            observedValues =
            [
                selected.ListPrice,
                selected.CurrentPrice
            ];
        }

        var rawMinimum = observedValues.Min();
        var rawMaximum = observedValues.Max();
        var spread = rawMaximum - rawMinimum;
        var padding = spread > 0
            ? spread * 0.1m
            : Math.Max(rawMaximum * 0.06m, 1m);
        var minimum = Math.Max(0, rawMinimum - padding);
        var maximum = rawMaximum + padding;

        if (maximum <= minimum)
        {
            maximum = minimum + 1;
        }

        var series = seriesInputs
            .Select(input => BuildSeries(
                input,
                fromUtc,
                toUtcExclusive,
                minimum,
                maximum))
            .ToArray();

        var yLabels = Enumerable.Range(0, 5)
            .Select(index =>
            {
                var ratio = index / 4m;
                var value =
                    maximum
                    - (maximum - minimum) * ratio;

                return new PriceHistoryChartAxisLabelViewModel
                {
                    Position =
                        ChartTop
                        + (ChartBottom - ChartTop)
                        * ratio,
                    Label = CompactMoney(value)
                };
            })
            .ToArray();

        var duration =
            toUtcExclusive - fromUtc;
        var xLabels = Enumerable.Range(0, 6)
            .Select(index =>
            {
                var ratio = index / 5m;
                var instant =
                    fromUtc
                    + TimeSpan.FromTicks(
                        (long)(duration.Ticks * ratio));

                return new PriceHistoryChartAxisLabelViewModel
                {
                    Position = ratio * ChartWidth,
                    Label = ToBusinessTime(instant)
                        .ToString("dd/MM")
                };
            })
            .ToArray();

        return new PriceHistoryChartViewModel
        {
            HasHistoryEvents = events.Count > 0,
            IsTruncated = isTruncated,
            MinimumPrice = minimum,
            MaximumPrice = maximum,
            Series = series,
            YAxisLabels = yLabels,
            XAxisLabels = xLabels
        };
    }

    private static PriceHistoryChartSeriesViewModel BuildSeries(
        SeriesInput input,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        decimal minimum,
        decimal maximum)
    {
        var durationTicks =
            Math.Max(
                1,
                (toUtcExclusive - fromUtc).Ticks);
        var currentValue = input.Baseline;
        var pathParts = new List<string>
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"M 0 {MapY(currentValue, minimum, maximum):0.###}")
        };
        var points =
            new List<PriceHistoryChartPointViewModel>();

        foreach (var item in input.Events)
        {
            var x = Math.Clamp(
                (item.EffectiveFromUtc - fromUtc).Ticks
                    * ChartWidth
                    / durationTicks,
                0,
                ChartWidth);
            var previousY =
                MapY(currentValue, minimum, maximum);
            var newY =
                MapY(item.NewPrice, minimum, maximum);

            pathParts.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"L {x:0.###} {previousY:0.###}"));
            pathParts.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"L {x:0.###} {newY:0.###}"));

            currentValue = item.NewPrice;

            points.Add(new PriceHistoryChartPointViewModel
            {
                EventId = item.Id,
                X = x,
                Y = newY,
                EffectiveAtLocal =
                    ToBusinessTime(
                        item.EffectiveFromUtc),
                Value = item.NewPrice,
                Label =
                    $"{PriceKindLabel(input.Kind)}: "
                    + $"{item.NewPrice:N0} {item.Currency}"
            });
        }

        pathParts.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"L {ChartWidth:0.###} "
                + $"{MapY(currentValue, minimum, maximum):0.###}"));

        return new PriceHistoryChartSeriesViewModel
        {
            Kind = input.Kind,
            Label = PriceKindLabel(input.Kind),
            Tone =
                input.Kind == PriceHistoryKind.ListPrice
                    ? "list"
                    : "effective",
            SvgPath = string.Join(" ", pathParts),
            Points = points
        };
    }

    private static decimal MapY(
        decimal value,
        decimal minimum,
        decimal maximum)
    {
        var ratio =
            (maximum - value)
            / (maximum - minimum);

        return ChartTop
            + ratio * (ChartBottom - ChartTop);
    }

    private static DateRange ResolveRange(
        DateOnly? requestedFrom,
        DateOnly? requestedTo,
        DateOnly today)
    {
        var from =
            requestedFrom
            ?? today.AddDays(-29);
        var to =
            requestedTo
            ?? today;
        string? notice = null;

        if (from > to)
        {
            (from, to) = (to, from);
            notice =
                "Đã tự động đổi thứ tự ngày bắt đầu và ngày kết thúc.";
        }

        if (to > today)
        {
            to = today;
            notice =
                "Ngày kết thúc đã được giới hạn đến ngày hiện tại.";
        }

        if (from > to)
        {
            from = to;
        }

        var dayCount =
            to.DayNumber - from.DayNumber + 1;

        if (dayCount > MaximumRangeDays)
        {
            from =
                to.AddDays(
                    -(MaximumRangeDays - 1));
            dayCount = MaximumRangeDays;
            notice =
                $"Chỉ hiển thị tối đa {MaximumRangeDays} ngày gần nhất.";
        }

        return new DateRange(
            from,
            to,
            dayCount,
            notice);
    }

    private static DateTime ToUtc(DateOnly date)
    {
        var localUnspecified =
            DateTime.SpecifyKind(
                date.ToDateTime(
                    TimeOnly.MinValue),
                DateTimeKind.Unspecified);

        return TimeZoneInfo.ConvertTimeToUtc(
            localUnspecified,
            BusinessTimeZone);
    }

    private static DateTime ToBusinessTime(
        DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local =>
                value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(
                value,
                DateTimeKind.Utc)
        };

        return TimeZoneInfo.ConvertTimeFromUtc(
            utc,
            BusinessTimeZone);
    }

    private static TimeZoneInfo ResolveBusinessTimeZone()
    {
        foreach (var identifier in new[]
        {
            "Asia/Ho_Chi_Minh",
            "SE Asia Standard Time"
        })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    identifier);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    private static string BuildVariantDescription(
        string? color,
        string? size)
    {
        var values = new[]
        {
            color?.Trim(),
            size?.Trim()
        }
        .Where(value =>
            !string.IsNullOrWhiteSpace(value))
        .ToArray();

        return values.Length == 0
            ? "Lựa chọn tiêu chuẩn"
            : string.Join(" · ", values);
    }

    private static string PriceKindLabel(
        PriceHistoryKind kind)
    {
        return kind switch
        {
            PriceHistoryKind.ListPrice =>
                "Giá niêm yết",
            PriceHistoryKind.EffectivePrice =>
                "Giá bán hiệu lực",
            _ => "Giá"
        };
    }

    private static string EventLabel(
        PriceHistoryEventType eventType)
    {
        return eventType switch
        {
            PriceHistoryEventType.Applied =>
                "Bắt đầu áp dụng",
            PriceHistoryEventType.Restored =>
                "Khôi phục giá",
            PriceHistoryEventType.Replaced =>
                "Thay thế kế hoạch",
            PriceHistoryEventType.Cancelled =>
                "Hủy áp dụng",
            PriceHistoryEventType.ListPriceChanged =>
                "Đổi giá niêm yết",
            PriceHistoryEventType.EffectivePriceChanged =>
                "Đổi giá bán hiệu lực",
            _ => "Dữ liệu kế thừa"
        };
    }

    private static string SourceLabel(
        PriceChangeSourceType sourceType)
    {
        return sourceType switch
        {
            PriceChangeSourceType.Manual =>
                "Quản trị viên",
            PriceChangeSourceType.Market =>
                "Điều chỉnh thị trường",
            PriceChangeSourceType.Promotion =>
                "Khuyến mại",
            PriceChangeSourceType.Recovery =>
                "Khôi phục",
            PriceChangeSourceType.System =>
                "Hệ thống",
            _ => "Dữ liệu cũ"
        };
    }

    private static string CampaignStatusLabel(
        PriceCampaignStatus status)
    {
        return status switch
        {
            PriceCampaignStatus.Draft =>
                "Bản nháp",
            PriceCampaignStatus.Confirmed =>
                "Đã xác nhận",
            PriceCampaignStatus.Scheduled =>
                "Đã lên lịch",
            PriceCampaignStatus.Active =>
                "Đang áp dụng",
            PriceCampaignStatus.Completed =>
                "Đã kết thúc",
            PriceCampaignStatus.Cancelled =>
                "Đã hủy",
            PriceCampaignStatus.Superseded =>
                "Đã thay thế",
            _ => status.ToString()
        };
    }

    private static string CampaignStatusTone(
        PriceCampaignStatus status)
    {
        return status switch
        {
            PriceCampaignStatus.Active =>
                "success",
            PriceCampaignStatus.Scheduled
                or PriceCampaignStatus.Confirmed =>
                "info",
            PriceCampaignStatus.Cancelled =>
                "danger",
            PriceCampaignStatus.Superseded =>
                "warning",
            _ => "neutral"
        };
    }

    private static string CompactMoney(decimal value)
    {
        var absolute = Math.Abs(value);

        if (absolute >= 1_000_000_000m)
        {
            return $"{value / 1_000_000_000m:0.#} tỷ";
        }

        if (absolute >= 1_000_000m)
        {
            return $"{value / 1_000_000m:0.#} tr";
        }

        if (absolute >= 1_000m)
        {
            return $"{value / 1_000m:0.#} k";
        }

        return value.ToString("0");
    }

    private sealed record DateRange(
        DateOnly FromDate,
        DateOnly ToDate,
        int DayCount,
        string? Notice);

    private sealed record VariantSelectionRow(
        int Id,
        int ProductId,
        string ProductName,
        string Sku,
        string? Color,
        string? Size,
        bool IsActive,
        bool HasHistory,
        decimal ListPrice,
        decimal CurrentPrice,
        EffectivePriceSourceType CurrentSourceType,
        int? CurrentSourceId);

    private sealed record LedgerProjection(
        int Id,
        PriceHistoryKind PriceKind,
        string Currency,
        decimal OldPrice,
        decimal NewPrice,
        PriceHistoryEventType EventType,
        PriceChangeSourceType SourceType,
        int? SourceId,
        string? CorrelationId,
        string? Reason,
        DateTime EffectiveFromUtc,
        DateTime? EffectiveToUtc,
        DateTime RecordedAtUtc,
        string ChangedBy,
        string Note);

    private sealed record CampaignProjection(
        int Id,
        string Code,
        string Name,
        PriceCampaignStatus Status,
        DateTime StartDateUtc,
        DateTime? EndDateUtc);

    private sealed record SeriesInput(
        PriceHistoryKind Kind,
        decimal Baseline,
        IReadOnlyList<LedgerProjection> Events);
}
