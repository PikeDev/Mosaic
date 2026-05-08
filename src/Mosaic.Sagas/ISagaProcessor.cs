namespace Mosaic.Sagas;

/// <summary>
/// Non-generic processor surface the host iterates. <see cref="SagaProcessor{TState}"/> is the
/// usual base class; this interface exists so the host can keep them in one collection without
/// caring about each saga's state type.
/// </summary>
public interface ISagaProcessor
{
    /// <summary>How often the host should call <see cref="ProcessOnceAsync"/> for this saga.</summary>
    System.TimeSpan PollInterval { get; }

    /// <summary>Display name used in log lines. Defaults to the implementing type's name.</summary>
    string Name { get; }

    /// <summary>Run one full poll cycle: find active state rows + drive each forward.</summary>
    System.Threading.Tasks.Task ProcessOnceAsync(
        System.IServiceProvider scope,
        System.Threading.CancellationToken cancellationToken);
}
