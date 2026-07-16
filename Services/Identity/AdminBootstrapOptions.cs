namespace WebApplication2.Services.Identity;

public sealed class AdminBootstrapOptions
{
    public const string SectionName = "Authentication:BootstrapAdmin";

    public bool Enabled { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    public bool IsConfigured =>
        !Enabled
        || (!string.IsNullOrWhiteSpace(Username)
            && !string.IsNullOrWhiteSpace(Email)
            && Password.Length >= 12
            && !string.IsNullOrWhiteSpace(FullName)
            && !string.IsNullOrWhiteSpace(PhoneNumber));
}
