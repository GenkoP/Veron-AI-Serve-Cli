using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Veron.Daemon;

namespace Veron;

/// <summary>
/// `veron run <name>` — chat with a model through the Veron server (like `ollama run`).
/// The model stays loaded afterwards and unloads after the keep-alive time.
/// </summary>
static class CmdRun
{
    const string Dim   = "\u001b[2m";
    const string Reset = "\u001b[0m";

    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        var model  = ToolSession.ResolveOrExit(opts, modelsDir, "run");
        var client = ToolSession.ConnectOrExit(modelsDir);

        var sampling = new JsonObject();
        if (opts.TryGetValue("temperature", out var t)) sampling["temperature"] = ParseFloat("temperature", t);
        if (opts.TryGetValue("top-p", out var tp))      sampling["top_p"]       = ParseFloat("top-p", tp);
        bool color = !CliParser.OptsBool(opts, "no-color") && !Console.IsOutputRedirected;

        string? oneShot = opts.GetValueOrDefault("prompt");
        if (oneShot is null) Console.WriteLine("Loading " + model.Name + " ...");
        try { client.Load(model.Name); }
        catch (DaemonException ex) { Console.Error.WriteLine("Error: " + ex.Message); Environment.Exit(1); }

        using var http = new HttpClient { BaseAddress = new Uri(client.BaseUrl), Timeout = Timeout.InfiniteTimeSpan };
        var history = new List<JsonObject>();

        if (oneShot is not null)
        {
            history.Add(Message("user", oneShot));
            bool ok = Chat(http, model.Name, history, sampling, color, CancellationToken.None, out _);
            if (!ok) Environment.Exit(1);
            return;
        }

        Console.WriteLine("Chatting with " + model.Name + ". Type /bye to exit, /clear to reset the conversation.");

        CancellationTokenSource? generation = null;
        Console.CancelKeyPress += (_, e) =>
        {
            // Ctrl+C stops the current answer; at the prompt it exits
            if (generation is { IsCancellationRequested: false }) { e.Cancel = true; generation.Cancel(); }
        };

        while (true)
        {
            Console.Write(">>> ");
            string? line = Console.ReadLine();
            if (line is null) { Console.WriteLine(); break; } // Ctrl+D
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line is "/bye" or "/exit" or "/quit") break;
            if (line == "/clear") { history.Clear(); Console.WriteLine("Conversation cleared."); continue; }

            history.Add(Message("user", line));
            generation = new CancellationTokenSource();
            bool ok = Chat(http, model.Name, history, sampling, color, generation.Token, out string answer);
            generation = null;

            if (ok) history.Add(Message("assistant", answer));
            else    history.RemoveAt(history.Count - 1);
        }
    }

    static JsonObject Message(string role, string content) => new() { ["role"] = role, ["content"] = content };

    static float ParseFloat(string name, string value)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return f;
        Console.Error.WriteLine($"Error: invalid --{name} '{value}'");
        Environment.Exit(1);
        return 0;
    }

    /// <summary>Streams one chat completion to the console. Returns false on error.</summary>
    static bool Chat(HttpClient http, string model, List<JsonObject> history, JsonObject sampling,
                     bool color, CancellationToken ct, out string answer)
    {
        var body = new JsonObject
        {
            ["model"]    = model,
            ["stream"]   = true,
            ["messages"] = new JsonArray(history.ConvertAll(m => (JsonNode)m.DeepClone()).ToArray()),
        };
        foreach (var (k, v) in sampling) body[k] = v?.DeepClone();

        var content = new StringBuilder();
        bool inReasoning = false;
        answer = "";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
            { Content = JsonContent.Create(body) };
            using var resp = http.Send(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!resp.IsSuccessStatusCode)
            {
                string err = resp.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
                Console.Error.WriteLine("Error: " + ErrorText(err, (int)resp.StatusCode));
                return false;
            }

            using var abort  = ct.Register(resp.Dispose); // Ctrl+C must interrupt a blocking read
            using var reader = new StreamReader(resp.Content.ReadAsStream(ct));
            while (reader.ReadLine() is { } line)
            {
                ct.ThrowIfCancellationRequested();
                if (!line.StartsWith("data:")) continue;
                string data = line[5..].Trim();
                if (data == "[DONE]") break;

                JsonNode? delta;
                try { delta = JsonNode.Parse(data)?["choices"]?[0]?["delta"]; }
                catch (JsonException) { continue; }
                if (delta is null) continue;

                if (delta["reasoning_content"]?.GetValue<string>() is { Length: > 0 } r)
                {
                    if (!inReasoning && color) Console.Write(Dim);
                    inReasoning = true;
                    Console.Write(r);
                }
                if (delta["content"]?.GetValue<string>() is { Length: > 0 } c)
                {
                    if (inReasoning) { Console.Write(color ? Reset + "\n\n" : "\n\n"); inReasoning = false; }
                    Console.Write(c);
                    content.Append(c);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException ||
                                   (ct.IsCancellationRequested && ex is IOException or ObjectDisposedException or HttpRequestException))
        {
            if (inReasoning && color) Console.Write(Reset);
            Console.WriteLine("\n[stopped]");
            answer = content.ToString();
            return content.Length > 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            if (inReasoning && color) Console.Write(Reset);
            Console.Error.WriteLine("\nError: " + ex.Message);
            return false;
        }

        if (inReasoning && color) Console.Write(Reset);
        Console.WriteLine();
        answer = content.ToString();
        return true;
    }

    static string ErrorText(string body, int status)
    {
        try
        {
            var msg = JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>();
            if (msg is not null) return msg;
        }
        catch (JsonException) { }
        return "HTTP " + status + " " + body;
    }
}
