using System;

namespace Veron;

public class ServerState
{
    public string Model { get; set; } = "";
    public string From { get; set; } = "";
    public int Port { get; set; }
    public int Context { get; set; }
    public int Pid { get; set; }
    public DateTime StartedAt { get; set; }
}
