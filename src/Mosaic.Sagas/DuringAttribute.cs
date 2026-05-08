namespace Mosaic.Sagas;

/// <summary>
/// Method-scope state guard: the handler only runs when <see cref="SagaData.CurrentState"/> equals
/// one of <see cref="States"/>. Source-gen emits an early-return guard at the top of the wrapped
/// handler call. Reads as <c>[During(OrderProcessState.ProcessingPayment)]</c> on the method.
/// <para>
/// Multiple states allowed: <c>[During(StateA, StateB)]</c> matches if <see cref="SagaData.CurrentState"/>
/// is either. Use the inline <see cref="Saga{TData}.When"/> predicate inside the body for
/// finer-grained branching when one method serves multiple states with different behaviour.
/// </para>
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DuringAttribute : System.Attribute
{
    public DuringAttribute(params string[] states)
    {
        States = states ?? System.Array.Empty<string>();
    }

    public string[] States { get; }
}
