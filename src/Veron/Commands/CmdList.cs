using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Veron;

static class CmdList
{
    public static void Run(string modelsDir)
    {
        if (!Directory.Exists(ModelfilesDir))
        {
            Console.WriteLine("No modelfiles found in " + ModelfilesDir);
            return;
        }

        var files = Directory.GetFiles(ModelfilesDir, "*", SearchOption.TopDirectoryOnly)
                            .OrderBy(p => p).ToArray();
        if (files.Length == 0)
        {
            Console.WriteLine("No modelfiles found in " + ModelfilesDir);
            return;
        }

        // Parse each modelfile to show the FROM target
        var entries = new List<(string name, string fromTarget)>();
        foreach (var f in files)
        {
            string name = Path.GetFileName(f);
            string fromTarget = "";

            try
            {
                foreach (var line in File.ReadLines(f))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                    if (trimmed.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
                    {
                        fromTarget = trimmed[5..].Trim().Trim('"').Trim('\'');
                        break;
                    }
                }
            }
            catch { /* skip files we can't read */ }

            entries.Add((name, fromTarget));
        }

        int nameW = entries.Max(e => e.name.Length);
        int fromW = entries.Max(e => e.fromTarget.Length);
        string header = "NAME".PadLeft(nameW) + "  FROM";
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));
        foreach (var (name, fromTarget) in entries)
            Console.WriteLine(name.PadLeft(nameW) + "  " + fromTarget);

        Console.WriteLine();
        Console.WriteLine("Total: " + entries.Count + " modelfile(s)");
    }
}
