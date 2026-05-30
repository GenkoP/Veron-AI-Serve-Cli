using System.Collections.Generic;
using System.IO;

namespace Veron;

static class CmdCreate
{
    public static void Run(Dictionary<string, string> opts, string modelsDir)
    {
        string? name = opts.GetValueOrDefault("model");
        string? sourcePath = opts.GetValueOrDefault("source-path");

        if (name is null || sourcePath is null)
        {
            Console.Error.WriteLine("Error: 'create' requires <name> and <path-to-modelfile>");
            Console.Error.WriteLine("Usage: veron create <name> <path-to-modelfile>");
            Environment.Exit(1);
        }

        // Source file must exist
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Error: source modelfile not found: {sourcePath}");
            Environment.Exit(1);
        }

        // Validate
        var errors = ModelfileValidator.ValidateModelfile(sourcePath, name, modelsDir);
        if (errors.Count > 0)
        {
            foreach (var err in errors)
                Console.Error.WriteLine(err);
            Environment.Exit(1);
        }

        // Ensure destination directory exists
        Directory.CreateDirectory(ModelfilesDir);

        // Copy (overwrite if exists — allows updates)
        string destPath = Path.Join(ModelfilesDir, name);
        File.Copy(sourcePath, destPath, overwrite: true);

        Console.WriteLine($"Using modelfile: {sourcePath}");
        Console.WriteLine($"Creating profile \"{name}\" -> {destPath} ✓");
    }
}
