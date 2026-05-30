namespace Veron;

class ModelConfig
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
    public int     Wait          { get; set; } = 30;

    // New fields for 'run' command
    public bool?   Color         { get; set; } = true;
    public float?  Temperature   { get; set; }
    public float?  TopP          { get; set; }
    public string? Prompt        { get; set; }
}
