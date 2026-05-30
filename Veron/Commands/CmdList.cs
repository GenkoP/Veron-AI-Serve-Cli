using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Veron;

static class CmdList
{
    public static void Run(string modelsDir)
    {
        if (!Directory.Exists(Paths.ModelfilesDir))
        {
            Console.WriteLine("No modelfiles found in " + Paths.ModelfilesDir);
            return;
        }

        var files = Directory.GetFiles(Paths.ModelfilesDir, "*", SearchOption.TopDirectoryOnly)
                            .OrderBy(p => p).ToArray();
        if (files.Length == 0)
        {
            Console.WriteLine("No modelfiles found in " + Paths.ModelfilesDir);
            return;
        }

        // Parse each modelfile to show the FROM target and GGUF file size
        var entries = new List<(string name, string fromTarget, string size)>();
        foreach (var f in files)
        {
            string name = Path.GetFileName(f);
            string fromTarget = "";
            string size = "";

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

                // Resolve the full path and stat the file
                string fullPath = Path.IsPathRooted(fromTarget)
                    ? fromTarget
                    : Path.Combine(modelsDir, fromTarget);

                try
                {
                    long bytes = new FileInfo(fullPath).Length;
                    double gb = bytes / (1024.0 * 1024.0 * 1024.0);
                    size = $"{gb:F1} GB";
                }
                catch { /* file missing or unreadable */ }

                if (string.IsNullOrEmpty(size))
                {
                    size = "missing";
                }
            }
            catch { /* skip files we can't read */ }

            entries.Add((name, fromTarget, size));
        }

        int nameW = entries.Max(e => e.name.Length);
        int fromW = entries.Max(e => e.fromTarget.Length);
        int sizeW = entries.Max(e => e.size.Length);
        string header = "NAME".PadLeft(nameW) + "  FROM".PadRight(fromW + 2) + "SIZE";
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));
        foreach (var (name, fromTarget, size) in entries)
            Console.WriteLine(name.PadLeft(nameW) + "  " + fromTarget.PadRight(fromW + 2) + size);

        Console.WriteLine();
        Console.WriteLine("Total: " + entries.Count + " modelfile(s)");
    }
}
