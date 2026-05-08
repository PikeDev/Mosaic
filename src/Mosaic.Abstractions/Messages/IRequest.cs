namespace Mosaic;

/// <summary>
/// Marks a message that is dispatched to <b>exactly one</b> <see cref="IRequestHandler{TRequest, TResponse}"/>
/// and produces a <typeparamref name="TResponse"/>. Mediator-style send.
/// </summary>
/// <typeparam name="TResponse">The type returned by the single handler.</typeparam>
/// <remarks>
/// The Mosaic source generator validates at compile time that exactly one handler exists
/// for each <c>IRequest&lt;TResponse&gt;</c> implementation. Zero or two-or-more produces a
/// diagnostic (<c>MOSAIC0001</c> / <c>MOSAIC0002</c>).
/// </remarks>
public interface IRequest<out TResponse>;
