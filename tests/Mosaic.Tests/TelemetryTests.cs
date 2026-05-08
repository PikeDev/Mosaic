using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Mosaic.Runtime;
using Mosaic.Tests.Telemetry;
using Shouldly;
using Xunit;

namespace Mosaic.Tests.Telemetry;

public sealed record TracedRequest(string Tag) : IRequest<int>;

public sealed class TracedHandler : IRequestHandler<TracedRequest, int>
{
    public ValueTask<int> Handle(TracedRequest request, ICompositionContext context, CancellationToken cancellationToken)
        => new(42);
}

public class TelemetryTests
{
    [Fact]
    public async Task Send_starts_an_Activity_with_well_known_tags()
    {
        // Filter to only this test's request — xUnit runs test classes in parallel, so other
        // tests' spans would otherwise land in this listener too.
        var startedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MosaicActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            // ActivityStopped fires after the using-block disposes — by then all tags are set.
            // ActivityStarted fires before the SetTag calls and would see no tags.
            ActivityStopped = a =>
            {
                if (a.GetTagItem("mosaic.message.type") is string t
                    && t == typeof(TracedRequest).FullName)
                {
                    lock (startedActivities) startedActivities.Add(a);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var services = new ServiceCollection();
        services.AddMosaic();
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();

        await engine.Send(new TracedRequest("hello"));

        Activity sendActivity;
        lock (startedActivities) { sendActivity = startedActivities.ShouldHaveSingleItem(); }
        sendActivity.OperationName.ShouldStartWith("Mosaic.Send ");
        sendActivity.GetTagItem("mosaic.message.kind").ShouldBe("request");
        sendActivity.GetTagItem("mosaic.message.type").ShouldBe("Mosaic.Tests.Telemetry.TracedRequest");
        sendActivity.GetTagItem("mosaic.handler.type").ShouldBe("Mosaic.Tests.Telemetry.TracedHandler");
        sendActivity.GetTagItem("mosaic.correlation_id").ShouldNotBeNull();
    }
}
