# Authentication configuration

Phase 7 uses ASP.NET Core cookie authentication on the existing `Account` entity.

## Bootstrap the first Admin

Do not store the Admin password in `appsettings.json`. Use User Secrets:

```powershell
dotnet user-secrets set "Authentication:BootstrapAdmin:Enabled" "true"
dotnet user-secrets set "Authentication:BootstrapAdmin:Username" "admin"
dotnet user-secrets set "Authentication:BootstrapAdmin:Email" "admin@example.com"
dotnet user-secrets set "Authentication:BootstrapAdmin:Password" "replace-with-a-strong-password"
dotnet user-secrets set "Authentication:BootstrapAdmin:FullName" "FastBuy Admin"
dotnet user-secrets set "Authentication:BootstrapAdmin:PhoneNumber" "0900000000"
```

Apply the authentication migration before starting the application. Run the application once so the Admin is created, then disable bootstrap:

```powershell
dotnet user-secrets set "Authentication:BootstrapAdmin:Enabled" "false"
```

The bootstrapper:

- only runs when explicitly enabled;
- stops when an Admin already exists;
- never upgrades an existing Customer account to Admin;
- never overwrites an existing password;
- requires a password of at least 12 characters.

## Customer authentication behavior

- Anonymous visitors can browse and keep using the Session cart.
- Opening `/checkout` requires authentication.
- The Session cart remains available after login.
- Orders are bound to the authenticated `CustomerId`.
- Admin Area requires the `Admin` role.
- Password verification uses `IPasswordHasher<Account>`.
- Five failed login attempts trigger a 15-minute lockout.
- Cookie validation checks `IsActive`, lockout state and `SecurityStamp`.
