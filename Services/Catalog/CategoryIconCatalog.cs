using System.Globalization;
using System.Text;

namespace WebApplication2.Services.Catalog;

public sealed record CategoryIconDefinition(
    string Key,
    string Label,
    string CssClass,
    string GroupKey,
    string GroupLabel,
    string Keywords);

public sealed record CategoryIconGroup(
    string Key,
    string Label);

public static class CategoryIconCatalog
{
    public const string DefaultIconKey = "folder";

    private static readonly CategoryIconDefinition[] Definitions =
    [
        new("folder", "Danh mục chung", "fa-solid fa-folder", "general", "Chung", "folder danh muc thu muc chung"),
        new("catalog", "Tổng hợp sản phẩm", "fa-solid fa-border-all", "general", "Chung", "catalog tong hop san pham luoi"),
        new("tags", "Ưu đãi và nhãn hàng", "fa-solid fa-tags", "general", "Chung", "tag nhan hang uu dai khuyen mai"),
        new("gift", "Quà tặng", "fa-solid fa-gift", "general", "Chung", "gift qua tang sinh nhat"),

        new("electronics", "Điện tử", "fa-solid fa-microchip", "technology", "Công nghệ", "dien tu cong nghe chip linh kien"),
        new("phone", "Điện thoại", "fa-solid fa-mobile-screen-button", "technology", "Công nghệ", "dien thoai smartphone mobile"),
        new("laptop", "Máy tính xách tay", "fa-solid fa-laptop", "technology", "Công nghệ", "laptop may tinh notebook"),
        new("tablet", "Máy tính bảng", "fa-solid fa-tablet-screen-button", "technology", "Công nghệ", "tablet may tinh bang"),
        new("television", "Tivi và màn hình", "fa-solid fa-tv", "technology", "Công nghệ", "tivi television man hinh"),
        new("camera", "Máy ảnh", "fa-solid fa-camera", "technology", "Công nghệ", "camera may anh quay phim"),
        new("audio", "Âm thanh", "fa-solid fa-headphones", "technology", "Công nghệ", "audio am thanh tai nghe loa"),
        new("gaming", "Thiết bị chơi game", "fa-solid fa-gamepad", "technology", "Công nghệ", "gaming game tro choi console"),
        new("computer-accessories", "Phụ kiện máy tính", "fa-solid fa-keyboard", "technology", "Công nghệ", "keyboard ban phim chuot phu kien"),

        new("fashion", "Thời trang", "fa-solid fa-shirt", "fashion", "Thời trang", "fashion thoi trang quan ao"),
        new("shoes", "Giày dép", "fa-solid fa-shoe-prints", "fashion", "Thời trang", "shoes giay dep sneaker"),
        new("eyewear", "Kính mắt", "fa-solid fa-glasses", "fashion", "Thời trang", "glasses kinh mat"),
        new("jewelry", "Trang sức", "fa-solid fa-gem", "fashion", "Thời trang", "jewelry trang suc gem"),
        new("watch", "Đồng hồ", "fa-solid fa-clock", "fashion", "Thời trang", "watch dong ho"),
        new("bag", "Túi và phụ kiện", "fa-solid fa-bag-shopping", "fashion", "Thời trang", "bag tui xach phu kien"),

        new("home", "Nhà cửa", "fa-solid fa-house", "home", "Nhà cửa", "home nha cua gia dinh"),
        new("furniture", "Nội thất", "fa-solid fa-couch", "home", "Nhà cửa", "furniture noi that sofa"),
        new("kitchen", "Nhà bếp", "fa-solid fa-kitchen-set", "home", "Nhà cửa", "kitchen nha bep"),
        new("appliances", "Điện gia dụng", "fa-solid fa-blender", "home", "Nhà cửa", "appliances dien gia dung"),
        new("lighting", "Đèn và chiếu sáng", "fa-solid fa-lightbulb", "home", "Nhà cửa", "lighting den chieu sang"),
        new("tools", "Dụng cụ", "fa-solid fa-screwdriver-wrench", "home", "Nhà cửa", "tools dung cu sua chua"),
        new("decor", "Trang trí", "fa-solid fa-palette", "home", "Nhà cửa", "decor trang tri my thuat"),

        new("beauty", "Làm đẹp", "fa-solid fa-wand-magic-sparkles", "beauty", "Sức khỏe và làm đẹp", "beauty lam dep my pham"),
        new("health", "Chăm sóc sức khỏe", "fa-solid fa-heart-pulse", "beauty", "Sức khỏe và làm đẹp", "health suc khoe cham soc"),
        new("spa", "Chăm sóc cá nhân", "fa-solid fa-spa", "beauty", "Sức khỏe và làm đẹp", "spa cham soc ca nhan"),

        new("fitness", "Thể hình", "fa-solid fa-dumbbell", "sports", "Thể thao", "fitness gym the hinh"),
        new("sports", "Thể thao", "fa-solid fa-person-running", "sports", "Thể thao", "sports the thao chay bo"),
        new("bicycle", "Xe đạp", "fa-solid fa-bicycle", "sports", "Thể thao", "bicycle xe dap"),
        new("outdoor", "Dã ngoại", "fa-solid fa-campground", "sports", "Thể thao", "outdoor da ngoai camping"),

        new("baby", "Mẹ và bé", "fa-solid fa-baby", "family", "Gia đình", "baby me va be tre em"),
        new("toys", "Đồ chơi", "fa-solid fa-puzzle-piece", "family", "Gia đình", "toys do choi puzzle"),
        new("books", "Sách", "fa-solid fa-book-open", "family", "Gia đình", "books sach giao duc"),
        new("stationery", "Văn phòng phẩm", "fa-solid fa-pen-ruler", "family", "Gia đình", "stationery van phong pham but"),
        new("pets", "Thú cưng", "fa-solid fa-paw", "family", "Gia đình", "pets thu cung cho meo"),

        new("food", "Thực phẩm", "fa-solid fa-utensils", "food", "Ẩm thực", "food thuc pham an uong"),
        new("grocery", "Bách hóa", "fa-solid fa-basket-shopping", "food", "Ẩm thực", "grocery bach hoa sieu thi"),
        new("coffee", "Đồ uống", "fa-solid fa-mug-hot", "food", "Ẩm thực", "coffee do uong ca phe"),

        new("car", "Ô tô", "fa-solid fa-car", "vehicle", "Phương tiện", "car oto xe hoi"),
        new("motorcycle", "Xe máy", "fa-solid fa-motorcycle", "vehicle", "Phương tiện", "motorcycle xe may"),

        new("office", "Thiết bị văn phòng", "fa-solid fa-briefcase", "business", "Văn phòng", "office van phong doanh nghiep"),
        new("printing", "Máy in và in ấn", "fa-solid fa-print", "business", "Văn phòng", "printer may in in an")
    ];

    private static readonly IReadOnlyDictionary<string, CategoryIconDefinition> ByKey =
        Definitions.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);

    private static readonly CategoryIconGroup[] IconGroups = Definitions
        .Select(item => new CategoryIconGroup(item.GroupKey, item.GroupLabel))
        .DistinctBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static IReadOnlyList<CategoryIconGroup> Groups => IconGroups;

    public static bool TryGet(string? key, out CategoryIconDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(key)
            && ByKey.TryGetValue(key.Trim(), out var matched))
        {
            definition = matched;
            return true;
        }

        definition = ByKey[DefaultIconKey];
        return false;
    }

    public static string NormalizeKey(string? key)
    {
        return TryGet(key, out var definition)
            ? definition.Key
            : DefaultIconKey;
    }

    public static string ResolveCssClass(string? key)
    {
        TryGet(key, out var definition);
        return definition.CssClass;
    }

    public static IReadOnlyList<CategoryIconDefinition> Search(
        string? query,
        string? group,
        int limit)
    {
        var normalizedQuery = NormalizeSearch(query);
        var normalizedGroup = group?.Trim();

        IEnumerable<CategoryIconDefinition> result = Definitions;

        if (!string.IsNullOrWhiteSpace(normalizedGroup))
        {
            result = result.Where(item =>
                string.Equals(
                    item.GroupKey,
                    normalizedGroup,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            result = result.Where(item =>
            {
                var searchable = NormalizeSearch(
                    $"{item.Key} {item.Label} {item.GroupLabel} {item.Keywords}");
                return searchable.Contains(
                    normalizedQuery,
                    StringComparison.Ordinal);
            });
        }

        return result
            .OrderBy(item => item.GroupLabel)
            .ThenBy(item => item.Label)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArray();
    }

    private static string NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character == 'đ' ? 'd' : character);
            }
        }

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }
}
