# Return lifecycle

Phase 6 implements a return aggregate that is independent from the internal order
and outbound GHN shipment lifecycles.

## Eligibility

A request is accepted only when:

- `OrderStatus == Completed`
- `FulfillmentStatus == Delivered`
- an outbound shipment has `Status == Delivered`
- `OutboundShipment.DeliveredAt` is present
- current UTC time is no later than `DeliveredAt + 7 days`
- requested quantity does not exceed purchased quantity minus approved
  cancellations and quantities reserved by other active returns

The server is the final source of truth. The storefront cannot override these
rules. A request may attach up to eight validated HTTP/HTTPS image or video
references in `ReturnEvidence`; executable or non-web URI schemes are rejected.

## State machine

```text
Requested
  -> UnderReview
  -> Approved | Rejected
Approved
  -> AwaitingReturnShipment
  -> AwaitingPickup
  -> ReturnInTransit
  -> ReceivedAtWarehouse
  -> Inspecting
  -> RefundPending | RejectedAfterInspection
RefundPending
  -> Refunded -> Closed       (Phase 7)
```

## Reverse GHN shipment

Return transport reuses `Shipment` with:

```text
Direction = Return
ReturnRequestId = current return
ParentShipmentId = delivered outbound shipment
CodAmount = 0
```

A GHN `Delivered` event on a return shipment means that the parcel reached the
FastBuy warehouse. It does not change `Order.FulfillmentStatus`.

Provider calls are made outside the business transaction. The outbox worker
then opens a new serializable transaction to apply the provider result, update
the return aggregate, write timeline data, and complete the outbox message.

## Inventory

Submitting or approving a request never changes sellable stock.

Inventory changes happen in two steps:

1. `ReturnReceived`: zero-delta audit movement after GHN delivered the reverse
   shipment and warehouse staff recorded physical quantities.
2. Inspection disposition:
   - `ReturnRestocked`: increments sellable stock.
   - `ReturnWriteOff`: zero-delta audit movement for accepted units that cannot
     return to sellable stock.

For each received line:

```text
AcceptedQuantity + RejectedQuantity = ReceivedQuantity
RestockQuantity + WriteOffQuantity = AcceptedQuantity
```

Every movement uses an idempotency key tied to the return item.

## Debugging

Return workflows use `CommerceFlowTracker` and `CommerceFlowException`.
Relevant exception data includes:

- ErrorCode
- FlowName
- Stage
- AggregateType / AggregateId
- RequestedAction
- CorrelationId
- IdempotencyKey
- Metadata

In Visual Studio, enable Break on thrown for:

```text
WebApplication2.Services.Commerce.Flows.CommerceFlowException
```

Do not add `Debugger.Break()` to production code.

## Migration process

Phase 6 source delivery intentionally contains no migration or snapshot.
After applying the source changes, generate them with EF Core:

```powershell
Add-Migration AddReturnManagement -Context ApplicationDbContext -OutputDir Migrations
```

Review the generated migration, designer, snapshot, and SQL script before
running `Update-Database`.
