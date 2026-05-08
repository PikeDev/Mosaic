namespace Mosaic.Sagas;

/// <summary>
/// Marks the property on a <see cref="SagaData"/>-derived class that is the saga's correlation
/// key. The Mosaic source generator uses convention to wire each handled message type to this
/// property (matching by name + compatible type); messages whose property name differs can opt in
/// to an explicit override via a <c>static partial Guid CorrelateBy(TMessage m) =&gt; …</c> method
/// on the saga class.
/// <para>
/// Typically only one property is marked. If none is marked, source-gen falls back to
/// <see cref="SagaData.Id"/>.
/// </para>
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CorrelationAttribute : System.Attribute { }
