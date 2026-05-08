; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID    | Category | Severity | Notes
-----------|----------|----------|------------------------------------------------
MOSAIC0001 | Mosaic   | Error    | Request has no handler
MOSAIC0002 | Mosaic   | Error    | Request has multiple handlers
MOSAIC0003 | Mosaic   | Warning  | Composable has no composers
MOSAIC0004 | Mosaic   | Error    | Composers disagree on view-model type
MOSAIC0005 | Mosaic   | Info     | Event has no handlers
MOSAIC0006 | Mosaic   | Warning  | MosaicJsonContext is missing [JsonSerializable] for an IEvent
MOSAIC_SAGA_001 | Mosaic | Error  | Saga has no IStartedBy marker
MOSAIC_SAGA_002 | Mosaic | Error  | Saga marker has no matching Handle method
MOSAIC_SAGA_003 | Mosaic | Error  | Saga correlation property not found on message
MOSAIC_SAGA_004 | Mosaic | Error  | Saga state type doesn't inherit SagaData
MOSAIC_SAGA_005 | Mosaic | Error  | Saga's DbContext could not be resolved from primary constructor
