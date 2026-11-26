using System;
using System.Collections.Generic;

namespace Veron;

public class ModelConfig
{
    public string  ModelPath     { get; set; } = "";
    public string  Alias         { get; set; } = "";
    public int     Port          { get; set; } = 5570;
    public int     Context       { get; set; } = 128000;
    public bool    Jinja         { get; set; } = true;
    public bool    Fa            { get; set; } = true;
    public float   RepeatPenalty { get; set; } = 1.05f;
    public int?    NGpuLayers    { get; set; }
    public int?    BatchSize     { get; set; }
    public int     Wait          { get; set; } = 120;

    /// <summary>Extra llama-server arguments from <c>PARAMETER args</c> (appended verbatim).</summary>
    public List<string> ExtraArgs { get; set; } = new();

    /// <summary>Per-profile idle unload time from <c>PARAMETER keep_alive</c>; null = daemon default.</summary>
    public TimeSpan? KeepAlive   { get; set; }

    public ModelConfig Clone()
    {
        var copy = (ModelConfig)MemberwiseClone();
        copy.ExtraArgs = new List<string>(ExtraArgs);
        return copy;
    }
}
