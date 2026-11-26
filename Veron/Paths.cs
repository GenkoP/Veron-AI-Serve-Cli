using System;
using System.IO;

namespace Veron;

static class Paths
{
    public static readonly string VeronDir = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".veron");
    public static readonly string ModelfilesDir   = Path.Join(VeronDir, "modelfiles");
    public static readonly string LogsDir         = Path.Join(VeronDir, "logs");
    public static readonly string DaemonStateFile = Path.Join(VeronDir, "daemon.json");
    public static readonly string ServerLogFile   = Path.Join(LogsDir, "server.log");
}
