using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Veron.Daemon;

/// <summary>A running llama-server child process.</summary>
public interface ILlamaProcess
{
    int Pid { get; }
    int Port { get; }
    string LogPath { get; }
    bool HasExited { get; }

    /// <summary>Raised once when the process exits (for any reason).</summary>
    event Action? Exited;

    /// <summary>Waits until /health answers 200. Throws <see cref="ModelLoadException"/> on exit or timeout.</summary>
    Task WaitReadyAsync(TimeSpan timeout, CancellationToken ct);

    /// <summary>Kills the process tree and waits briefly for it to exit.</summary>
    void Kill();
}

public interface ILlamaLauncher
{
    ILlamaProcess Start(ResolvedModel model, int port, string logPath);
}

public sealed class ModelLoadException(string message) : Exception(message);

/// <summary>Starts real llama-server processes, logging their output to a file.</summary>
public sealed class LlamaLauncher : ILlamaLauncher
{
    public ILlamaProcess Start(ResolvedModel model, int port, string logPath)
    {
        var cfg = model.Config.Clone();
        cfg.Port = port;
        var cmd = LlamaServer.BuildLlamaCmd(cfg);

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        { AutoFlush = true };
        log.WriteLine("# veron: " + string.Join(" ", cmd.Select(CliParser.EscapeArg)));

        var psi = LlamaServer.ToPsi(cmd);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError  = true;
        psi.RedirectStandardInput  = true;

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!proc.Start()) throw new ModelLoadException("Failed to start " + cmd[0]);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            log.Dispose();
            throw new ModelLoadException("Cannot start llama-server '" + cmd[0] + "': " + ex.Message +
                                         " (set VERON_LLAMA_SERVER to the binary path)");
        }

        return new RealLlamaProcess(proc, port, logPath, log);
    }

    /// <summary>Returns a currently free TCP port on 127.0.0.1.</summary>
    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Last <paramref name="n"/> lines of a log file (best effort).</summary>
    public static string Tail(string path, int n = 20)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            var q = new Queue<string>();
            while (sr.ReadLine() is { } line)
            {
                q.Enqueue(line);
                if (q.Count > n) q.Dequeue();
            }
            return string.Join("\n", q);
        }
        catch { return ""; }
    }

    sealed class RealLlamaProcess : ILlamaProcess
    {
        readonly Process _proc;
        readonly StreamWriter _log;
        readonly object _logLock = new();
        int _exitedRaised;

        public RealLlamaProcess(Process proc, int port, string logPath, StreamWriter log)
        {
            _proc = proc; Port = port; LogPath = logPath; _log = log;
            Pid = proc.Id;

            proc.OutputDataReceived += (_, e) => Write(e.Data);
            proc.ErrorDataReceived  += (_, e) => Write(e.Data);
            proc.Exited += (_, _) =>
            {
                if (Interlocked.Exchange(ref _exitedRaised, 1) == 0)
                {
                    // let the output readers drain before closing the log
                    try { _proc.WaitForExit(); } catch { }
                    lock (_logLock) { try { _log.Dispose(); } catch { } }
                    Exited?.Invoke();
                }
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }

        void Write(string? line)
        {
            if (line is null) return;
            lock (_logLock) { try { _log.WriteLine(line); } catch (ObjectDisposedException) { } }
        }

        public int Pid { get; }
        public int Port { get; }
        public string LogPath { get; }
        public bool HasExited { get { try { return _proc.HasExited; } catch { return true; } } }
        public event Action? Exited;

        public async Task WaitReadyAsync(TimeSpan timeout, CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var deadline = DateTime.UtcNow + timeout;
            string url = "http://127.0.0.1:" + Port + "/health";

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (HasExited)
                    throw new ModelLoadException("llama-server exited during startup (exit code " +
                                                 SafeExitCode() + ")");
                try
                {
                    using var r = await http.GetAsync(url, ct);
                    if (r.StatusCode == HttpStatusCode.OK) return;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }

                await Task.Delay(500, ct);
            }
            throw new ModelLoadException("llama-server did not become ready within " + (int)timeout.TotalSeconds +
                                         "s (raise it with PARAMETER wait <seconds>)");
        }

        string SafeExitCode() { try { return _proc.ExitCode.ToString(); } catch { return "?"; } }

        public void Kill()
        {
            try
            {
                if (_proc.HasExited) return;
                // graceful first (lets llama-server release the GPU cleanly), then force
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) SysKill(_proc.Id, SIGTERM);
                if (!_proc.WaitForExit(10000))
                {
                    _proc.Kill(entireProcessTree: true);
                    _proc.WaitForExit(10000);
                }
            }
            catch (InvalidOperationException) { }
        }

        const int SIGTERM = 15;

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        static extern int SysKill(int pid, int sig);
    }
}
