using System;
using System.IO;
using Veron;
using Xunit;

namespace Veron.Tests;

public class CatCommandTests
{
    [Fact]
    public void CmdCat_Preserves_Modelfile_Content()
    {
        string testName = "cat-test-" + Guid.NewGuid().ToString("N")[..8];
        string content = $"FROM model-{testName}.gguf\nPARAMETER port 5570";
        string mfPath = Path.Join(Paths.ModelfilesDir, testName);

        try
        {
            Directory.CreateDirectory(Paths.ModelfilesDir);
            File.WriteAllText(mfPath, content);

            using var sw = new StringWriter();
            Console.SetOut(sw);
            CmdCat.Run(testName);

            string output = sw.ToString();
            Assert.Equal(content, output);
        }
        finally
        {
            File.Delete(mfPath);
        }
    }

    [Fact]
    public void CmdCat_Exit1_For_Nonexistent_Modelfile()
    {
        string testName = "cat-test-nonexistent-" + Guid.NewGuid().ToString("N")[..8];

        var ex = Assert.ThrowsAny<Environment.ExitException>(() => CmdCat.Run(testName));
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public void CmdCat_Resolved_By_Stem_Name()
    {
        string testName = "cat-stem-test-" + Guid.NewGuid().ToString("N")[..8];
        string content = $"FROM model-{testName}.gguf";
        string mfPath = Path.Join(Paths.ModelfilesDir, testName + ".modelfile");

        try
        {
            Directory.CreateDirectory(Paths.ModelfilesDir);
            File.WriteAllText(mfPath, content);

            using var sw = new StringWriter();
            Console.SetOut(sw);
            CmdCat.Run(testName);

            string output = sw.ToString();
            Assert.Equal(content, output);
        }
        finally
        {
            File.Delete(mfPath);
        }
    }
}
