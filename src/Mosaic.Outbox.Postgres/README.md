# Mosaic.Outbox.Postgres

Standalone Postgres outbox for [Mosaic](https://github.com/pikedev/mosaic) — for consumers that don't use EF Core for state. Writes published events to a `mosaic_outbox` table; a relay hosted service polls the table and ships rows via the configured `IOutboxShipper` (typically `Mosaic.Transport.Postgres`'s shipper, which uses `pg_notify`).

```csharp
services.AddMosaic()
    .UsePostgresTransport(connStr)
    .UsePostgresOutbox();
```

Trade-off vs `Mosaic.Outbox.EFCore`: this package writes the outbox row in its own transaction, **after** the consumer's commit. A process crash in that window can lose the event. For full atomicity with the consumer's own DbContext save, use `Mosaic.Outbox.EFCore.UseEFCoreOutbox<TDbContext>()` instead — it stages the row in the consumer's change tracker so it commits in the same transaction as state.

Use this package when:
- The consumer doesn't own a `DbContext` (worker that talks to Postgres directly via Npgsql, calls a SQL stored proc, etc.).
- You're OK with the small "saved state but didn't write outbox" window (it's much smaller than the fully decoupled "publish-then-pray" alternative).

The relay marks rows sent only after the shipper returns successfully. Failed shipments stay un-marked and retry on the next poll. Receivers dedup via the inbox keyed on the wire `MessageId`.
