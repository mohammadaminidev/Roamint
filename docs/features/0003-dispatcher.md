# 0003 — In-process dispatcher (CQS + events)

**Status:** Done
**Lands in:** `src/Shared/Roamint.Application.Abstractions/`, `tests/Shared/Roamint.Application.Abstractions.Tests/`, `Directory.Packages.props`

## Context

Every module's Application layer will route work through a single seam: commands that mutate, queries
that read, and events that fan out. CLAUDE.md mandates a **hand-rolled in-process dispatcher — no
MediatR** (licensing). This slice delivers that seam and nothing else: contracts, an in-process
implementation, DI registration by assembly scan, and unit tests.

It is deliberately the *first* infrastructure slice because the Expenses **Application** handlers
(0004) and the **transactional outbox** (0006) both depend on it — the outbox dispatches integration
events through this same publisher. No EF, no persistence, no module wiring here.

## Decisions

- **Dedicated `Roamint.Application.Abstractions` project**, separate from `Roamint.SharedKernel`.
  The kernel stays domain-only (`Money`, `Entity`, `IDomainEvent`); application-layer mediation does
  not belong there. Every module's Application project references *this* project to declare handlers.
- **Contracts public, implementation `internal sealed`.** Only the interfaces, marker types, and the
  `AddApplicationDispatcher` DI extension are public. The concrete dispatcher classes are `internal` so
  modules cannot bypass the interfaces or new-up the dispatcher — the layering boundary is enforced by
  the compiler, not convention. This keeps the whole dispatcher in one project (no sprawl) while still
  preventing leakage.
- **CQS split, not a unified `ISender`.** Three focused interfaces — `ICommandDispatcher`,
  `IQueryDispatcher`, `IEventDispatcher` — over one god-facade. Respects ISP, makes test doubles
  trivial, and lets the outbox depend only on `IEventDispatcher`. Trade-off: callers that both command
  and query inject two things; acceptable, and honest about what each call site actually does.
- **Commands may be void or return a value.** `ICommand` (no result) and `ICommand<TResult>` (e.g.
  returning a new `ExpenseId`). Two handler interfaces accordingly. Avoids a MediatR-style `Unit`
  placeholder type for the common void case.
- **Queries always return a value.** `IQuery<TResult>` + `IQueryHandler<TQuery, TResult>`.
- **Events are integration events.** `IIntegrationEvent` marker + `IIntegrationEventHandler<TEvent>`.
  `IEventDispatcher.PublishAsync` resolves *all* handlers for the event's runtime type and invokes them
  sequentially. (In-module domain-event handling and the domain→integration translation are the
  outbox slice's job; this slice only provides the publish mechanism.) Domain events keep using
  `IDomainEvent` in the kernel — the two markers are intentionally distinct concepts.
- **No `dynamic`, cached closed-generic invokers.** Each dispatcher builds the closed handler type
  (e.g. `ICommandHandler<RecordExpense, ExpenseId>`) once per message type and caches a compiled
  invoker delegate in a `ConcurrentDictionary`. Resolution of the *handler instance* still goes through
  the DI scope on every call; only the reflection is cached. Faster and AOT-friendlier than `dynamic`.
- **Dispatchers are scoped; handlers resolved from the ambient scope.** Each dispatcher takes
  `IServiceProvider` and calls `GetRequiredService` per dispatch. In Blazor Server every request/circuit
  action runs inside a DI scope, so scoped handlers (and their scoped `DbContext` later) resolve
  correctly. A missing command/query handler throws a clear `DispatcherException`; zero event handlers
  is a no-op (events are fire-and-forget fan-out).
- **`AddApplicationDispatcher(this IServiceCollection, params Assembly[])`** scans the given assemblies
  for closed implementations of the four handler interfaces and registers each as scoped, then registers
  the three dispatchers. The composition root (web host) passes each module's Application assembly.
- **`CancellationToken` on every async method; `Task`-returning** (not `ValueTask`) — handlers are
  genuinely async (DB, outbox) and `Task` is the lower-friction, more interoperable default here.
- **No pipeline behaviors yet.** Validation, logging, and transaction-wrapping behaviors are real needs
  but arrive with the Application slice (0004) that has something to validate. Adding an
  `IPipelineBehavior` abstraction now would be a speculative seam with no second implementation — see
  Open questions.
- **Misuse throws synchronously; handler errors flow through the `Task`.** Null arguments (`ArgumentNullException.ThrowIfNull`) and routing/config failures (no handler, multiple handlers →
  `DispatcherException`) throw *before* the returned `Task` is created — they're programmer errors, not
  runtime outcomes, and surface at the call site immediately. Anything thrown by a handler propagates
  through the awaited task as normal. (Test note: assert these with the deferred `Should.ThrowAsync(() => ...)`
  overload, not by passing an already-invoked call.)
- **Two analyzer accommodations under `latest-recommended` + warnings-as-errors.** (1) `PublishAsync`/
  `HandleAsync` take `integrationEvent`, not `@event` — CA1716 forbids the reserved keyword on
  interface members. (2) `IIntegrationEventHandler` carries a narrow `[SuppressMessage(CA1711)]`: the
  rule dislikes the `EventHandler` suffix, but `Handler` is the ubiquitous DDD term and consistency with
  `ICommandHandler`/`IQueryHandler` wins. Wrapper impls instantiated via `Activator.CreateInstance` carry
  `[SuppressMessage(CA1812)]` (the analyzer can't see reflection-based construction).

## Public surface

```csharp
// Commands
public interface ICommand;
public interface ICommand<TResult>;
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken ct);
}
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
}

// Queries
public interface IQuery<TResult>;
public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct);
}

// Integration events
public interface IIntegrationEvent;
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

// Dispatchers (implementations internal)
public interface ICommandDispatcher
{
    Task SendAsync(ICommand command, CancellationToken ct = default);
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default);
}
public interface IQueryDispatcher
{
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}
public interface IEventDispatcher
{
    Task PublishAsync(IIntegrationEvent @event, CancellationToken ct = default);
}

// DI
public static IServiceCollection AddApplicationDispatcher(
    this IServiceCollection services, params Assembly[] handlerAssemblies);

// Errors
public sealed class DispatcherException : Exception; // no handler / multiple command handlers
```

## Packages

- `Microsoft.Extensions.DependencyInjection.Abstractions` (10.6.x band) — for `IServiceCollection` in the
  library. Verify with `dotnet package search` and add a `<PackageVersion>` entry before referencing.
- Tests use the already-pinned xUnit v3 + Shouldly. Handler doubles are hand-written fakes; **no
  NSubstitute needed** for this slice (the dispatcher's collaborators are concrete handler classes).

## Open questions

- **Pipeline behaviors** — when 0004 introduces validators, decide between (a) a generic
  `IPipelineBehavior<TMessage>` chain the dispatcher composes, or (b) explicit validation inside each
  handler. Lean toward (a) only if a second cross-cutting concern (e.g. logging/timing) also appears.
- **Event dispatch error policy** — sequential invoke is fine in-process; once the outbox drives this,
  decide whether one failing handler should abort the batch or be isolated/retried. Belongs to 0006.
- **Parallel vs sequential event handlers** — sequential for determinism now; revisit only if a real
  handler is slow enough to matter.

## Verification

1. `dotnet restore` — clean, no NU1507.
2. `dotnet build` — 0 warnings, 0 errors (warnings-as-errors on).
3. `dotnet test tests/Shared/Roamint.Application.Abstractions.Tests` — all green. Cases:
   - void command routes to its handler; result command returns the handler's value.
   - query returns the handler's value.
   - event publishes to *all* registered handlers, sequentially.
   - missing command/query handler throws `DispatcherException` with a useful message.
   - publishing an event with no handlers is a no-op.
   - `AddApplicationDispatcher` discovers and registers closed handlers from a scanned assembly.
4. The abstractions project references no other Roamint project; nothing else references its internals.

## What's next

Slice B (0004) — Expenses **Application** layer: `RecordExpense` / `RecategorizeExpense` /
`CorrectExpense` and the Category commands, repository *interfaces*, validators, handlers tested against
fakes — all riding on the dispatcher contracts defined here.
