using Microsoft.Extensions.DependencyInjection;

namespace Roamint.Application.Abstractions.Tests;

public sealed class DispatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddApplicationDispatcher(typeof(DispatcherTests).Assembly);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registers_the_three_dispatchers()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetService<ICommandDispatcher>().ShouldNotBeNull();
        sp.GetService<IQueryDispatcher>().ShouldNotBeNull();
        sp.GetService<IEventDispatcher>().ShouldNotBeNull();
    }

    [Fact]
    public void Calling_twice_registers_each_dispatcher_once()
    {
        // Each module calls AddApplicationDispatcher with its own Application assembly, so the shared
        // dispatcher registrations must be idempotent (TryAdd) to avoid duplicates.
        var services = new ServiceCollection();
        services.AddApplicationDispatcher(typeof(DispatcherTests).Assembly);
        services.AddApplicationDispatcher(typeof(DispatcherTests).Assembly);

        services.Count(d => d.ServiceType == typeof(ICommandDispatcher)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IQueryDispatcher)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IEventDispatcher)).ShouldBe(1);
    }

    [Fact]
    public async Task Void_command_routes_to_its_handler()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<ICommandDispatcher>().SendAsync(new Ping("hi"), Ct);

        sp.GetRequiredService<Recorder>().Calls.ShouldBe(["ping:hi"]);
    }

    [Fact]
    public async Task Result_command_returns_handler_value()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync(new CreateThing("widget"), Ct);

        result.ShouldBe(CreateThingHandler.FixedId);
    }

    [Fact]
    public async Task Query_returns_handler_value()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .QueryAsync(new Greet("Roamint"), Ct);

        result.ShouldBe("Hello, Roamint");
    }

    [Fact]
    public async Task Event_publishes_to_all_handlers()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<IEventDispatcher>().PublishAsync(new ThingHappened(7), Ct);

        sp.GetRequiredService<Recorder>().Calls.ShouldBe(["first:7", "second:7"], ignoreOrder: true);
    }

    [Fact]
    public async Task Event_with_no_handlers_is_a_no_op()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        await Should.NotThrowAsync(
            scope.ServiceProvider.GetRequiredService<IEventDispatcher>().PublishAsync(new NobodyCares(), Ct));
    }

    [Fact]
    public async Task Missing_command_handler_throws_DispatcherException()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        var ex = await Should.ThrowAsync<DispatcherException>(() => dispatcher.SendAsync(new Orphan(), Ct));

        ex.Message.ShouldContain(nameof(Orphan));
    }
}
