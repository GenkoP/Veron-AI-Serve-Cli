using System;
using System.Collections.Generic;

namespace Veron;

static class CliParser
{
    public static Dictionary<string, string> ParseOpts(ReadOnlySpan<string> args)
    {
        var dict = new Dictionary<string, string>();
        bool gotModel = false;
        bool gotSourcePath = false;
        for (int i = 0; i < args.Length; i++)
        {
            // First positional arg = model name
            if (!gotModel && !args[i].StartsWith("-"))
            {
                dict["model"] = args[i];
                gotModel = true;
                continue;
            }

            // Second positional arg = source path (used by 'create')
            if (!gotSourcePath && !args[i].StartsWith("-"))
            {
                dict["source-path"] = args[i];
                gotSourcePath = true;
                continue;
            }

            if (!args[i].StartsWith("--")) continue;

            string key = args[i][2..];

            // --key=value
            int eqPos = key.IndexOf('=');
            if (eqPos >= 0) { dict[key[..eqPos]] = key[(eqPos + 1)..]; continue; }

            // --key value  OR  --key (boolean true)
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                { i++; dict[key] = args[i]; }
            else dict[key] = "true";
        }
        return dict;
    }

    public static bool OptsBool(Dictionary<string, string> opts, string key) =>
        opts.TryGetValue(key, out var v) && v == "true";

    public static string ExpandEnv(string path) =>
        path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     StringComparison.Ordinal);

    public static string EscapeArg(string arg) => arg.Contains(' ') ? "\"" + arg + "\"" : arg;
}
