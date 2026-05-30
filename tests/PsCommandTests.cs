using Veron;
using Xunit;

namespace Veron.Tests;

public class PsCommandTests
{
    [Fact]
    public void ListRunningServers_Returns_Empty_When_Nothing_Running()
    {
        var servers = StateManager.ListRunningServers();
        Assert.Empty(servers);
    }

    [Fact]
    public void FormatStartedTime_SameDay_Shows_HH_MM()
    {
        var today = DateTime.Today;
        var result = CmdPs.FormatStartedTime(today.AddHours(14).AddMinutes(30));
        Assert.Equal("14:30", result);
    }

    [Fact]
    public void FormatStartedTime_DifferentDay_Shows_Full_Date()
    {
        var yesterday = DateTime.Today.AddDays(-1).AddHours(10).AddMinutes(15);
        var result = CmdPs.FormatStartedTime(yesterday);

        Assert.Contains("10:15", result);
        Assert.NotEqual("10:15", result); // should be longer than just time
    }
}
