namespace Roamint.Application.Abstractions.Tests;

/// <summary>Captures handler invocations so tests can assert routing without static state.</summary>
public sealed class Recorder
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls => _calls;

    public void Record(string entry) => _calls.Add(entry);
}

// --- Void command ---
public sealed record Ping(string Text) : ICommand;

public sealed class PingHandler(Recorder recorder) : ICommandHandler<Ping>
{
    public Task HandleAsync(Ping command, CancellationToken ct)
    {
        recorder.Record($"ping:{command.Text}");
        return Task.CompletedTask;
    }
}

// --- Command with result ---
public sealed record CreateThing(string Name) : ICommand<Guid>;

public sealed class CreateThingHandler : ICommandHandler<CreateThing, Guid>
{
    public static readonly Guid FixedId = Guid.Parse("0192e8c0-0000-7000-8000-000000000001");

    public Task<Guid> HandleAsync(CreateThing command, CancellationToken ct) => Task.FromResult(FixedId);
}

// --- Query ---
public sealed record Greet(string Name) : IQuery<string>;

public sealed class GreetHandler : IQueryHandler<Greet, string>
{
    public Task<string> HandleAsync(Greet query, CancellationToken ct) => Task.FromResult($"Hello, {query.Name}");
}

// --- Event with two handlers ---
public sealed record ThingHappened(int Id) : IIntegrationEvent;

public sealed class FirstThingHandler(Recorder recorder) : IIntegrationEventHandler<ThingHappened>
{
    public Task HandleAsync(ThingHappened @event, CancellationToken ct)
    {
        recorder.Record($"first:{@event.Id}");
        return Task.CompletedTask;
    }
}

public sealed class SecondThingHandler(Recorder recorder) : IIntegrationEventHandler<ThingHappened>
{
    public Task HandleAsync(ThingHappened @event, CancellationToken ct)
    {
        recorder.Record($"second:{@event.Id}");
        return Task.CompletedTask;
    }
}

// --- Deliberately unhandled message types ---
public sealed record Orphan : ICommand;

public sealed record NobodyCares : IIntegrationEvent;
