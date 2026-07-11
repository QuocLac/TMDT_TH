# GHN Sandbox shipping execution

Phase 5 uses `Integrations:Shipping:Ghn`. Credentials and the webhook secret
must stay outside `appsettings.json`.

## Required User Secrets

```powershell
dotnet user-secrets set "Integrations:Shipping:Ghn:Enabled" "true"
dotnet user-secrets set "Integrations:Shipping:Ghn:Token" "<GHN_TOKEN>"
dotnet user-secrets set "Integrations:Shipping:Ghn:ShopId" "<SHOP_ID>"
dotnet user-secrets set "Integrations:Shipping:Ghn:FromDistrictId" "<DISTRICT_ID>"
dotnet user-secrets set "Integrations:Shipping:Ghn:FromWardCode" "<WARD_CODE>"
dotnet user-secrets set "Integrations:Shipping:Ghn:SenderName" "<SENDER_NAME>"
dotnet user-secrets set "Integrations:Shipping:Ghn:SenderPhone" "<SENDER_PHONE>"
dotnet user-secrets set "Integrations:Shipping:Ghn:SenderAddress" "<SENDER_ADDRESS>"
dotnet user-secrets set "Integrations:Shipping:Ghn:WebhookSecret" "<RANDOM_SECRET_AT_LEAST_16_CHARS>"
```

## Webhook URL

The current FastBuy adapter protects the callback with its own shared secret;
it does not claim to implement a GHN signature scheme.

```text
https://your-host/integrations/ghn/webhook?secret=<WebhookSecret>
```

Controlled tests may use the `X-FastBuy-GHN-Secret` header instead.

## Outbox behavior

Admin commands only write an `IntegrationOutboxMessage` and commit. The hosted
worker calls GHN after the transaction, then applies the provider response in a
new transaction.

Supported messages:

```text
ShipmentCreateRequested
ShipmentCancelRequested
```

Failed retryable calls use exponential backoff. Non-retryable failures keep
`NextAttemptAt = null` and are not selected again. A message left in
`Processing` longer than `WorkerLockMinutes` is reclaimed. The FastBuy order
code is sent as `client_order_code`; the local outbox idempotency key remains
stable for the shipment command.

## Operational pages

```text
/Admin/Shipping
/Admin/Shipping/{shipmentId}
```

The details page supports service lookup, quote, create queue, cancellation
queue, manual provider reconciliation and inbox/outbox diagnostics.

## Cancellation safety

Once `CarrierHandoffAt` exists or shipment status is `Picking`/`InTransit`, the
cancellation flow is rejected. If a GHN order code exists, stock compensation
and cancellation approval wait for provider status `Cancelled`.

## Return shipments

Phase 6 also creates GHN reverse-logistics orders. The sender is the customer
address snapshot from the original order; the recipient and fallback return
address are taken from the configured FastBuy shop address.

Return shipment rules:

- `Direction = Return`
- `ReturnRequestId` and `ParentShipmentId` are required
- COD is always zero
- the return request must be approved
- a GHN `Delivered` event means the parcel reached the FastBuy warehouse
- outbound order fulfillment is never overwritten by a return shipment event

Return create requests use a separate outbox message type:

```text
ReturnShipmentCreateRequested
```
