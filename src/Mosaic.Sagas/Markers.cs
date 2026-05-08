namespace Mosaic.Sagas;

/// <summary>
/// Marker: this saga is *started* by <typeparamref name="TMessage"/>. The first arrival creates a
/// new <see cref="SagaData"/> row; later arrivals correlate to the existing instance (re-entrant
/// start). The user's class must define a matching
/// <c>Handle(TMessage, ICompositionContext, CancellationToken)</c> method.
/// </summary>
public interface IStartedBy<TMessage> where TMessage : IEvent { }

/// <summary>
/// Marker: this saga *handles* (continues on) <typeparamref name="TMessage"/>. The framework looks
/// up the existing saga by correlation; if not found, the message routes to
/// <see cref="IHandleSagaNotFound{TMessage}"/> instead. The user's class must define a matching
/// <c>Handle(TMessage, ICompositionContext, CancellationToken)</c> method.
/// </summary>
public interface IHandles<TMessage> where TMessage : IEvent { }
