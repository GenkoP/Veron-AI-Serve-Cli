using Veron;
using Xunit;

namespace Veron.Tests;

public class PsCommandTests
{
    static Veron.Daemon.ModelStatus Status(DateTimeOffset? expires, bool forever = false, int active = 0, int sessions = 0) =>
        new("m", "m.gguf", 1, 1234, 4096, DateTimeOffset.Now, expires, forever, active, sessions, "/tmp/m.log");

    [Fact]
    public void FormatUntil_Shows_Minutes_Left()
    {
        var now = DateTimeOffset.Now;
        Assert.Equal("4 minutes from now", CmdPs.FormatUntil(Status(now.AddMinutes(4)), now));
        Assert.Equal("1 minute from now", CmdPs.FormatUntil(Status(now.AddSeconds(70)), now));
        Assert.Equal("30 seconds from now", CmdPs.FormatUntil(Status(now.AddSeconds(30)), now));
    }

    [Fact]
    public void FormatUntil_Special_States()
    {
        var now = DateTimeOffset.Now;
        Assert.Equal("Forever", CmdPs.FormatUntil(Status(null, forever: true), now));
        Assert.Equal("In use (2 sessions)", CmdPs.FormatUntil(Status(null, sessions: 2), now));
        Assert.Equal("In use (1 session)", CmdPs.FormatUntil(Status(null, sessions: 1), now));
        Assert.Equal("Busy", CmdPs.FormatUntil(Status(null, active: 1), now));
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
