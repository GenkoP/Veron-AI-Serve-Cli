using Veron;
using Xunit;

namespace Veron.Tests;

public class HelpCommandTests
{
    [Fact]
    public void CmdHelp_Has_Entries_For_All_Commands()
    {
        var commands = new[] { "cat", "ls", "list", "create", "serve", "claude", "copilot", "run", "ps", "stop", "remove", "rm", "help", "version" };

        foreach (var cmd in commands)
        {
            Assert.NotNull(CmdHelp.Get(cmd));
        }
    }
}
