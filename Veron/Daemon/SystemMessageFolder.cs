using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Veron.Daemon;

/// <summary>
/// Claude Code (2.1.x) sends parts of its context — the "# Environment" block, SessionStart hook
/// output — as <c>role: "system"</c> entries inside <c>messages</c> of an Anthropic /v1/messages
/// request. llama-server renders those through the model's chat template, and templates such as
/// Qwen's reject any system message that is not first ("System message must be at the beginning"),
/// failing every request with HTTP 500.
///
/// This folds each such message into the adjacent user turn as a <c>&lt;system-reminder&gt;</c>
/// text block: appended to the preceding user turn, else merged into the next user turn, else
/// inserted as a user turn of its own. The top-level <c>system</c> field is left alone.
/// </summary>
public static class SystemMessageFolder
{
    static readonly JsonSerializerOptions Out = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Returns the rewritten body, or null when there was nothing to fold (send the original bytes).</summary>
    public static byte[]? Fold(byte[] body, out int folded)
    {
        folded = 0;
        if (body.Length == 0) return null;

        JsonObject? root;
        try { root = JsonNode.Parse(body) as JsonObject; }
        catch (JsonException) { return null; }

        if (root?["messages"] is not JsonArray messages ||
            !messages.Any(m => m is JsonObject o && IsSystem(o)))
            return null;

        var input = messages.ToList();
        messages.Clear(); // detaches the nodes so they can be re-added

        var pending = new List<JsonNode>();
        JsonObject? lastOut = null;

        foreach (var node in input)
        {
            if (node is JsonObject msg && IsSystem(msg))
            {
                folded++;
                var reminder = Reminder(msg["content"]);
                if (lastOut is not null && IsRole(lastOut, "user")) Blocks(lastOut).Add(reminder);
                else pending.Add(reminder);
                continue;
            }

            if (pending.Count > 0)
            {
                if (node is JsonObject user && IsRole(user, "user"))
                {
                    var blocks = Blocks(user);
                    foreach (var b in pending) blocks.Add(b);
                }
                else
                {
                    messages.Add(UserTurn(pending));
                }
                pending.Clear();
            }

            messages.Add(node);
            lastOut = node as JsonObject;
        }

        if (pending.Count > 0) messages.Add(UserTurn(pending));

        return JsonSerializer.SerializeToUtf8Bytes(root, Out);
    }

    static bool IsSystem(JsonObject msg) => IsRole(msg, "system");

    static bool IsRole(JsonObject msg, string role) =>
        msg["role"] is JsonValue v && v.TryGetValue<string>(out var r) && r == role;

    static JsonObject UserTurn(List<JsonNode> blocks) => new()
    {
        ["role"]    = "user",
        ["content"] = new JsonArray(blocks.ToArray()),
    };

    /// <summary>The message's content as a block array, converting a string content in place.</summary>
    static JsonArray Blocks(JsonObject msg)
    {
        switch (msg["content"])
        {
            case JsonArray arr:
                return arr;
            case JsonValue v when v.TryGetValue<string>(out var s):
                var converted = new JsonArray(TextBlock(s));
                msg["content"] = converted;
                return converted;
            default:
                var empty = new JsonArray();
                msg["content"] = empty;
                return empty;
        }
    }

    static JsonObject Reminder(JsonNode? content)
    {
        string text = content switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonArray arr => string.Join("\n", arr.OfType<JsonObject>()
                .Where(b => b["type"] is JsonValue t && t.TryGetValue<string>(out var type) && type == "text")
                .Select(b => b["text"] is JsonValue tv && tv.TryGetValue<string>(out var s) ? s : "")),
            _ => "",
        };
        return TextBlock("<system-reminder>\n" + text + "\n</system-reminder>");
    }

    static JsonObject TextBlock(string text) => new() { ["type"] = "text", ["text"] = text };
}
