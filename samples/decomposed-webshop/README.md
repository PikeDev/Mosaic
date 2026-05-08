# Decomposed webshop

A small, runnable end-to-end example showing how Mosaic dispatches messages and how a multi-service codebase composes a response from independent slices.

The whole sample runs as a single console process. In a real deployment each "service" project would live in its own assembly in its own repository so cross-service code references are physically prevented at build time; here they live side-by-side under one folder so it builds in one `dotnet run`.

---

## What's demonstrated

Two things at the same time:

1. **The Mosaic message-flow** — a query or command goes through `engine.Send(...)`; the matching handler runs; that handler can take `ICompositionEngine` via constructor injection and call `engine.Compose(...)` to fan out across multiple contributing handlers in different assemblies.
2. **A defensible service decomposition** — each project is responsible for one business capability, owns its own data, and contributes its slice of the response without knowing what the other services do.

Section "Why it's structured this way" below explains the reasoning behind every project boundary.

---

## Service map

| Project | Responsibility | Owns | Contributes |
|---|---|---|---|
| `Mosaic.Sample.Composition` | The edge / API layer | The **shape** of responses (`CheckoutSummaryVm`, `LineItemVm`), top-level queries | The orchestrating handler |
| `Mosaic.Sample.Sales` | Order placement | `OrderId`, `OrderPlaced` event | `PlaceOrder` command |
| `Mosaic.Sample.Catalog` | Product information | Product titles | `LineItemVm.Title` |
| `Mosaic.Sample.Pricing` | Prices | Unit prices | `LineItemVm.UnitPrice` |
| `Mosaic.Sample.Inventory` | Stock levels | Stock counts | `LineItemVm.InStock` / `AvailableUnits`, reacts to `OrderPlaced` |
| `Mosaic.Sample.Marketing` | Customer comms | (no data here) | Reacts to `OrderPlaced` |
| `Mosaic.Sample.Host` | Composition root | DI wiring + simulated HTTP entry point | — |

Note what's deliberately absent:

- **Catalog has no project reference to Pricing or Inventory.** It contributes its slice of the line-item view-model and stops there.
- **Sales has no reference to Inventory or Marketing.** It raises `OrderPlaced` once; the others subscribe in their own assemblies.
- **The view-model `LineItemVm` lives in the Composition project**, not in any service. Each service writes the properties it owns — Catalog touches `Title`, Pricing touches `UnitPrice`, Inventory touches `InStock`/`AvailableUnits`. Everyone writes to the same instance in parallel.

---

## The two flows

### Query flow — `GET /checkout/summary`

```
Program.cs
  └─ engine.Send(GetCheckoutSummary)              [Composition]
        └─ GetCheckoutSummaryHandler.Handle       [Composition]
              └─ for each productId:
                    engine.Compose(GetLineItem)
                          ├─ LineItemCatalogComposer.Compose      [Catalog]    ← parallel
                          ├─ LineItemPricingComposer.Compose      [Pricing]    ← parallel
                          └─ LineItemInventoryComposer.Compose    [Inventory]  ← parallel
              └─ summary.Total = lines.Sum(...)
```

Where to look:

- `Mosaic.Sample.Composition/Queries.cs` — defines `GetCheckoutSummary` (single-handler query) and `GetLineItem` (multi-contributor composable).
- `Mosaic.Sample.Composition/GetCheckoutSummaryHandler.cs` — the orchestrator. Constructor takes `ICompositionEngine engine`; the handler calls `engine.Compose(new GetLineItem(pid))` per product.
- `Mosaic.Sample.{Catalog,Pricing,Inventory}/LineItem*Composer.cs` — each implements `IComposer<GetLineItem, LineItemVm>` and writes only the properties its service owns.

The Mosaic source generator runs in the Host's compilation, walks every transitively-referenced assembly, finds all three composers across three different projects, and emits a dispatcher that resolves them from DI and runs them in parallel via `Task.WhenAll`. There is no registration code, no startup-time scanning by reflection.

### Command flow — `POST /orders`

```
Program.cs
  └─ engine.Send(PlaceOrder)                     [Sales]
        └─ PlaceOrderHandler.Handle              [Sales]
              ├─ Interlocked.Increment(_nextOrderId)
              └─ context.Publish(new OrderPlaced(...))
                       │
                       ▼ (buffered — flushes after handler returns)
                       ├─ OrderPlacedInventoryHandler.Handle   [Inventory]   ← reserves stock
                       └─ OrderPlacedMarketingHandler.Handle   [Marketing]   ← queues email
```

- `Mosaic.Sample.Sales/PlaceOrderHandler.cs` — Sales is the only service that creates orders, so it's the only one that mints `OrderId`s. After the authoritative state change, it publishes `OrderPlaced` via `context.Publish(...)`. With the default `Buffered` event mode, the event flushes after the handler returns — the in-process equivalent of an outbox.
- `Mosaic.Sample.Inventory/OrderPlacedInventoryHandler.cs` and `Mosaic.Sample.Marketing/OrderPlacedMarketingHandler.cs` — independent subscribers in independent assemblies. Sales has zero coupling to either. Adding a third subscriber tomorrow is a one-class change in a new assembly with no edit to Sales.

---

## Why it's structured this way

The decomposition above isn't accidental. Each rule is chosen to keep **one business change touching exactly one project**.

### Each service is the only writer of its data

Pricing is the only project that writes `UnitPrice`. If "price" later evolves — picks up a currency, then later a tax-jurisdiction modifier, then later a crypto-vs-fiat distinction — the change lands in Pricing and nowhere else. If Catalog or Sales were also allowed to write prices, every such change would ripple across multiple projects and you'd never be sure you'd caught all the call sites.

The same logic goes the other way: a project whose only role is "wrap a database table in a few methods" isn't a service in this sense — it has no business capability of its own. And a project that has only stateless functions (calculations, validations) isn't a service either; it's a function library.

### The response shape lives at the edge, not in any service

`CheckoutSummaryVm` and `LineItemVm` live in `Composition`, not in Catalog or Pricing. Which fields appear on the "checkout summary" is an API contract for the client; it has nothing to do with the business capability of pricing or stock-keeping. Forcing one service to own the shape would make that service reach into the others (or, worse, replicate their data) just to populate the response.

The trade-off: no individual service "owns the page" — and the temptation to bundle everything that visually appears together into one team disappears.

### No top-level orchestrator drives the services

`GetCheckoutSummaryHandler` is allowed to compose, but it doesn't tell Catalog/Pricing/Inventory **what to do** — it just asks each "what's your slice of this view-model?" and hands the result back. The composers run in parallel; their order is irrelevant.

This matters at scale. A workflow-style central orchestrator becomes the place every change has to pass through, and every service ends up being a thin RPC wrapper around its data. The composition-layer-as-orchestrator approach keeps the control flow narrow (one fan-out per response) and the services full-bodied (full ownership of their capability).

### Identifiers cross service boundaries; business data does not

`GetLineItem(productId)` carries an `int productId` and nothing else. Each composer fetches what it needs from its own store using that ID. This works because identifiers are stable concepts — a `ProductId` is a `ProductId` forever — while business data evolves constantly. If `GetLineItem` carried a `Product` object with name and price baked in, every business change to "what is a product?" would force every composer to update.

The same applies to events. `OrderPlaced` carries `OrderId`, `CustomerId`, and `IReadOnlyList<int> ProductIds` — IDs only. Inventory and Marketing fetch whatever else they need from their own stores. The event doesn't carry the order's price total, the customer's email, or the product names.

### Sales doesn't call Inventory; Inventory reacts to Sales

If Sales had a project reference to Inventory and called `inventory.Reserve(productId)` directly:

- Inventory being unavailable would also break Sales.
- Adding Marketing as a third reactor would require an edit to Sales.
- The two services would deploy and version together, defeating the point of separation.

Raising an event keeps Sales' job to one thing: "create the authoritative order record". Whoever reacts to the event reacts on their own schedule, in their own assembly, with their own failure modes. In a multi-process deployment the event would land on a durable queue (RabbitMQ, Service Bus, etc.); in this in-process sample it's the buffered flush after the handler returns. The application code is the same either way.

### The composer fan-out works because each writer touches different fields

When Catalog writes `Title`, Pricing writes `UnitPrice`, and Inventory writes `InStock`/`AvailableUnits`, the parallel writes can't conflict — each composer touches a different property of the shared VM instance. That's what makes `Task.WhenAll(composers)` safe without locks.

If two services tried to write the same field, that field belongs to neither of them — it belongs to a third service that hasn't been carved out yet.

---

## Running it

```
dotnet run --project samples/decomposed-webshop/Mosaic.Sample.Host
```

Expected output:

```
══════ GET /checkout/summary?customerId=1&products=42,17,99 ══════

Customer #1
  product #42   Acme Widget          € 19,99   in-stock=True  (12 units)
  product #17   Premium Gizmo        € 89,50   in-stock=True  (3 units)
  product #99   Bulk Sprocket         € 4,25   in-stock=False (0 units)
  TOTAL: € 113,74

══════ POST /orders { customerId: 1, products: [42, 17] } ══════
[sales]      order #1001 created for customer 1
[inventory]  reserved 1 unit of product 42
[inventory]  reserved 1 unit of product 17
[marketing]  order confirmation queued for customer 1
  → 201 Created, order #1001
```

Two things in the output worth pointing at:

- The query result interleaves data from three different assemblies (Catalog/Pricing/Inventory) into one VM, with no orchestration code beyond the per-line `engine.Compose` call.
- In the command block, the `[sales]` line prints first, then `[inventory]` and `[marketing]` print **before** the Host's `→ 201 Created` line. That's the buffered event flush in action: `Publish` queued the event during the handler; the engine flushed it after the handler returned but before `Send` returned to the caller.
