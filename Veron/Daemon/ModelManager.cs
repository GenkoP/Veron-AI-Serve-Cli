using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Veron.Daemon;

/// <summary>Snapshot of the loaded model for `veron ps`.</summary>
public sealed record ModelStatus(
    string Name, string From, int Pid, int Port, int Context,
    DateTimeOffset LoadedAt, DateTimeOffset? ExpiresAt, bool KeepForever,
    int ActiveRequests, int Sessions, string LogPath);

public sealed record SessionInfo(string Id, string Model, bool WasAlreadyLoaded);

public sealed record SessionReleaseResult(bool Unloaded, bool UnloadPending);

/// <summary>
/// Keeps at most one llama-server running. Requests take a <see cref="Lease"/>; a request
/// for another model waits for in-flight requests to finish, then swaps. Idle models are
/// unloaded after keep_alive. CLI sessions (veron claude/copilot) pin the model while
/// open and unload it when the last one ends — unless it was loaded before they started.
/// </summary>
public sealed class ModelManager : IDisposable
{
    readonly ILlamaLauncher _launcher;
    readonly TimeSpan _defaultKeepAlive;
    readonly string _logsDir;
    readonly TimeProvider _time;
    readonly Func<int, bool> _isPidAlive;
    readonly Func<int> _allocatePort;
    readonly Action<string> _log;
    readonly Func<int, CancellationToken, Task<EffortCaps>> _probeEffort;

    readonly object _sync = new();
    readonly SemaphoreSlim _gate = new(1, 1);            // serialises load / swap
    readonly CancellationTokenSource _shutdown = new();
    readonly Dictionary<string, Session> _sessions = new();
    Running? _current;
    Task _killing = Task.CompletedTask;                  // last pending kill; loads wait for it

    public ModelManager(
        ILlamaLauncher launcher, TimeSpan defaultKeepAlive, string logsDir,
        TimeProvider? time = null, Func<int, bool>? isPidAlive = null,
        Func<int>? allocatePort = null, Action<string>? log = null,
        Func<int, CancellationToken, Task<EffortCaps>>? probeEffort = null)
    {
        _launcher         = launcher;
        _defaultKeepAlive = defaultKeepAlive;
        _logsDir          = logsDir;
        _time             = time ?? TimeProvider.System;
        _isPidAlive       = isPidAlive ?? DefaultIsPidAlive;
        _allocatePort     = allocatePort ?? LlamaLauncher.FreePort;
        _log              = log ?? (_ => { });
        _probeEffort      = probeEffort ?? EffortCaps.FetchAsync;
    }

    // ── State ────────────────────────────────────────────────────────────

    internal sealed class Running(ResolvedModel model, ILlamaProcess proc, DateTimeOffset loadedAt)
    {
        public ResolvedModel Model { get; } = model;
        public ILlamaProcess Proc { get; } = proc;
        public DateTimeOffset LoadedAt { get; } = loadedAt;
        public EffortCaps Effort { get; init; } = EffortCaps.None;  // chat template's reasoning effort levels
        public readonly ConcurrentDictionary<string, byte> EffortRewritesLogged = new();
        public DateTimeOffset LastUsed;
        public int  Active;
        public bool Draining;          // a swap is waiting for in-flight requests
        public bool LoadedBySession;   // unload when the last session ends
        public bool UnloadWhenIdle;    // last session ended while requests were in flight
        public bool Stopping;
    }

    sealed record Session(string Id, string Model, int Pid);

    /// <summary>A claim on the loaded model for one request. Dispose when the response is done.</summary>
    public sealed class Lease : IDisposable
    {
        readonly ModelManager _owner;
        internal readonly Running Run;
        int _disposed;

        internal Lease(ModelManager owner, Running run) { _owner = owner; Run = run; }

        public ResolvedModel Model => Run.Model;
        public int Port => Run.Proc.Port;
        public string BaseUrl => "http://127.0.0.1:" + Run.Proc.Port;
        public EffortCaps Effort => Run.Effort;

        /// <summary>True the first time a rewrite of <paramref name="from"/> is seen for this loaded model.</summary>
        public bool FirstEffortRewrite(string from) => Run.EffortRewritesLogged.TryAdd(from, 0);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _owner.Release(Run);
        }
    }

    public string? CurrentModelName
    {
        get { lock (_sync) return _current is { Stopping: false } c ? c.Model.Name : null; }
    }

    TimeSpan KeepAliveOf(Running r) => r.Model.Config.KeepAlive ?? _defaultKeepAlive;

    bool HasSessions(string model) => _sessions.Values.Any(s => s.Model == model);

    // ── Acquire / release ────────────────────────────────────────────────

    public async Task<Lease> AcquireAsync(ResolvedModel model, CancellationToken ct, bool forSession = false)
    {
        if (TryFastLease(model) is { } fast) return fast;

        await _gate.WaitAsync(ct);
        try
        {
            if (TryFastLease(model) is { } loadedMeanwhile) return loadedMeanwhile;

            Running? old;
            lock (_sync)
            {
                old = _current;
                if (old is not null) old.Draining = true;
            }

            if (old is not null)
            {
                try { await WaitDrainedAsync(old, ct); }
                catch { lock (_sync) old.Draining = false; throw; }

                _log($"swapping {old.Model.Name} -> {model.Name}");
                lock (_sync) Detach(old);
            }

            Task killing;
            lock (_sync) killing = _killing;
            await killing; // VRAM must be free before the next model starts

            var run = await LoadAsync(model);
            lock (_sync)
            {
                run.LoadedBySession = forSession || HasSessions(model.Name);
                run.Active   = 1;
                run.LastUsed = _time.GetUtcNow();
                _current     = run;
            }
            return new Lease(this, run);
        }
        finally { _gate.Release(); }
    }

    Lease? TryFastLease(ResolvedModel model)
    {
        lock (_sync)
        {
            if (_current is { Draining: false, Stopping: false } c && c.Model.Name == model.Name && !c.Proc.HasExited)
            {
                c.Active++;
                return new Lease(this, c);
            }
            return null;
        }
    }

    async Task WaitDrainedAsync(Running r, CancellationToken ct)
    {
        while (true)
        {
            lock (_sync) if (r.Active == 0 || r.Proc.HasExited) return;
            await Task.Delay(100, ct);
        }
    }

    async Task<Running> LoadAsync(ResolvedModel model)
    {
        int port = _allocatePort();
        string logPath = Path.Join(_logsDir, model.Name + ".log");
        _log($"loading {model.Name} ({Path.GetFileName(model.Config.ModelPath)}) on internal port {port}");

        var proc = _launcher.Start(model, port, logPath);
        try
        {
            await proc.WaitReadyAsync(TimeSpan.FromSeconds(Math.Max(1, model.Config.Wait)), _shutdown.Token);
        }
        catch (Exception ex) when (ex is ModelLoadException or OperationCanceledException)
        {
            proc.Kill();
            string tail = LlamaLauncher.Tail(logPath);
            _log($"failed to load {model.Name}: {ex.Message}");
            throw new ModelLoadException(
                $"Failed to load model '{model.Name}': {ex.Message}\nlog: {logPath}" +
                (tail.Length > 0 ? "\n" + tail : ""));
        }

        var effort = await ProbeEffortAsync(model, proc.Port);
        var run = new Running(model, proc, _time.GetUtcNow()) { Effort = effort };
        proc.Exited += () => OnExited(run);
        _log($"loaded {model.Name} (pid {proc.Pid})");
        return run;
    }

    /// <summary>Best effort: reads the template's effort levels from /props; any failure means "no info".</summary>
    async Task<EffortCaps> ProbeEffortAsync(ResolvedModel model, int port)
    {
        EffortCaps caps;
        try { caps = await _probeEffort(port, _shutdown.Token) ?? EffortCaps.None; }
        catch (Exception ex)
        {
            _log($"could not read chat template effort levels of {model.Name} from /props: {ex.Message}");
            return EffortCaps.None;
        }
        if (!caps.IsEmpty)
            _log($"{model.Name} template effort levels: {Join(caps.Levels)} (rejected: {Join(caps.Rejected)})");
        return caps;

        static string Join(IReadOnlyList<string> l) => l.Count == 0 ? "-" : string.Join(", ", l);
    }

    void Release(Running r)
    {
        Running? unload = null;
        lock (_sync)
        {
            r.Active--;
            r.LastUsed = _time.GetUtcNow();
            if (r.Active == 0 && !r.Draining && _current == r)
            {
                bool keepAliveZero = KeepAliveOf(r) == TimeSpan.Zero && !HasSessions(r.Model.Name);
                if (r.UnloadWhenIdle || keepAliveZero)
                {
                    Detach(r);
                    unload = r;
                }
            }
        }
        if (unload is not null) _log($"unloaded {unload.Model.Name} (idle)");
    }

    void OnExited(Running r)
    {
        lock (_sync)
        {
            if (r.Stopping) return;
            r.Stopping = true;
            if (_current == r) _current = null;
        }
        _log($"llama-server for {r.Model.Name} exited unexpectedly — see {r.Proc.LogPath}");
    }

    /// <summary>Must hold _sync. Clears _current and schedules the kill (loads await it).</summary>
    void Detach(Running r)
    {
        if (_current == r) _current = null;
        if (r.Stopping) return;
        r.Stopping = true;
        var prev = _killing;
        _killing = Task.Run(async () =>
        {
            try { await prev; } catch { }
            r.Proc.Kill();
        });
    }

    // ── Sessions (veron claude / copilot) ────────────────────────────────

    public async Task<SessionInfo> CreateSessionAsync(ResolvedModel model, int clientPid, CancellationToken ct)
    {
        bool wasLoaded;
        lock (_sync) wasLoaded = _current is { Draining: false, Stopping: false } c && c.Model.Name == model.Name;

        var lease = await AcquireAsync(model, ct, forSession: true);
        string id = Guid.NewGuid().ToString("n");
        lock (_sync) _sessions[id] = new Session(id, model.Name, clientPid);
        lease.Dispose(); // the session now pins the model

        _log($"session {id[..8]} opened for {model.Name} (client pid {clientPid})");
        return new SessionInfo(id, model.Name, wasLoaded);
    }

    public async Task<SessionReleaseResult> ReleaseSessionAsync(string id)
    {
        Running? detached;
        bool pending;
        Task killing;
        lock (_sync)
        {
            if (!_sessions.Remove(id, out var s)) return new SessionReleaseResult(false, false);
            (detached, pending) = OnSessionEnded(s.Model);
            killing = _killing;
        }
        _log($"session {id[..Math.Min(8, id.Length)]} closed");
        if (detached is not null)
        {
            _log($"unloaded {detached.Model.Name} (last session ended)");
            await killing;
        }
        return new SessionReleaseResult(detached is not null, pending);
    }

    /// <summary>Must hold _sync.</summary>
    (Running? Detached, bool Pending) OnSessionEnded(string model)
    {
        if (HasSessions(model)) return (null, false);
        var c = _current;
        if (c is null || c.Model.Name != model || !c.LoadedBySession || c.Stopping) return (null, false);

        if (c.Active == 0 && !c.Draining)
        {
            Detach(c);
            return (c, false);
        }
        c.UnloadWhenIdle = true;
        return (null, true);
    }

    // ── Periodic maintenance ─────────────────────────────────────────────

    /// <summary>Drops sessions of dead clients and unloads idle models. Called every few seconds.</summary>
    public void Tick()
    {
        var unloaded = new List<(string Name, string Why)>();
        lock (_sync)
        {
            foreach (var s in _sessions.Values.Where(s => !_isPidAlive(s.Pid)).ToList())
            {
                _sessions.Remove(s.Id);
                _log($"session {s.Id[..8]} dropped (client pid {s.Pid} is gone)");
                if (OnSessionEnded(s.Model).Detached is { } d) unloaded.Add((d.Model.Name, "last session ended"));
            }

            if (_current is { Draining: false, Stopping: false } c && c.Active == 0)
            {
                var keep = KeepAliveOf(c);
                if (c.UnloadWhenIdle)
                {
                    Detach(c);
                    unloaded.Add((c.Model.Name, "last session ended"));
                }
                else if (!HasSessions(c.Model.Name) && !KeepAlive.IsForever(keep) &&
                         _time.GetUtcNow() - c.LastUsed >= keep)
                {
                    Detach(c);
                    unloaded.Add((c.Model.Name, "idle"));
                }
            }
        }
        foreach (var (name, why) in unloaded) _log($"unloaded {name} ({why})");
    }

    // ── Admin ────────────────────────────────────────────────────────────

    public IReadOnlyList<ModelStatus> Status()
    {
        lock (_sync)
        {
            if (_current is not { Stopping: false } c) return Array.Empty<ModelStatus>();
            var keep     = KeepAliveOf(c);
            int sessions = _sessions.Values.Count(s => s.Model == c.Model.Name);
            bool forever = KeepAlive.IsForever(keep);
            DateTimeOffset? expires = (forever || sessions > 0 || c.Active > 0) ? null : c.LastUsed + keep;

            return new[]
            {
                new ModelStatus(c.Model.Name, Path.GetFileName(c.Model.Config.ModelPath), c.Proc.Pid, c.Proc.Port,
                                c.Model.Config.Context, c.LoadedAt, expires, forever, c.Active, sessions, c.Proc.LogPath)
            };
        }
    }

    /// <summary>Unloads the named model (or any model when null) immediately. Returns unloaded names.</summary>
    public async Task<IReadOnlyList<string>> UnloadAsync(string? name)
    {
        Running? r;
        Task killing;
        lock (_sync)
        {
            r = _current;
            if (r is null || r.Stopping || (name is not null && !r.Model.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return Array.Empty<string>();
            Detach(r);
            killing = _killing;
        }
        _log($"unloaded {r.Model.Name} (requested)");
        await killing;
        return new[] { r.Model.Name };
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        Task killing;
        lock (_sync)
        {
            if (_current is { } c) Detach(c);
            killing = _killing;
        }
        try { killing.Wait(TimeSpan.FromSeconds(20)); } catch { }
    }

    static bool DefaultIsPidAlive(int pid)
    {
        if (pid <= 0) return false;
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }
}
