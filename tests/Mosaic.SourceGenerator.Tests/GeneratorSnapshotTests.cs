using VerifyXunit;
using Xunit;

namespace Mosaic.SourceGenerator.Tests;

public class GeneratorSnapshotTests
{
    [Fact]
    public Task Single_request_with_handler()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            namespace Sample;

            public sealed record GetGreeting(string Name) : IRequest<string>;

            public sealed class GreetingHandler : IRequestHandler<GetGreeting, string>
            {
                public ValueTask<string> Handle(GetGreeting request, ICompositionContext ctx, CancellationToken ct)
                    => new($"Hello, {request.Name}");
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task Composable_with_two_composers()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            namespace Sample;

            public sealed class OrderVm { public int OrderId { get; set; } public decimal Total { get; set; } }
            public sealed record GetOrder(int OrderId) : IComposable<OrderVm>;

            public sealed class TotalsComposer : IComposer<GetOrder, OrderVm>
            {
                public ValueTask Compose(GetOrder r, OrderVm vm, ICompositionContext ctx, CancellationToken ct) { vm.Total = 1m; return default; }
            }
            public sealed class IdComposer : IComposer<GetOrder, OrderVm>
            {
                public ValueTask Compose(GetOrder r, OrderVm vm, ICompositionContext ctx, CancellationToken ct) { vm.OrderId = r.OrderId; return default; }
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task Event_with_two_handlers()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            namespace Sample;

            public sealed record OrderPlaced(int OrderId) : IEvent;

            public sealed class Auditor : IEventHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced e, ICompositionContext ctx, CancellationToken ct) => default;
            }
            public sealed class Notifier : IEventHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced e, ICompositionContext ctx, CancellationToken ct) => default;
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task Lifetime_attribute_changes_registration()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;
            using Microsoft.Extensions.DependencyInjection;

            namespace Sample;

            public sealed record Ping() : IRequest<string>;

            [Lifetime(ServiceLifetime.Transient)]
            public sealed class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping r, ICompositionContext ctx, CancellationToken ct) => new("pong");
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task Open_generic_pipeline_behaviors_wrap_request_dispatch()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            [assembly: CompositionConfiguration(PipelineBehaviors = new[] {
                typeof(Sample.LoggingBehavior<,>),
                typeof(Sample.ValidationBehavior<,>)
            })]

            namespace Sample;

            public sealed record DoThing(int X) : IRequest<int>;
            public sealed class DoThingHandler : IRequestHandler<DoThing, int>
            {
                public ValueTask<int> Handle(DoThing r, ICompositionContext ctx, CancellationToken ct) => new(r.X);
            }

            public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
            {
                public ValueTask<TResponse> Handle(TRequest req, RequestHandlerDelegate<TResponse> next, ICompositionContext ctx, CancellationToken ct) => next();
            }
            public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
            {
                public ValueTask<TResponse> Handle(TRequest req, RequestHandlerDelegate<TResponse> next, ICompositionContext ctx, CancellationToken ct) => next();
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task Configuration_attribute_settings_apply_to_emission()
    {
        // Set every CompositionConfiguration knob to a non-default value and confirm the
        // generated engine reflects all of them.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;
            using Microsoft.Extensions.DependencyInjection;

            [assembly: CompositionConfiguration(
                DefaultLifetime = ServiceLifetime.Singleton,
                EventPublishMode = EventPublishMode.Eager,
                EmitTelemetry = false,
                GeneratedNamespace = "MyApp.MosaicGen")]

            namespace Sample;

            public sealed record Ping() : IRequest<int>;
            public sealed class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping r, ICompositionContext ctx, CancellationToken ct) => new(1);
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task Nullable_reference_response_preserves_annotation()
    {
        // Compiled with <Nullable>enable</Nullable>, the response type is `User?`. The generator
        // must carry that annotation through to the inner dispatcher signature so consumer code
        // that does `var u = await engine.Send(new FindUser(...))` gets a `User?` back, not a
        // non-nullable `User` that silently swallows null.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            namespace Sample;

            public sealed class User { public string Name { get; set; } = ""; }
            public sealed record FindUser(int Id) : IRequest<User?>;

            public sealed class FindUserHandler : IRequestHandler<FindUser, User?>
            {
                public ValueTask<User?> Handle(FindUser r, ICompositionContext ctx, CancellationToken ct)
                    => new((User?)null);
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source, nullable: true));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task Open_generic_publish_behaviors_wrap_event_dispatch()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            [assembly: CompositionConfiguration(PublishBehaviors = new[] {
                typeof(Sample.PublishLoggingBehavior<>),
                typeof(Sample.PublishMetricsBehavior<>)
            })]

            namespace Sample;

            public sealed record OrderPlaced(int OrderId) : IEvent;
            public sealed class Auditor : IEventHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced e, ICompositionContext c, CancellationToken t) => default;
            }

            public sealed class PublishLoggingBehavior<TEvent> : IPublishBehavior<TEvent> where TEvent : IEvent
            {
                public ValueTask Handle(TEvent n, PublishHandlerDelegate next, ICompositionContext c, CancellationToken t) => next();
            }
            public sealed class PublishMetricsBehavior<TEvent> : IPublishBehavior<TEvent> where TEvent : IEvent
            {
                public ValueTask Handle(TEvent n, PublishHandlerDelegate next, ICompositionContext c, CancellationToken t) => next();
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task Open_generic_compose_behaviors_wrap_compose_dispatch()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            [assembly: CompositionConfiguration(ComposeBehaviors = new[] {
                typeof(Sample.ComposeLoggingBehavior<,>)
            })]

            namespace Sample;

            public sealed class TileVm { public int ProductId { get; set; } }
            public sealed record GetTile(int ProductId) : IComposable<TileVm>;

            public sealed class IdComposer : IComposer<GetTile, TileVm>
            {
                public ValueTask Compose(GetTile r, TileVm vm, ICompositionContext c, CancellationToken t) { vm.ProductId = r.ProductId; return default; }
            }

            public sealed class ComposeLoggingBehavior<TRequest, TViewModel> : IComposeBehavior<TRequest, TViewModel>
                where TRequest : IComposable<TViewModel>
            {
                public ValueTask Handle(TRequest r, TViewModel vm, ComposeHandlerDelegate next, ICompositionContext c, CancellationToken t) => next();
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task Multiple_handlers_for_same_request_emits_diagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            namespace Sample;

            public sealed record Q() : IRequest<int>;
            public sealed class A : IRequestHandler<Q, int> { public ValueTask<int> Handle(Q r, ICompositionContext c, CancellationToken t) => new(1); }
            public sealed class B : IRequestHandler<Q, int> { public ValueTask<int> Handle(Q r, ICompositionContext c, CancellationToken t) => new(2); }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task MosaicJsonContext_missing_event_emits_MOSAIC0006()
    {
        // Two events in the catalog; the JsonContext only declares one — the missing one should
        // produce a MOSAIC0006 warning on the context class.
        const string source = """
            using System.Text.Json.Serialization;
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            namespace Sample;

            public sealed record OrderPlaced(int OrderId) : IEvent;
            public sealed record OrderAccepted(int OrderId) : IEvent;
            public sealed class OrderPlacedHandler : IEventHandler<OrderPlaced> { public ValueTask Handle(OrderPlaced e, ICompositionContext c, CancellationToken t) => default; }
            public sealed class OrderAcceptedHandler : IEventHandler<OrderAccepted> { public ValueTask Handle(OrderAccepted e, ICompositionContext c, CancellationToken t) => default; }

            [MosaicJsonContext]
            [JsonSerializable(typeof(OrderPlaced))]
            internal sealed class WebshopJsonContext : JsonSerializerContext
            {
                // Stubs only — the real S.T.Json source generator would emit these. The analyzer
                // looks at [JsonSerializable] attributes on the class, not at the typeinfo body,
                // so stubs are fine for testing the diagnostic path.
                public WebshopJsonContext() : base(null) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task MosaicJsonContext_with_full_coverage_produces_no_MOSAIC0006()
    {
        // All events declared on the context — no MOSAIC0006 in the diagnostics.
        const string source = """
            using System.Text.Json.Serialization;
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            namespace Sample;

            public sealed record OrderPlaced(int OrderId) : IEvent;
            public sealed record OrderAccepted(int OrderId) : IEvent;
            public sealed class OrderPlacedHandler : IEventHandler<OrderPlaced> { public ValueTask Handle(OrderPlaced e, ICompositionContext c, CancellationToken t) => default; }
            public sealed class OrderAcceptedHandler : IEventHandler<OrderAccepted> { public ValueTask Handle(OrderAccepted e, ICompositionContext c, CancellationToken t) => default; }

            [MosaicJsonContext]
            [JsonSerializable(typeof(OrderPlaced))]
            [JsonSerializable(typeof(OrderAccepted))]
            internal sealed class WebshopJsonContext : JsonSerializerContext
            {
                public WebshopJsonContext() : base(null) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }

    [Fact]
    public Task JsonSerializerContext_without_MosaicJsonContext_attribute_is_ignored()
    {
        // No [MosaicJsonContext] — analyzer is opt-in. A missing event must not trigger MOSAIC0006.
        const string source = """
            using System.Text.Json.Serialization;
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            namespace Sample;

            public sealed record OrderPlaced(int OrderId) : IEvent;
            public sealed class OrderPlacedHandler : IEventHandler<OrderPlaced> { public ValueTask Handle(OrderPlaced e, ICompositionContext c, CancellationToken t) => default; }

            // No [MosaicJsonContext] — completely unrelated to Mosaic, should be left alone.
            [JsonSerializable(typeof(string))]
            internal sealed class UnrelatedContext : JsonSerializerContext
            {
                public UnrelatedContext() : base(null) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
            }
            """;

        var driver = TestCompilationFactory.RunGenerator(TestCompilationFactory.Create(source));
        return Verifier.Verify(driver);
    }
}
