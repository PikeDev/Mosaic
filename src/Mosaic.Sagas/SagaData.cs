namespace Mosaic.Sagas;

/// <summary>
/// Base class for saga state entities. Holds the framework's lifecycle fields plus a string-typed
/// <see cref="CurrentState"/> the user transitions via <see cref="Saga{TData}.TransitionTo"/>.
/// <para>
/// Inherit, add domain fields, mark the correlation property with <see cref="CorrelationAttribute"/>:
/// <code>
/// public sealed class OrderProcessData : SagaData
/// {
///     [Correlation]
///     public Guid OrderId { get; set; }
///     public decimal OrderTotal { get; set; }
/// }
/// </code>
/// </para>
/// </summary>
public abstract class SagaData
{
    /// <summary>Primary key of the saga state row. Source-gen seeds this on saga start.</summary>
    public System.Guid Id { get; set; }

    /// <summary>String-typed current state. Use <c>nameof</c> constants on a <c>static class</c> for typo safety.</summary>
    public string CurrentState { get; set; } = "";

    /// <summary>Set true when the saga calls <see cref="Saga{TData}.Complete"/>; the row is then deleted (or stamped, by policy) on save.</summary>
    public bool IsCompleted { get; set; }

    /// <summary>Stamped when <see cref="Saga{TData}.Complete"/> runs.</summary>
    public System.DateTime? CompletedAt { get; set; }
}
