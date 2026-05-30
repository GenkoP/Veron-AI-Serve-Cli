using System;
using System.IO;

namespace Veron;

static class CmdCat
{
    public static void Run(string name, Action<int>? exitAction = null)
    {
        string? mfPath = ModelfileParser.FindModelfile(name);

        if (mfPath is null)
        {
            Console.Error.WriteLine("No modelfile found for '" + name + "' in " + Paths.ModelfilesDir);
            (exitAction ?? Environment.Exit)(1);
            return;
        }

        Console.Write(File.ReadAllText(mfPath));
    }
}
