# Commerce lifecycle boundaries

FastBuy treats order, outbound shipping, return and cancellation as separate
workflows. A status in one workflow must never be used as a shortcut to mutate
another workflow.

## 1. Internal order lifecycle

```text
PendingPayment -> Placed -> Confirmed -> Processing
Processing --(GHN outbound Delivered + Payment Paid)--> Completed
Completed -> Closed
```

Rules:

- Admin may move the order only through the preparation steps.
- `Completed` is written by the outbound shipment aggregate updater.
- COD becomes `Paid` only when the GHN outbound shipment is delivered.
- `Cancelled` is written only by the cancellation workflow.
- Return and refund workflows may block `Closed` in later phases.

## 2. GHN outbound shipment lifecycle

```text
Draft -> PendingCreation -> Created -> Picking -> InTransit -> Delivered
```

Exceptional branches:

```text
Draft/PendingCreation/Created -> CancelRequested -> Cancelled
Picking/InTransit -> DeliveryFailed
DeliveryFailed -> InTransit
DeliveryFailed/InTransit -> Returning -> Returned
Any non-terminal provider state -> Exception
```

Provider states are applied only by:

- `ShippingOutboxWorker`, after a successful provider command; or
- `GhnShippingWebhookProcessor`; or
- explicit provider reconciliation in `ShippingExecutionService.SyncAsync`.

Admin order status forms cannot set `Shipped`, `Delivered` or
`DeliveryFailed`.

`CarrierHandoffAt` is set on the first provider `Picking` or `InTransit`
event. `DeliveredAt` is set only by a provider-confirmed `Delivered` event.

## 3. Return lifecycle foundation

Phase 5 adds shipment metadata needed by Phase 6:

```text
Shipment.Direction = Outbound | Return
Return shipment.ParentShipmentId = delivered outbound shipment
```

Phase 6 must enforce:

```text
OutboundShipment.Status == Delivered
OutboundShipment.DeliveredAt != null
UtcNow <= DeliveredAt + 7 days
```

A return shipment `Delivered` event means the parcel reached the FastBuy
warehouse. It must not complete the customer order again.

## 4. Conditional cancellation workflow

Cancellation is outside the normal order state machine.

- A request may be created before carrier handoff.
- A local draft shipment can be cancelled immediately.
- If GHN already created the shipment, cancellation approval and stock
  compensation must wait until GHN confirms `Cancelled`.
- After `CarrierHandoffAt`, `Picking` or `InTransit`, cancellation is rejected
  with `CANCELLATION_AFTER_CARRIER_HANDOFF`.
- After delivery, the customer must use the return workflow.

Partial cancellation cancels the old outbound shipment first, then creates a
new outbound draft for the remaining order lines.

## 5. Transaction and provider boundary

Provider calls never run inside a database transaction.

```text
Transaction A
  validate aggregate and RowVersion
  apply internal state
  write timeline and outbox
  commit

Provider call
  worker calls GHN

Transaction B
  reload aggregate
  validate provider event order
  apply shipment/order/payment changes
  complete outbox or inbox
  commit
```

This prevents a GHN timeout from holding locks or rolling back an already
accepted internal command.

## 6. Debugging commerce flows

All complex flows use `CommerceFlowTracker`. Exceptions expose:

- `ErrorCode`
- `FlowContext.FlowName`
- `FlowContext.Stage`
- `AggregateType` and `AggregateId`
- `CurrentState`
- `RequestedAction`
- `CorrelationId`
- `IdempotencyKey`
- metadata and inner exception

In Visual Studio, enable **Break on thrown** for:

```text
WebApplication2.Services.Commerce.Flows.CommerceFlowException
```

Do not add `Debugger.Break()` to production code. The exception stage identifies
the exact checkpoint, such as `ValidateBusinessRules`, `WriteInventoryLedger`,
`CallProvider`, `ApplyProviderResponse`, `SaveChanges` or `Commit`.
