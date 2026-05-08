using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Mosaic.Tests.Behaviors;
using Shouldly;
using Xunit;

namespace Mosaic.Tests.Behaviors;

public sealed record CalcRequest(int Value) : IRequest<int>;

public sealed class CalcHandler : IRequestHandler<CalcRequest, int>
{
    public ValueTask<int> Handle(CalcRequest request, ICompositionContext context, CancellationToken cancellationToken)
    {
        BehaviorTrace.Add("handler:run");
        return new(request.Value * 2);
    }
}

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> nextHandler, ICompositionContext context, CancellationToken cancellationToken)
    {
        // Only trace when the request is the one this test owns — these behaviors wrap every
        // IRequest in the assembly (LoggingBehavior<,> is open-generic), and xUnit runs test
        // classes in parallel, so traces from other tests would interleave into the static queue.
        var isOurs = request is CalcRequest;
        if (isOurs) BehaviorTrace.Add("logging:before");
        var result = await nextHandler();
        if (isOurs) BehaviorTrace.Add("logging:after");
        return result;
    }
}

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> nextHandler, ICompositionContext context, CancellationToken cancellationToken)
    {
        var isOurs = request is CalcRequest;
        if (isOurs) BehaviorTrace.Add("validation:before");
        var result = await nextHandler();
        if (isOurs) BehaviorTrace.Add("validation:after");
        return result;
    }
}

public static class BehaviorTrace
{
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _events = new();
    public static void Add(string e) => _events.Enqueue(e);
    public static void Clear() => _events.Clear();
    public static IReadOnlyList<string> Snapshot() => [.. _events];
}

public class PipelineBehaviorTests
{
    private static readonly string[] ExpectedTrace =
    [
        "logging:before",
        "validation:before",
        "handler:run",
        "validation:after",
        "logging:after",
    ];

    [Fact]
    public async Task Behaviors_wrap_handler_outermost_to_innermost()
    {
        BehaviorTrace.Clear();
        var services = new ServiceCollection();
        services.AddMosaic();
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();

        var result = await engine.Send(new CalcRequest(7));

        result.ShouldBe(14);
        BehaviorTrace.Snapshot().ShouldBe(ExpectedTrace);
    }
}
