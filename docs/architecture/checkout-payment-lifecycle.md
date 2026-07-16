# Checkout, GHN quote and VNPay lifecycle

## Scope

This flow turns selected Session cart lines into a persisted commercial order.
The browser never decides the final product price, stock quantity, shipping fee
or payment status.

## Checkout sequence

1. Customer selects cart lines.
2. `POST /cart/prepare-checkout` verifies that selected lines are purchasable.
3. Anonymous customers are redirected to login with `/checkout` as the return
   URL.
4. Checkout loads the authenticated customer profile and default address.
5. Province, district and ward values are validated against GHN data.
6. `POST /checkout/shipping-quote` reloads the selected cart from the server,
   selects a GHN service and requests the provider fee.
7. When the order form is submitted, the server repeats cart, address and GHN
   quote validation.
8. If the provider fee or service changed, the order is not created. The page
   shows the new total and requires confirmation again.

Client fields named `ExpectedShippingFee`, `ShippingServiceId` and
`ShippingServiceTypeId` are comparison snapshots only. They are never trusted
as the values used to create the order.

## COD lifecycle

```text
Cart selected
  -> create Order(Placed, CodPending, Unfulfilled)
  -> create PaymentTransaction(Internal, CodPending)
  -> create Shipment(GHN, Draft, COD = GrandTotal)
  -> reserve inventory
  -> commit inventory reservation
  -> clear purchased Session cart lines
```

The GHN shipment is not created at the provider during checkout. The existing
admin order workflow must move the order to the correct processing and
fulfilment states before queuing shipment creation.

## VNPay lifecycle

```text
Cart selected
  -> create Order(PendingPayment, Pending, Unfulfilled)
  -> create PaymentTransaction(VNPAY, Pending)
  -> create Shipment(GHN, Draft, COD = 0)
  -> reserve inventory
  -> create signed VNPay URL
  -> redirect customer to VNPay
```

The order remains `PendingPayment` until a signed provider callback is
processed.

### Successful callback

The shared callback processor verifies:

- HMAC-SHA512 signature;
- merchant `TmnCode`;
- merchant reference;
- transaction amount;
- response code and transaction status;
- provider transaction number.

Then, inside a serializable transaction:

```text
PaymentTransaction: Pending -> Paid
Order.PaymentStatus: Pending -> Paid
Order.OrderStatus: PendingPayment -> Placed
Inventory reservation: Reserved -> Committed
```

Purchased cart lines are cleared when the authenticated customer returns or
when the same checkout request is opened again.

### Failed callback

For a correctly signed provider failure:

```text
PaymentTransaction: Pending -> Failed
Order.PaymentStatus: Pending -> Failed
Order.OrderStatus: PendingPayment -> Cancelled
Order.FulfillmentStatus: Unfulfilled -> Cancelled
Shipment: Draft/PendingCreation -> Cancelled
Inventory reservation: Reserved -> Released
```

The Session cart remains available, and its old checkout request ID is reset so
the customer can create a new order.

### Expiration

`VnPayPaymentExpirationWorker` periodically cancels pending VNPay orders that
exceed `PaymentTimeoutMinutes`. It releases inventory using a distinct
idempotency key.

The inventory reservation window is longer than the maximum configured VNPay
timeout. The worker controls the business expiration so a valid callback is not
rejected merely because a timestamp passed while the stock was still reserved.

## Callback authority

- `GET /payments/vnpay/ipn` is the authoritative server-to-server endpoint.
- `GET /payments/vnpay/return` is the customer-facing return endpoint.
- Both endpoints use the same idempotent processor.
- Return URL never marks an order paid without the same signature, reference
  and amount checks used by IPN.
- Browser query values are not written directly into order state.

## Idempotency

- Order creation: scoped `Order.ClientRequestId`.
- Payment creation: `PaymentTransaction.IdempotencyKey`.
- Inventory reservation: checkout request inventory key.
- Inventory release after provider failure: payment reference release key.
- Inventory release after expiration: payment reference expiration key.
- Repeated final VNPay callbacks return the provider “already confirmed”
  response and do not repeat inventory operations.

## Failure recovery

- A payment URL creation failure leaves the order in `PendingPayment` and
  exposes a retry button on the order status page.
- A failed or expired previous checkout request resets its Session request ID
  when the customer revisits checkout.
- A successfully paid existing request clears its purchased cart lines before
  redirecting to the receipt.
- GHN provider errors do not create an order with a guessed fee when GHN is
  enabled.
- When GHN is disabled, checkout cannot create an order until provider
  configuration is restored.
