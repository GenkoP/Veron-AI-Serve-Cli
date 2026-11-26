# Plan: `veron stats` / `veron stats --watch` — live generation metrics

Spec: the design approved in chat on 2026-11-26 (reproduced under "Design" below — this file is the binding spec).

## Design (approved)

- Daemon gets `GET /veron/stats` returning a JSON snapshot:
  - loaded model (name, gguf file, context size) — or none;
  - live slot state from the model's llama-server `GET /slots`: idle / processing prompt (x of y tokens) / generating (n tokens);
  - the last 10 finished requests, parsed from the `print_timing` lines llama-server writes into the model log
    (`~/.veron/logs/<model>.log`): prompt tokens, prompt t/s, output tokens, output t/s, draft acceptance.
- CLI: `veron stats` prints the snapshot once; `veron stats --watch` (aliases `-w`, `veron stats watch`) redraws ~2×/s until
  Ctrl+C. Live t/s = growth of `n_decoded` over a ~2 s sliding window (client side). When a request finishes its exact numbers
  appear in the "Recent requests" table. When stdout is not a terminal, watch mode prints one line per finished request instead of
  redrawing. No model loaded → "No model loaded". A model load in progress → "Loading…" if detectable, else "No model loaded".

Target screen (watch mode, terminal):

```
Veron stats · qwen38-fusion-q4 · ctx 262,144                 17:42:05  (Ctrl+C to quit)

State      ● generating
Prompt     20,024 tokens  (19,800 from cache)
Context    20,410 / 262,144  (7.8%)
Output     386 tokens    91.3 t/s live

Recent requests
TIME      PROMPT   PROMPT t/s   OUTPUT   OUTPUT t/s   DRAFT ACCEPT
17:41:52      35          106      512         92.0            63%
17:40:10   20,024       1,113      256         84.4            61%
```

## Global constraints

- C# / .NET 10, existing project `Veron/Veron.csproj`, tests in `tests/` (xUnit). Run `dotnet test` from the repo root; all
  existing 122 tests must keep passing.
- Follow existing patterns: daemon endpoints in `Veron/Daemon/DaemonHost.cs` (`app.MapGet`), client calls in
  `Veron/Daemon/DaemonClient.cs` (throw `DaemonException` on errors), commands as `static class CmdXxx` in `Veron/Commands/`,
  dispatch in `Veron/Program.cs`, help text in `Veron/Commands/CmdHelp.cs`, docs in `README.md`.
- No new NuGet packages. No changes to llama.cpp, the OS, or drivers.
- llama-server facts (this build, verified in source `tools/server/server-context.cpp`):
  - `GET /slots` is enabled by default and returns a JSON array; each slot object has `id`, `n_ctx`, `is_processing`, and — once
    it has served a task — `n_prompt_tokens`, `n_prompt_tokens_processed`, `n_prompt_tokens_cache`, and
    `next_token: [{ "n_decoded": <int>, "n_remain": <int>, "has_next_token": <bool> }]`. Prompt text is NOT included by default.
    `n_decoded` and `n_prompt_tokens_processed` update live while a request runs. Veron always starts llama-server with `-np 1`
    (one slot) but code must not crash on several slots (use the processing one, else the first).
  - The model log gets these lines when a request finishes (prefix = `minutes.seconds.millis.micros` since llama-server start, then
    level letter). Real sample from `~/.veron/logs/qwen38-fusion-q4.log`:
    ```
    0.24.466.535 I slot print_timing: id  0 | task 348 | prompt eval time =     329.84 ms /    35 tokens (    9.42 ms per token,   106.11 tokens per second)
    0.24.466.537 I slot print_timing: id  0 | task 348 |        eval time =    5552.89 ms /   512 tokens (   10.87 ms per token,    92.02 tokens per second)
    0.24.466.538 I slot print_timing: id  0 | task 348 |       total time =    5882.73 ms /   547 tokens
    0.24.466.541 I slot print_timing: id  0 | task 348 | draft acceptance = 0.63258 (  334 accepted /   528 generated), mean len =  2.90
    ```
    The `draft acceptance` line is only present with speculative decoding. There are also progress lines like
    `... | task 348 | n_gen =    249, tg =  82.00 t/s, tg_3s =  82.32 t/s` — ignore those. Minutes can exceed 59 (e.g. `125.03.001.000`).
- The model log is created fresh (`FileMode.Create`) on every model load by `LlamaLauncher`, and opened with
  `FileShare.ReadWrite`, so it can be read while llama-server writes it. It can grow large over a long session: read it
  incrementally (remember the byte offset per log path + process pid; restart from 0 if the pid changed or the file shrank).
- Finish time of a request = llama-server process start time + the log-line prefix offset. Get the process start time from
  `System.Diagnostics.Process.GetProcessById(pid).StartTime`; if that fails, fall back to the time the daemon first parsed the line.
- Recent list: newest first, at most 10 entries, only for the currently loaded model.

## Task 1: Daemon side — stats collector, `/veron/stats`, `DaemonClient.Stats()`

Files: new `Veron/Daemon/StatsCollector.cs`; edit `Veron/Daemon/DaemonHost.cs`, `Veron/Daemon/DaemonClient.cs`;
new tests `tests/StatsCollectorTests.cs`; extend `tests/DaemonProxyTests.cs` (its `FakeLlamaServerLauncher`) with a `/slots`
handler for an endpoint test.

1. Records (public, in `Veron.Daemon`, serialised with the same System.Text.Json web defaults the daemon already uses):
   - `RequestTiming(int Task, DateTimeOffset FinishedAt, int PromptTokens, double PromptTokensPerSecond, int OutputTokens,
     double OutputTokensPerSecond, double? DraftAcceptance)`
   - `SlotSnapshot(bool IsProcessing, int PromptTokens, int PromptTokensProcessed, int PromptTokensCached, int Decoded, int NCtx)`
   - `StatsSnapshot(DateTimeOffset Now, string? Model, string? From, int Context, int? Pid, int ActiveRequests,
     SlotSnapshot? Slot, IReadOnlyList<RequestTiming> Recent)` — `Model == null` means nothing is loaded.
2. `static class TimingLogParser` with a pure, unit-testable method that turns log lines into `RequestTiming`s: group the
   `prompt eval time` / `eval time` / `draft acceptance` lines by task id; a request is complete once its `eval time` line is
   seen (draft line optional — it comes right after; when parsing incrementally, a draft line arriving in a later chunk for an
   already-emitted task must update that task's `DraftAcceptance`). Parse numbers with `CultureInfo.InvariantCulture`.
   Malformed / unrelated lines are ignored, never throw.
3. `StatsCollector` (one instance owned by `DaemonHost`, thread-safe): `Task<StatsSnapshot> GetAsync(CancellationToken)`:
   takes `manager.Status()`; if a model is loaded, reads new log bytes (incremental, see constraints), updates its recent list,
   and calls `GET http://127.0.0.1:<port>/slots` with a 2 s timeout (on failure `Slot = null`, never throw). Inject the HTTP
   handler / clock / process-start lookup so it is testable.
4. `GET /veron/stats` in `DaemonHost.Build` → `Results.Json(await collector.GetAsync(ct))`.
5. `DaemonClient.Stats()` → `StatsSnapshot` (short timeout, ~5 s).
6. Tests: parser (real sample lines above incl. no-draft case, minutes > 59, malformed lines, incremental chunk with late draft
   line); collector incremental read + file-recreated reset; endpoint test via the fake launcher returning a canned `/slots`
   body and a temp log file containing timing lines.

## Task 2: CLI — `veron stats` and `veron stats --watch`

Files: new `Veron/Commands/CmdStats.cs`; edit `Veron/Program.cs`, `Veron/Commands/CmdHelp.cs`, `README.md`;
new tests `tests/StatsCommandTests.cs`. Depends on Task 1's records and `DaemonClient.Stats()`.

1. `veron stats` → one snapshot rendered as the screen above (without the "(Ctrl+C to quit)" hint); server not running →
   `Veron server is not running.` (same wording as `veron ps`); nothing loaded → `No model loaded (Veron server at <url>).`
2. `veron stats --watch`, `veron stats -w`, `veron stats watch`: poll every 500 ms, redraw in place (ANSI: move cursor home +
   clear to end of screen; hide cursor while running, restore on exit incl. Ctrl+C via `Console.CancelKeyPress`). Live t/s computed
   by a small pure class (e.g. `LiveRate`) from (timestamp, n_decoded) samples over a ~2 s window; reset when n_decoded drops or a
   new request starts; show `—` when not generating. Prompt-processing progress while `IsProcessing && PromptTokensProcessed <
   PromptTokens`: `State ● processing prompt 12,288 / 20,024 (61%)`. Daemon unreachable during watch → show
   `Veron server is not running — waiting…` and keep polling (do not exit).
3. Non-terminal stdout (`Console.IsOutputRedirected`) in watch mode: print one line per newly finished request, e.g.
   `17:41:52  qwen38-fusion-q4  prompt 35 tok @ 106 t/s  output 512 tok @ 92.0 t/s  draft 63%`.
4. Rendering is a pure function (snapshot + live rate + width → string) so it is unit-testable; numbers use thousands separators
   with `CultureInfo.InvariantCulture`; times shown in local time `HH:mm:ss`.
5. Help: add `stats` to the command list in `CmdHelp` and a per-command help entry; README: a short "Live stats" section.
   Bump version to 1.2.0 in `Program.PrintVersion` and anywhere else the version string lives (grep for `1.1.0`).
6. Tests: renderer output for idle / prompt-processing / generating / no-model; LiveRate window maths; arg parsing of the three
   watch spellings; non-tty line format.
