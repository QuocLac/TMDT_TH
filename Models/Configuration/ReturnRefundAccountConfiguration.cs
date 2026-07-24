using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebApplication2.Models.Configuration;

public sealed class ReturnRefundAccountConfiguration
    : IEntityTypeConfiguration<ReturnRefundAccount>
{
    public void Configure(EntityTypeBuilder<ReturnRefundAccount> entity)
    {
        entity.HasIndex(item => item.ReturnRequestId)
            .IsUnique();

        entity.Property(item => item.RowVersion)
            .IsRowVersion();

        entity.HasOne(item => item.ReturnRequest)
            .WithOne(item => item.RefundAccount)
            .HasForeignKey<ReturnRefundAccount>(item => item.ReturnRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable("ReturnRefundAccounts", table =>
        {
            table.HasCheckConstraint(
                "CK_ReturnRefundAccount_AccountNumber",
                "LEN([AccountNumber]) >= 6 AND LEN([AccountNumber]) <= 24");
        });
    }
}
