SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Categories', N'U') IS NULL
        THROW 50001, N'Không tìm thấy bảng dbo.Categories.', 1;

    IF OBJECT_ID(N'dbo.Brands', N'U') IS NULL
        THROW 50002, N'Không tìm thấy bảng dbo.Brands.', 1;

    IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
        THROW 50003, N'Không tìm thấy bảng dbo.Products.', 1;

    IF OBJECT_ID(N'dbo.ProductVariants', N'U') IS NULL
        THROW 50004, N'Không tìm thấy bảng dbo.ProductVariants.', 1;

    ---------------------------------------------------------------------------
    -- 1. Danh mục mẫu
    ---------------------------------------------------------------------------
    DECLARE @Categories TABLE
    (
        [Name] nvarchar(255) NOT NULL,
        [Slug] nvarchar(255) NOT NULL,
        [MetaTitle] nvarchar(255) NOT NULL,
        [MetaDescription] nvarchar(500) NOT NULL
    );

    INSERT INTO @Categories ([Name], [Slug], [MetaTitle], [MetaDescription])
    VALUES
        (N'Điện tử', N'dien-tu', N'Sản phẩm điện tử', N'Điện thoại, thiết bị thông minh và sản phẩm điện tử.'),
        (N'Thời trang', N'thoi-trang', N'Sản phẩm thời trang', N'Quần áo và sản phẩm thời trang nhiều biến thể.'),
        (N'Mỹ phẩm', N'my-pham', N'Mỹ phẩm chính hãng', N'Sản phẩm chăm sóc da và mỹ phẩm.'),
        (N'Gia dụng', N'gia-dung', N'Thiết bị gia dụng', N'Thiết bị và đồ dùng phục vụ gia đình.'),
        (N'Sách', N'sach', N'Sách và tài liệu', N'Sách giấy, sách điện tử và tài liệu học tập.'),
        (N'Thực phẩm đóng gói', N'thuc-pham-dong-goi', N'Thực phẩm đóng gói', N'Thực phẩm có quy cách và trọng lượng rõ ràng.'),
        (N'Phụ kiện', N'phu-kien', N'Phụ kiện công nghệ', N'Phụ kiện điện tử và phụ kiện cá nhân.');

    INSERT INTO dbo.Categories
    (
        [Name],
        [ParentId],
        [Slug],
        [MetaTitle],
        [MetaDescription]
    )
    SELECT
        source.[Name],
        NULL,
        source.[Slug],
        source.[MetaTitle],
        source.[MetaDescription]
    FROM @Categories AS source
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.Categories AS existing
        WHERE existing.[Slug] = source.[Slug]
    );

    ---------------------------------------------------------------------------
    -- 2. Thương hiệu mẫu
    ---------------------------------------------------------------------------
    DECLARE @Brands TABLE
    (
        [Name] nvarchar(255) NOT NULL,
        [Slug] nvarchar(255) NOT NULL,
        [Description] nvarchar(max) NULL
    );

    INSERT INTO @Brands ([Name], [Slug], [Description])
    VALUES
        (N'Apple', N'apple', N'Thương hiệu thiết bị điện tử.'),
        (N'Samsung', N'samsung', N'Thương hiệu điện tử và thiết bị thông minh.'),
        (N'Uniqlo', N'uniqlo', N'Thương hiệu thời trang.'),
        (N'Anessa', N'anessa', N'Thương hiệu chăm sóc da.'),
        (N'Philips', N'philips', N'Thương hiệu điện tử gia dụng.'),
        (N'Trung Nguyên', N'trung-nguyen', N'Thương hiệu cà phê Việt Nam.'),
        (N'Sony', N'sony', N'Thương hiệu điện tử và âm thanh.');

    INSERT INTO dbo.Brands
    (
        [Name],
        [Slug],
        [Description],
        [IsActive]
    )
    SELECT
        source.[Name],
        source.[Slug],
        source.[Description],
        CAST(1 AS bit)
    FROM @Brands AS source
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.Brands AS existing
        WHERE existing.[Slug] = source.[Slug]
    );

    ---------------------------------------------------------------------------
    -- 3. Sản phẩm mẫu
    -- Không chèn bản ghi vào dbo.ProductImage.
    ---------------------------------------------------------------------------
    DECLARE @Products TABLE
    (
        [Name] nvarchar(255) NOT NULL,
        [Slug] nvarchar(255) NOT NULL,
        [Description] nvarchar(max) NULL,
        [CategorySlug] nvarchar(255) NOT NULL,
        [BrandSlug] nvarchar(255) NULL,
        [MetaTitle] nvarchar(255) NULL,
        [MetaDescription] nvarchar(500) NULL
    );

    INSERT INTO @Products
    (
        [Name],
        [Slug],
        [Description],
        [CategorySlug],
        [BrandSlug],
        [MetaTitle],
        [MetaDescription]
    )
    VALUES
        (
            N'iPhone 16 Pro',
            N'iphone-16-pro',
            N'Điện thoại cao cấp với nhiều lựa chọn dung lượng và màu sắc.',
            N'dien-tu',
            N'apple',
            N'iPhone 16 Pro chính hãng',
            N'iPhone 16 Pro với các biến thể màu sắc và dung lượng.'
        ),
        (
            N'Samsung Galaxy S25',
            N'samsung-galaxy-s25',
            N'Điện thoại Android cao cấp dành cho nhu cầu công việc và giải trí.',
            N'dien-tu',
            N'samsung',
            N'Samsung Galaxy S25',
            N'Galaxy S25 nhiều màu sắc và dung lượng.'
        ),
        (
            N'Áo thun cotton basic',
            N'ao-thun-cotton-basic',
            N'Áo thun cotton kiểu dáng cơ bản, phù hợp mặc hằng ngày.',
            N'thoi-trang',
            N'uniqlo',
            N'Áo thun cotton basic',
            N'Áo thun cotton với nhiều màu và kích thước.'
        ),
        (
            N'Kem chống nắng Anessa Perfect UV',
            N'kem-chong-nang-anessa-perfect-uv',
            N'Kem chống nắng dùng hằng ngày với nhiều quy cách dung tích.',
            N'my-pham',
            N'anessa',
            N'Kem chống nắng Anessa Perfect UV',
            N'Kem chống nắng Anessa với nhiều lựa chọn dung tích.'
        ),
        (
            N'Nồi chiên không dầu Philips 6.2L',
            N'noi-chien-khong-dau-philips-6-2l',
            N'Nồi chiên không dầu dung tích lớn cho gia đình.',
            N'gia-dung',
            N'philips',
            N'Nồi chiên không dầu Philips 6.2L',
            N'Nồi chiên không dầu Philips dung tích 6.2 lít.'
        ),
        (
            N'Sách Clean Code',
            N'sach-clean-code',
            N'Sách tham khảo về cách tổ chức và cải thiện chất lượng mã nguồn.',
            N'sach',
            NULL,
            N'Sách Clean Code',
            N'Sách Clean Code với phiên bản sách giấy và ebook.'
        ),
        (
            N'Cà phê rang xay Arabica',
            N'ca-phe-rang-xay-arabica',
            N'Cà phê Arabica rang xay đóng gói theo nhiều trọng lượng.',
            N'thuc-pham-dong-goi',
            N'trung-nguyen',
            N'Cà phê rang xay Arabica',
            N'Cà phê Arabica đóng gói 500g và 1kg.'
        ),
        (
            N'Tai nghe Bluetooth Sony',
            N'tai-nghe-bluetooth-sony',
            N'Tai nghe không dây phục vụ nghe nhạc và gọi điện.',
            N'phu-kien',
            N'sony',
            N'Tai nghe Bluetooth Sony',
            N'Tai nghe Bluetooth Sony với nhiều màu sắc.'
        );

    INSERT INTO dbo.Products
    (
        [Name],
        [Description],
        [IsActive],
        [CategoryId],
        [BrandId],
        [Slug],
        [MetaTitle],
        [MetaDescription],
        [MetaKeywords]
    )
    SELECT
        source.[Name],
        source.[Description],
        CAST(1 AS bit),
        category.[Id],
        brand.[Id],
        source.[Slug],
        source.[MetaTitle],
        source.[MetaDescription],
        NULL
    FROM @Products AS source
    INNER JOIN dbo.Categories AS category
        ON category.[Slug] = source.[CategorySlug]
    LEFT JOIN dbo.Brands AS brand
        ON brand.[Slug] = source.[BrandSlug]
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.Products AS existing
        WHERE existing.[Slug] = source.[Slug]
    );

    ---------------------------------------------------------------------------
    -- 4. Biến thể mẫu
    -- ImageUrl luôn NULL.
    -- SKU tạm chỉ tồn tại trong transaction, sau đó đổi sang SKU-P000001-V00000001...
    ---------------------------------------------------------------------------
    DECLARE @Variants TABLE
    (
        [ProductSlug] nvarchar(255) NOT NULL,
        [Color] nvarchar(50) NULL,
        [Size] nvarchar(50) NULL,
        [Price] decimal(18,2) NOT NULL,
        [StockQuantity] int NOT NULL
    );

    INSERT INTO @Variants
    (
        [ProductSlug],
        [Color],
        [Size],
        [Price],
        [StockQuantity]
    )
    VALUES
        (N'iphone-16-pro', N'Titan tự nhiên', N'256GB', 28990000, 20),
        (N'iphone-16-pro', N'Titan đen', N'512GB', 34990000, 12),
        (N'samsung-galaxy-s25', N'Xanh navy', N'256GB', 22990000, 18),
        (N'samsung-galaxy-s25', N'Bạc', N'512GB', 26990000, 10),
        (N'ao-thun-cotton-basic', N'Đen', N'S', 299000, 40),
        (N'ao-thun-cotton-basic', N'Đen', N'M', 299000, 55),
        (N'ao-thun-cotton-basic', N'Trắng', N'L', 299000, 35),
        (N'kem-chong-nang-anessa-perfect-uv', NULL, N'60ml', 649000, 30),
        (N'kem-chong-nang-anessa-perfect-uv', NULL, N'90ml', 879000, 22),
        (N'noi-chien-khong-dau-philips-6-2l', N'Đen', N'6.2L', 3290000, 14),
        (N'sach-clean-code', NULL, N'Bìa mềm', 349000, 25),
        (N'sach-clean-code', NULL, N'Ebook', 199000, 999),
        (N'ca-phe-rang-xay-arabica', NULL, N'500g', 189000, 60),
        (N'ca-phe-rang-xay-arabica', NULL, N'1kg', 349000, 45),
        (N'tai-nghe-bluetooth-sony', N'Đen', N'Tiêu chuẩn', 1990000, 28),
        (N'tai-nghe-bluetooth-sony', N'Trắng', N'Tiêu chuẩn', 1990000, 21);

    DECLARE @InsertedVariants TABLE
    (
        [Id] int NOT NULL PRIMARY KEY
    );

    INSERT INTO dbo.ProductVariants
    (
        [SKU],
        [Color],
        [Size],
        [Price],
        [CurrentPrice],
        [StockQuantity],
        [ImageUrl],
        [IsActive],
        [ProductId]
    )
    OUTPUT inserted.[Id] INTO @InsertedVariants ([Id])
    SELECT
        N'TMP-' + REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''),
        source.[Color],
        source.[Size],
        source.[Price],
        source.[Price],
        source.[StockQuantity],
        NULL,
        CAST(1 AS bit),
        product.[Id]
    FROM @Variants AS source
    INNER JOIN dbo.Products AS product
        ON product.[Slug] = source.[ProductSlug]
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.ProductVariants AS existing
        WHERE existing.[ProductId] = product.[Id]
          AND ISNULL(existing.[Color], N'') = ISNULL(source.[Color], N'')
          AND ISNULL(existing.[Size], N'') = ISNULL(source.[Size], N'')
    );

    IF EXISTS
    (
        SELECT 1
        FROM @InsertedVariants AS insertedVariant
        INNER JOIN dbo.ProductVariants AS target
            ON target.[Id] = insertedVariant.[Id]
        INNER JOIN dbo.ProductVariants AS conflicting
            ON conflicting.[Id] <> target.[Id]
           AND conflicting.[SKU] =
               N'SKU-P' +
               RIGHT(
                   REPLICATE('0', 6) + CONVERT(varchar(20), target.[ProductId]),
                   6
               ) +
               N'-V' +
               RIGHT(
                   REPLICATE('0', 8) + CONVERT(varchar(20), target.[Id]),
                   8
               )
    )
    BEGIN
        THROW 50005, N'SKU tự động dự kiến bị trùng với dữ liệu SKU cũ.', 1;
    END;

    UPDATE target
    SET target.[SKU] =
        N'SKU-P' +
        RIGHT(
            REPLICATE('0', 6) + CONVERT(varchar(20), target.[ProductId]),
            6
        ) +
        N'-V' +
        RIGHT(
            REPLICATE('0', 8) + CONVERT(varchar(20), target.[Id]),
            8
        )
    FROM dbo.ProductVariants AS target
    INNER JOIN @InsertedVariants AS insertedVariant
        ON insertedVariant.[Id] = target.[Id];

    COMMIT TRANSACTION;

    SELECT
        product.[Id] AS ProductId,
        product.[Name] AS ProductName,
        variant.[Id] AS VariantId,
        variant.[SKU],
        variant.[Color],
        variant.[Size],
        variant.[Price],
        variant.[CurrentPrice],
        variant.[StockQuantity],
        variant.[ImageUrl]
    FROM dbo.Products AS product
    INNER JOIN dbo.ProductVariants AS variant
        ON variant.[ProductId] = product.[Id]
    WHERE product.[Slug] IN
    (
        N'iphone-16-pro',
        N'samsung-galaxy-s25',
        N'ao-thun-cotton-basic',
        N'kem-chong-nang-anessa-perfect-uv',
        N'noi-chien-khong-dau-philips-6-2l',
        N'sach-clean-code',
        N'ca-phe-rang-xay-arabica',
        N'tai-nghe-bluetooth-sony'
    )
    ORDER BY product.[Id], variant.[Id];
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
