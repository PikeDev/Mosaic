using Mosaic.SourceGenerator.Helpers;

namespace Mosaic.SourceGenerator.Analysis;

/// <summary>
/// All information the emitters need to produce the engine + DI extension. Everything is reduced
/// to value-equatable strings and records so the incremental generator can cache it.
/// </summary>
internal sealed record HandlerCatalog(
    string GeneratedNamespace,
    string EventPublishMode,           // "Buffered" | "Eager"
    bool EmitTelemetry,
    bool EFCoreSagaStateStoreAvailable,  // true when Mosaic.Sagas.EFCore is referenced — enables saga-state auto-registration for sagas with a DbContext-typed ctor parameter
    EquatableArray<RequestDispatch> Requests,
    EquatableArray<ComposableDispatch> Composables,
    EquatableArray<EventDispatch> Events,
    EquatableArray<HandlerRegistration> Registrations,
    EquatableArray<SagaDispatch> Sagas);

/// <summary>One <c>case</c> in the generated <c>Send</c> switch.</summary>
internal sealed record RequestDispatch(
    string RequestFullName,        // e.g. "global::MyApp.PlaceOrder"
    string ResponseFullName,       // e.g. "global::MyApp.PlaceOrderResult"
    string HandlerFullName,        // e.g. "global::MyApp.PlaceOrderHandler"
    string MethodSafeName,         // e.g. "PlaceOrder" — used to name the per-request private dispatcher method
    EquatableArray<string> BehaviorClosedFullNames) // outer-to-inner; closed for this (req,resp) pair
    : IEquatable<RequestDispatch>;

/// <summary>One <c>case</c> in the generated <c>Compose</c> switch.</summary>
internal sealed record ComposableDispatch(
    string RequestFullName,
    string ViewModelFullName,
    EquatableArray<string> ComposerFullNames,
    string MethodSafeName,
    EquatableArray<string> BehaviorClosedFullNames)  // outer-to-inner; closed for this (req,vm) pair
    : IEquatable<ComposableDispatch>;

/// <summary>One <c>case</c> in the generated <c>Publish</c> switch.</summary>
internal sealed record EventDispatch(
    string EventFullName,
    EquatableArray<string> HandlerFullNames,
    string MethodSafeName,
    EquatableArray<string> BehaviorClosedFullNames)  // outer-to-inner; closed for this event
    : IEquatable<EventDispatch>;

/// <summary>One DI registration emitted into <c>AddMosaic()</c>.</summary>
internal sealed record HandlerRegistration(
    string ServiceTypeFullName,    // e.g. "global::Mosaic.IRequestHandler<global::MyApp.PlaceOrder, global::MyApp.PlaceOrderResult>"
                                   // or for composers/event-handlers: empty (registered as concrete type only)
    string ImplementationFullName,
    string Lifetime)               // "Singleton" | "Scoped" | "Transient"
    : IEquatable<HandlerRegistration>;

/// <summary>One discovered saga (Saga&lt;TData&gt; + marker interfaces). Source-gen emits a
/// per-message <c>IEventHandler&lt;TMessage&gt;</c> wrapper for each declared marker, plus DI registrations.</summary>
internal sealed record SagaDispatch(
    string SagaFullName,                          // e.g. "global::Webshop.Billing.Sagas.PaymentAuthorizationSaga"
    string SagaSimpleName,                        // e.g. "PaymentAuthorizationSaga" — used to name the wrapper class
    string DataFullName,                          // "global::Webshop.Billing.Domain.PaymentAuthorizationData"
    string DbContextFullName,                     // "global::Webshop.Billing.Persistence.BillingDbContext"
    string CorrelationPropertyName,               // "OrderId"
    string CorrelationPropertyTypeFullName,       // "global::Webshop.Identifiers.OrderId" or "global::System.Guid"
    EquatableArray<SagaMessage> Messages)
    : IEquatable<SagaDispatch>;

/// <summary>One declared <see cref="Mosaic.Sagas.IStartedBy{T}"/> or <c>IHandles&lt;T&gt;</c> on a saga.</summary>
internal sealed record SagaMessage(
    string MessageFullName,
    string MessageSimpleName,                     // disambiguated suffix for the wrapper class name
    bool IsStarter,                               // IStartedBy vs IHandles
    string CorrelationAccessorOnMessage,          // "OrderId" — property on the message; "" when HasCorrelateByPartial
    bool HasCorrelateByPartial,                   // user wrote `static partial Guid CorrelateBy(TMessage)` etc
    EquatableArray<string> DuringStates)          // empty = no [During] guard
    : IEquatable<SagaMessage>;
