using Veron;
using Xunit;

namespace Veron.Tests;

public class ModelSizeTests : IDisposable
{
    readonly string _dir = Path.Join(Path.GetTempPath(), "veron-modelsize-" + Guid.NewGuid());

    public ModelSizeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Make(string name, int bytes)
    {
        string p = Path.Join(_dir, name);
        File.WriteAllBytes(p, new byte[bytes]);
        return p;
    }

    [Fact]
    public void Single_File_Returns_Its_Length()
    {
        string p = Make("model-Q4_K_M.gguf", 1234);
        Assert.Equal(1234, ModelSize.TotalBytes(p));
    }

    [Fact]
    public void Split_First_Shard_Sums_All_Shards()
    {
        string first = Make("Big-UD-Q4_K_XL-00001-of-00003.gguf", 10);
        Make("Big-UD-Q4_K_XL-00002-of-00003.gguf", 200);
        Make("Big-UD-Q4_K_XL-00003-of-00003.gguf", 3000);
        Assert.Equal(3210, ModelSize.TotalBytes(first));
    }

    [Fact]
    public void Split_With_Missing_Shard_Returns_Null()
    {
        string first = Make("Big-00001-of-00003.gguf", 10);
        Make("Big-00003-of-00003.gguf", 3000);
        Assert.Null(ModelSize.TotalBytes(first));
    }

    [Fact]
    public void Missing_File_Returns_Null()
    {
        Assert.Null(ModelSize.TotalBytes(Path.Join(_dir, "nope.gguf")));
    }

    [Fact]
    public void Later_Shard_Named_Directly_Counts_Only_Itself()
    {
        // only the first shard is a valid -m target; a later one is reported as-is
        Make("Big-00001-of-00002.gguf", 10);
        string second = Make("Big-00002-of-00002.gguf", 200);
        Assert.Equal(200, ModelSize.TotalBytes(second));
    }
}
