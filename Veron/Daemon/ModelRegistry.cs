using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Veron.Daemon;

/// <summary>A modelfile profile resolved to a llama-server configuration.</summary>
public sealed record ResolvedModel(string Name, string ModelfilePath, ModelConfig Config);

public interface IModelRegistry
{
    /// <summary>Resolves a request's model name (profile name first, then alias; case-insensitive).</summary>
    ResolvedModel? Resolve(string name);

    /// <summary>All profiles that parse successfully.</summary>
    IReadOnlyList<ResolvedModel> List();

    /// <summary>If a profile with this name exists but cannot be served (e.g. its .gguf is missing), why.</summary>
    string? ProfileError(string name);
}

/// <summary>
/// Reads modelfiles from disk on every call, so `veron create` / `veron rm` take effect
/// without restarting the daemon.
/// </summary>
public sealed class ModelRegistry(string modelfilesDir, string modelsDir) : IModelRegistry
{
    public ResolvedModel? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        // 1. profile file name
        string? path = ModelfileParser.FindModelfile(name, modelfilesDir);
        if (path is not null && TryLoad(path) is { } byName) return byName;

        // 2. alias
        return List().FirstOrDefault(m => m.Config.Alias.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ResolvedModel> List()
    {
        if (!Directory.Exists(modelfilesDir)) return Array.Empty<ResolvedModel>();

        return Directory.GetFiles(modelfilesDir)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(TryLoad)
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();
    }

    public string? ProfileError(string name)
    {
        string? path = ModelfileParser.FindModelfile(name, modelfilesDir);
        if (path is null) return null;
        TryLoad(path, out string? error);
        return error is null ? null : $"Profile '{Path.GetFileName(path)}' cannot be served: {error}";
    }

    ResolvedModel? TryLoad(string path) => TryLoad(path, out _);

    ResolvedModel? TryLoad(string path, out string? error)
    {
        error = null;
        try
        {
            var cfg = ModelfileParser.ParseModelfile(path, modelsDir);
            return new ResolvedModel(Path.GetFileName(path), path, cfg);
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            error = ex.Message; // broken or missing model: not servable
            return null;
        }
    }
}
