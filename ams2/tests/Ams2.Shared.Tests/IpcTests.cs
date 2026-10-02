using Ams2.Shared.Ipc;
using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

public class IpcTests
{
    static string NewPipe() => "ams2-test-" + Guid.NewGuid().ToString("N");

    static async Task WaitFor(Func<bool> cond, int ms = 3000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < until) await Task.Delay(20);
        Assert.True(cond(), "condicao nao atingida a tempo");
    }

    [Fact]
    public async Task Request_response_and_event_roundtrip()
    {
        var pipe = NewPipe();
        IpcServer? server = null;
        server = new IpcServer(pipe, req =>
        {
            Assert.Equal("setWidget", req.Cmd);
            Assert.Equal("fuel", req.Widget);
            Assert.Equal(1.5f, req.Patch!.Scale);
            server!.Broadcast(new IpcMessage { Event = IpcEvents.StateChanged });
            return new IpcMessage { State = new HostState { Theme = "f1-1998", ActiveProfile = "Padrão", GameConnected = true } };
        });
        using var _s = server;
        using var client = new IpcClient(pipe);
        IpcMessage? evt = null;
        client.EventReceived += e => evt = e;
        client.Start();
        await WaitFor(() => client.Connected);

        var state = await client.SendAsync(IpcCommands.SetWidget, m => m with { Widget = "fuel", Patch = new WidgetPatch { Scale = 1.5f } });
        Assert.True(state!.GameConnected);
        Assert.Equal("Padrão", state.ActiveProfile);
        await WaitFor(() => evt is not null);
        Assert.Equal(IpcEvents.StateChanged, evt!.Event);
    }

    [Fact]
    public async Task Full_profile_travels_in_applyProfile()
    {
        var pipe = NewPipe();
        Profile? got = null;
        using var server = new IpcServer(pipe, req => { got = req.Data; return new IpcMessage(); });
        using var client = new IpcClient(pipe);
        client.Start();
        await WaitFor(() => client.Connected);
        var p = ProfileFactory.CreateDefault("Teste", "f1-1998").WithWidget(new WidgetSettings { Id = "fuel", X = 7, Y = 8, Columns = ["laps"] });
        await client.SendAsync(IpcCommands.ApplyProfile, m => m with { Data = p });
        Assert.Equal("Teste", got!.Name);
        Assert.Equal(7, got.Get("fuel")!.X);
        Assert.Equal(["laps"], got.Get("fuel")!.Columns);
    }

    [Fact]
    public async Task Handler_exception_becomes_error_response()
    {
        var pipe = NewPipe();
        using var server = new IpcServer(pipe, _ => throw new InvalidOperationException("boom"));
        using var client = new IpcClient(pipe);
        client.Start();
        await WaitFor(() => client.Connected);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(IpcCommands.GetState));
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public async Task Client_connects_when_host_starts_later_and_reconnects_after_restart()
    {
        var pipe = NewPipe();
        using var client = new IpcClient(pipe);
        client.Start();
        await Task.Delay(300);
        Assert.False(client.Connected);
        await Assert.ThrowsAsync<IOException>(() => client.SendAsync(IpcCommands.GetState));

        var server1 = new IpcServer(pipe, _ => new IpcMessage());
        await WaitFor(() => client.Connected);
        server1.Dispose();
        await WaitFor(() => !client.Connected);

        using var server2 = new IpcServer(pipe, _ => new IpcMessage { State = new HostState { ActiveProfile = "B" } });
        await WaitFor(() => client.Connected, 5000);
        Assert.Equal("B", (await client.SendAsync(IpcCommands.GetState))!.ActiveProfile);
    }

    [Fact]
    public void Messages_of_other_protocol_versions_or_garbage_are_ignored()
    {
        Assert.Null(IpcProtocol.TryParse("{\"v\":99,\"kind\":\"req\"}"));
        Assert.Null(IpcProtocol.TryParse("nao e json"));
        Assert.NotNull(IpcProtocol.TryParse(IpcProtocol.Serialize(new IpcMessage { Cmd = "getState" })));
    }
}
