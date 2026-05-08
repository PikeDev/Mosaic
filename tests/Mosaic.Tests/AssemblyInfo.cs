using Mosaic;
using Mosaic.Tests.Behaviors;
using Mosaic.Testing;

// One assembly-level CompositionConfiguration covers every fixture in this test project: the
// pipeline-behavior tests' Logging + Validation, plus the test-harness recording behaviors.
[assembly: CompositionConfiguration(
    PipelineBehaviors = new[]
    {
        typeof(LoggingBehavior<,>),
        typeof(ValidationBehavior<,>),
        typeof(RecordingPipelineBehavior<,>),
    },
    PublishBehaviors = new[] { typeof(RecordingPublishBehavior<>) },
    ComposeBehaviors = new[] { typeof(RecordingComposeBehavior<,>) })]
