# Mosaic.Serialization.MemoryPack

[MemoryPack](https://github.com/Cysharp/MemoryPack) adapter for [Mosaic](https://github.com/pikedev/mosaic). Swaps the default reflection-based System.Text.Json registry for the binary MemoryPack format on the wire.

```csharp
[MemoryPackable]
public partial record OrderPlaced(int OrderId, string Sku) : IEvent;

services.AddSingleton<IMosaicSerializerRegistry, MemoryPackMosaicSerializerRegistry>();
services.AddMosaic().UsePostgresTransport(connStr);
```

Useful when JSON parsing is a measurable bottleneck on the publish path or when you want an AOT-clean serializer without declaring `[JsonSerializable]` per type.

Caller responsibility: every event type must be annotated `[MemoryPackable]` (and declared `partial`) so MemoryPack's source generator can emit the formatter at compile time. Types without the annotation throw at runtime when first dispatched — fall back to the JSON registry for those if the cost matters less than the ergonomics.

Register the registry **before** `AddMosaic()` so the source-gen registration's `TryAddSingleton` for the JSON default is a no-op.
