using System;
using System.IO;

namespace Veron;

static class Paths
{
    public static readonly string VeronDir = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".veron");
    public static readonly string ModelfilesDir = Path.Join(VeronDir, "modelfiles");
    public static readonly string PidFile = Path.Join(VeronDir, "veron.pid");
}
