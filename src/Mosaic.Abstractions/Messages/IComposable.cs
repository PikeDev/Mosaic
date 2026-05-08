namespace Mosaic;

/// <summary>
/// Marks a message that is dispatched to <b>many</b> <see cref="IComposer{TRequest, TViewModel}"/> implementations,
/// each contributing a slice of a shared, typed <typeparamref name="TViewModel"/>.
/// </summary>
/// <typeparam name="TViewModel">
/// The view-model the composers contribute to. Must have a parameterless constructor when invoked via
/// the allocating <c>Compose&lt;TVm&gt;(IComposable&lt;TVm&gt;)</c> overload; the
/// <c>Compose&lt;TVm&gt;(IComposable&lt;TVm&gt;, TVm)</c> overload accepts a pre-populated instance.
/// </typeparam>
/// <remarks>
/// Composers run in parallel by default, so the contract is that each composer mutates only its own
/// section of the view-model. The convention is one sub-object per service on the parent VM
/// (e.g. <c>vm.Catalog</c>, <c>vm.Price</c>, <c>vm.Availability</c>); ownership of a sub-object can
/// be declared with <see cref="OwnedByAttribute"/>.
/// </remarks>
public interface IComposable<TViewModel>;
