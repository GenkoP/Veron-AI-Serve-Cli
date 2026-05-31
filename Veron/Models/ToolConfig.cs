using System.Collections.Generic;

namespace Veron;

public class ToolConfig
{
    public string Name                           { get; set; } = "";
    public Dictionary<string, string?> Parameters { get; set; } = new();
}
