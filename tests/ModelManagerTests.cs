using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Veron;
using Veron.Daemon;
using Xunit;

namespace Veron.Tests;

sealed class ManualTime : TimeProvider
{
    DateTimeOffset _now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

sealed class FakeProcess(int pid, int port, string logPath) : ILlamaProcess
{
    public int Pid { get; } = pid;
    public int Port { get; } = port;
    public string LogPath { get; } = logPath;
    public bool HasExited { get; private set; }
    public event Action? Exited;
    public int KillCount;

    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task WaitReadyAsync(TimeSpan timeout, CancellationToken ct)
    {
        var done = await Task.WhenAny(Ready.Task, Task.Delay(timeout, ct));
        if (done != Ready.Task) throw new ModelLoadException("not ready in time");
        await Ready.Task; // propagate failure
    }

    public void Kill() { Interlocked.Increment(ref KillCount); HasExited = true; }

    public void Crash() { HasExited = true; Exited?.Invoke(); }
}

sealed class FakeLauncher : ILlamaLauncher
{
    int _pid = 1000;
    public ConcurrentQueue<(string Model, FakeProcess Proc)> Starts { get; } = new();
    public bool AutoReady { get; set; } = true;
    public Exception? FailWith { get; set; }

    public ILlamaProcess Start(ResolvedModel model, int port, string logPath)
    {
        var p = new FakeProcess(Interlocked.Increment(ref _pid), port, logPath);
        if (FailWith is not null) p.Ready.SetException(FailWith);
        else if (AutoReady) p.Ready.SetResult();
        Starts.Enqueue((model.Name, p));
        return p;
    }

    public FakeProcess Last => Starts.Last().Proc;
    public string[] StartedModels => Starts.Select(s => s.Model).ToArray();
}

public class ModelManagerTests
{
    static ResolvedModel Model(string name, TimeSpan? keepAlive = null) =>
        new(name, "/tmp/" + name, new ModelConfig { ModelPath = "/tmp/" + name + ".gguf", Alias = name, Wait = 5, KeepAlive = keepAlive });

    readonly FakeLauncher _launcher = new();
    readonly ManualTime _time = new();
    readonly HashSet<int> _deadPids = new();

    ModelManager Manager(TimeSpan? keepAlive = null) =>
        new(_launcher, keepAlive ?? TimeSpan.FromMinutes(5), "/tmp/veron-test-logs", _time,
            isPidAlive: pid => !_deadPids.Contains(pid), allocatePort: () => 40000 + _launcher.Starts.Count);

    static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    [Fact]
    public async Task Loads_On_First_Request_And_Reuses_Afterwards()
    {
        using var m = Manager();
        using (var l = await m.AcquireAsync(Model("a"), default)) Assert.Equal(40000, l.Port);
        using (await m.AcquireAsync(Model("a"), default)) { }

        Assert.Equal(new[] { "a" }, _launcher.StartedModels);
        Assert.Equal("a", m.Status().Single().Name);
    }

    [Fact]
    public async Task Concurrent_Requests_Share_One_Load()
    {
        _launcher.AutoReady = false;
        using var m = Manager();

        var t1 = m.AcquireAsync(Model("a"), default);
        var t2 = m.AcquireAsync(Model("a"), default);
        await Eventually(() => _launcher.Starts.Count == 1);
        Assert.False(t1.IsCompleted || t2.IsCompleted);

        _launcher.Last.Ready.SetResult();
        (await t1).Dispose();
        (await t2).Dispose();

        Assert.Single(_launcher.Starts);
    }

    [Fact]
    public async Task Swap_Waits_For_InFlight_Requests_Then_Stops_Old_Model()
    {
        using var m = Manager();
        var leaseA = await m.AcquireAsync(Model("a"), default);
        var procA  = _launcher.Last;

        var acquireB = m.AcquireAsync(Model("b"), default);
        await Task.Delay(300);
        Assert.False(acquireB.IsCompleted);           // A still generating
        Assert.Equal(0, procA.KillCount);

        leaseA.Dispose();
        using var leaseB = await acquireB;

        Assert.Equal("b", leaseB.Model.Name);
        Assert.Equal(1, procA.KillCount);
        Assert.Equal(new[] { "a", "b" }, _launcher.StartedModels);
    }

    [Fact]
    public async Task Idle_Model_Unloads_After_Keep_Alive()
    {
        using var m = Manager(TimeSpan.FromMinutes(5));
        (await m.AcquireAsync(Model("a"), default)).Dispose();
        var proc = _launcher.Last;

        _time.Advance(TimeSpan.FromMinutes(4));
        m.Tick();
        Assert.Single(m.Status());
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromMinutes(1), m.Status()[0].ExpiresAt);

        _time.Advance(TimeSpan.FromMinutes(1));
        m.Tick();
        Assert.Empty(m.Status());
        await Eventually(() => proc.KillCount == 1);
    }

    [Fact]
    public async Task Profile_Keep_Alive_Overrides_Default_Forever_And_Zero()
    {
        using var m = Manager(TimeSpan.FromMinutes(5));
        (await m.AcquireAsync(Model("forever", Timeout.InfiniteTimeSpan), default)).Dispose();
        _time.Advance(TimeSpan.FromHours(10));
        m.Tick();
        Assert.True(m.Status().Single().KeepForever);

        (await m.AcquireAsync(Model("zero", TimeSpan.Zero), default)).Dispose();
        Assert.Empty(m.Status()); // unloaded as soon as the request finished
    }

    [Fact]
    public async Task Last_Session_Ending_Unloads_Model_It_Loaded()
    {
        using var m = Manager();
        var s1 = await m.CreateSessionAsync(Model("a"), 111, default);
        var s2 = await m.CreateSessionAsync(Model("a"), 222, default);
        Assert.False(s1.WasAlreadyLoaded);
        Assert.True(s2.WasAlreadyLoaded);
        var proc = _launcher.Last;

        var r1 = await m.ReleaseSessionAsync(s1.Id);
        Assert.False(r1.Unloaded);
        Assert.Single(m.Status());

        var r2 = await m.ReleaseSessionAsync(s2.Id);
        Assert.True(r2.Unloaded);
        Assert.Empty(m.Status());
        Assert.Equal(1, proc.KillCount);
    }

    [Fact]
    public async Task Session_Does_Not_Unload_A_Model_That_Was_Already_Loaded()
    {
        using var m = Manager();
        (await m.AcquireAsync(Model("a"), default)).Dispose(); // e.g. `veron run` or an API client

        var s = await m.CreateSessionAsync(Model("a"), 111, default);
        var r = await m.ReleaseSessionAsync(s.Id);

        Assert.False(r.Unloaded);
        Assert.Single(m.Status());
    }

    [Fact]
    public async Task Open_Session_Pins_Model_Against_Idle_Unload()
    {
        using var m = Manager(TimeSpan.FromMinutes(5));
        await m.CreateSessionAsync(Model("a"), 111, default);

        _time.Advance(TimeSpan.FromHours(2));
        m.Tick();

        var st = m.Status().Single();
        Assert.Equal(1, st.Sessions);
        Assert.Null(st.ExpiresAt);
    }

    [Fact]
    public async Task Session_Of_Dead_Client_Is_Dropped_And_Model_Unloaded()
    {
        using var m = Manager();
        await m.CreateSessionAsync(Model("a"), 4242, default);

        _deadPids.Add(4242);
        m.Tick();

        Assert.Empty(m.Status());
    }

    [Fact]
    public async Task Session_Ending_During_A_Request_Unloads_When_The_Request_Finishes()
    {
        using var m = Manager();
        var s = await m.CreateSessionAsync(Model("a"), 111, default);
        var lease = await m.AcquireAsync(Model("a"), default);

        var r = await m.ReleaseSessionAsync(s.Id);
        Assert.True(r.UnloadPending);
        Assert.Single(m.Status());

        lease.Dispose();
        Assert.Empty(m.Status());
    }

    [Fact]
    public async Task Crash_Clears_State_And_Next_Request_Reloads()
    {
        using var m = Manager();
        (await m.AcquireAsync(Model("a"), default)).Dispose();

        _launcher.Last.Crash();
        Assert.Empty(m.Status());

        (await m.AcquireAsync(Model("a"), default)).Dispose();
        Assert.Equal(2, _launcher.Starts.Count);
    }

    [Fact]
    public async Task Load_Failure_Throws_Kills_Process_And_Allows_Retry()
    {
        using var m = Manager();
        _launcher.FailWith = new ModelLoadException("llama-server exited during startup (exit code 1)");

        var ex = await Assert.ThrowsAsync<ModelLoadException>(() => m.AcquireAsync(Model("a"), default));
        Assert.Contains("exit code 1", ex.Message);
        Assert.Contains("log:", ex.Message);
        Assert.Equal(1, _launcher.Last.KillCount);
        Assert.Empty(m.Status());

        _launcher.FailWith = null;
        (await m.AcquireAsync(Model("a"), default)).Dispose();
        Assert.Single(m.Status());
    }

    [Fact]
    public async Task Unload_Stops_Named_Or_Any_Model()
    {
        using var m = Manager();
        (await m.AcquireAsync(Model("a"), default)).Dispose();

        Assert.Empty(await m.UnloadAsync("other"));
        Assert.Equal(new[] { "a" }, await m.UnloadAsync("A"));
        Assert.Empty(m.Status());

        (await m.AcquireAsync(Model("b"), default)).Dispose();
        Assert.Equal(new[] { "b" }, await m.UnloadAsync(null));
    }

    [Fact]
    public async Task Cancelled_Swap_Leaves_Old_Model_Usable()
    {
        using var m = Manager();
        var leaseA = await m.AcquireAsync(Model("a"), default);

        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => m.AcquireAsync(Model("b"), cts.Token));

        // A is not draining any more: new requests for A are served without waiting
        var again = m.AcquireAsync(Model("a"), default);
        Assert.True(again.IsCompleted);
        (await again).Dispose();
        leaseA.Dispose();
        Assert.Equal(new[] { "a" }, _launcher.StartedModels);
    }
}
