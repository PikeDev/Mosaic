# Mosaic.SourceGenerator

Roslyn `IIncrementalGenerator` for the [Mosaic](https://github.com/pikedev/mosaic) typed composition + mediator framework. Emits the dispatcher (`GeneratedCompositionEngine`), DI registrations (`AddMosaic()`), and saga wrappers in your composition root.

**Reference this only in the composition root, with `PrivateAssets="all"`** — handlers and message-defining projects only need `Mosaic.Abstractions`. Most consumers reference the meta package `Mosaic`, which already wires the generator correctly.

```xml
<ProjectReference Include="..\..\src\Mosaic.SourceGenerator\Mosaic.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

The generator walks the consumer's compilation plus every transitively-referenced assembly that depends on `Mosaic.Abstractions`, classifies discovered types by which marker interface they implement, validates cardinality (one handler per request, etc.), and emits source. No reflection at startup, no startup-time scanning, AOT/trim-friendly.

Diagnostics: MOSAIC0001 (request without handler), MOSAIC0002 (multiple handlers), MOSAIC0003 (composable without composers), MOSAIC0004 (composers disagree on view-model), MOSAIC0005 (event without handlers), MOSAIC0006 (`[MosaicJsonContext]` missing `[JsonSerializable]` for a known event), plus saga-specific diagnostics under MOSAIC_SAGA_001..005.
