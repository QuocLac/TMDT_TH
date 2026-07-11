namespace WebApplication2.Services.Commerce.Returns;

public interface IReturnCodeGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken);
}
