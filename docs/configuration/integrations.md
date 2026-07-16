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
dotnet user-secrets set "Integrations:Shipping:Ghn:SenderName" "<STORE_NAME>"
dotnet user-secrets set "Integrations:Shipping:Ghn:SenderPhone" "<STORE_PHONE>"
dotnet user-secrets set "Integrations:Shipping:Ghn:SenderAddress" "<STORE_ADDRESS>"
dotnet user-secrets set "Integrations:Shipping:Ghn:WebhookSecret" "<NEW_RANDOM_SECRET_AT_LEAST_16_CHARACTERS>"
```

The Base URL must contain only the HTTPS host:

```text
https://dev-online-gateway.ghn.vn
```

Do not append `/shiip/public-api`; the client endpoints already contain that
path.

Checkout uses GHN for:

- province, district and ward lookup;
- address validation;
- available service lookup;
- shipping fee calculation;
- outbound shipment creation after the order becomes eligible for fulfilment.

`DefaultWeightGram`, `DefaultLengthCm`, `DefaultWidthCm` and
`DefaultHeightCm` are currently parcel defaults. They must be replaced by
product-level logistics data in a later catalog phase before production use.

Checkout fails closed when GHN is disabled or unavailable. It does not create
an order using a guessed shipping fee.

## VNPay Sandbox with User Secrets

The application implements VNPay payment URL generation, Return URL handling,
authoritative IPN processing, amount verification, signature verification and
expiration of unpaid orders.

```powershell
dotnet user-secrets set "Integrations:Payments:VnPay:Enabled" "true"
dotnet user-secrets set "Integrations:Payments:VnPay:TmnCode" "<TMN_CODE>"
dotnet user-secrets set "Integrations:Payments:VnPay:HashSecret" "<HASH_SECRET>"
dotnet user-secrets set "Integrations:Payments:VnPay:ReturnUrl" "https://localhost:<PORT>/payments/vnpay/return"
dotnet user-secrets set "Integrations:Payments:VnPay:IpnUrl" "https://<PUBLIC_HTTPS_HOST>/payments/vnpay/ipn"
dotnet user-secrets set "Integrations:Payments:VnPay:ExpirationGraceMinutes" "5"
```

The payment endpoint is configured separately:

```text
BaseUrl: https://sandbox.vnpayment.vn
PaymentPath: /paymentv2/vpcpay.html
```

Important operational rules:

1. Return URL is used to display the result to the customer.
2. IPN and Return URL both pass through the same idempotent payment processor.
3. The processor verifies signature, `TmnCode`, merchant reference and amount.
4. A successful payment changes the order from `PendingPayment` to `Placed`
   and commits the existing inventory reservation.
5. A failed or expired payment cancels the order and releases inventory.
6. Do not manually mark a VNPay transaction as paid from the browser response.

For local IPN testing, VNPay must be able to reach a public HTTPS URL. A
localhost-only URL cannot receive server-to-server IPN requests.

## Environment variables

Production or sandbox deployments can use double underscores:

```text
Integrations__Shipping__Ghn__Token
Integrations__Shipping__Ghn__ShopId
Integrations__Shipping__Ghn__FromDistrictId
Integrations__Shipping__Ghn__FromWardCode
Integrations__Shipping__Ghn__SenderName
Integrations__Shipping__Ghn__SenderPhone
Integrations__Shipping__Ghn__SenderAddress
Integrations__Shipping__Ghn__WebhookSecret
Integrations__Payments__VnPay__TmnCode
Integrations__Payments__VnPay__HashSecret
Integrations__Payments__VnPay__ReturnUrl
Integrations__Payments__VnPay__IpnUrl
```

## Required security action

Credentials previously committed to Git must be treated as compromised. Revoke
or rotate them at each provider before configuring replacements. Removing a
secret from the latest commit does not make the old credential safe again.
