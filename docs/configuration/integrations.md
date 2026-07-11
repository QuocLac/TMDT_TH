# Integration configuration

Provider credentials must never be committed to source control. The checked-in
`appsettings.json` keeps providers disabled and contains only non-secret defaults.

## GHN Sandbox with User Secrets

Run these commands from the directory containing `WebApplication2.csproj`:

```powershell
dotnet user-secrets set "Integrations:Shipping:Ghn:Enabled" "true"
dotnet user-secrets set "Integrations:Shipping:Ghn:Token" "<NEW_GHN_TOKEN>"
dotnet user-secrets set "Integrations:Shipping:Ghn:ShopId" "<SHOP_ID>"
dotnet user-secrets set "Integrations:Shipping:Ghn:FromDistrictId" "<DISTRICT_ID>"
dotnet user-secrets set "Integrations:Shipping:Ghn:FromWardCode" "<WARD_CODE>"
```

The Base URL must contain only the HTTPS host:

```text
https://dev-online-gateway.ghn.vn
```

Do not append `/shiip/public-api`; the client endpoints already contain that path.

## VNPay Sandbox with User Secrets

```powershell
dotnet user-secrets set "Integrations:Payments:VnPay:Enabled" "true"
dotnet user-secrets set "Integrations:Payments:VnPay:TmnCode" "<TMN_CODE>"
dotnet user-secrets set "Integrations:Payments:VnPay:HashSecret" "<HASH_SECRET>"
dotnet user-secrets set "Integrations:Payments:VnPay:ReturnUrl" "https://localhost:<PORT>/payment/vnpay/return"
dotnet user-secrets set "Integrations:Payments:VnPay:IpnUrl" "https://<PUBLIC_HTTPS_HOST>/payment/vnpay/ipn"
```

VNPay remains disabled until its payment adapter and authoritative IPN flow are implemented.

## Environment variables

Production or sandbox deployments can use double underscores:

```text
Integrations__Shipping__Ghn__Token
Integrations__Shipping__Ghn__ShopId
Integrations__Shipping__Ghn__FromDistrictId
Integrations__Shipping__Ghn__FromWardCode
Integrations__Payments__VnPay__TmnCode
Integrations__Payments__VnPay__HashSecret
```

## Required security action

Credentials previously committed to Git must be treated as compromised. Revoke or
rotate them at each provider before configuring replacements. Removing a secret from
the latest commit does not make the old credential safe again.
