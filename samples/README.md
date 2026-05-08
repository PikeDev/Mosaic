# Mosaic samples

Three runnable samples, each focused on one slice of what Mosaic does. Each folder has its own README explaining the wiring and the design choices behind it.

| Sample | Demonstrates |
|---|---|
| [`decomposed-webshop/`](decomposed-webshop) | Send + Compose + Publish across multiple service assemblies. The biggest sample — a small webshop with seven projects (one per business capability) showing how a checkout summary is composed from independent slices and how `OrderPlaced` cascades into separate subscribers without coupling Sales to either of them. |
| [`saga-timeout/`](saga-timeout) | The buyer's-remorse saga shape: started by an event, schedules a self-timeout, absorbs a competing cancel during the hold window, only emits the downstream effect if the timeout actually fires. Single console process, in-memory state. |
| [`audit-trail/`](audit-trail) | Every Mosaic message carries `MessageId` / `CorrelationId` / `CausationId`. This sample drives a three-link cascade and renders the resulting correlation graph as an indented tree, queryable by `CorrelationId`. |

Run any sample with:

```
dotnet run --project samples/<sample-folder>
```

Each sample's README explains *what's stand-in vs production* — the in-memory infrastructure used in the samples (saga state stores, schedulers, audit stores) is replaced in real deployments by the EF Core / Postgres adapter packages. The handler/saga code itself is identical between sample and production.
