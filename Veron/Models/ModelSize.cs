using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Veron;

/// <summary>
/// On-disk size of a model. A split GGUF (<c>name-00001-of-00004.gguf</c>) is loaded from its first
/// shard, which holds only metadata, so its size is the sum of all shards.
/// </summary>
static partial class ModelSize
{
    [GeneratedRegex(@"^(?<prefix>.*)-(?<idx>\d{5})-of-(?<count>\d{5})\.gguf$", RegexOptions.IgnoreCase)]
    private static partial Regex SplitName();

    /// <summary>Total bytes of the model, or null if the file (or any of its shards) is missing.</summary>
    public static long? TotalBytes(string path)
    {
        if (!File.Exists(path)) return null;

        var m = SplitName().Match(Path.GetFileName(path));
        if (!m.Success || int.Parse(m.Groups["idx"].Value, CultureInfo.InvariantCulture) != 1)
            return new FileInfo(path).Length;

        string dir    = Path.GetDirectoryName(path) ?? "";
        string prefix = m.Groups["prefix"].Value;
        string countS = m.Groups["count"].Value;
        int    count  = int.Parse(countS, CultureInfo.InvariantCulture);

        long total = 0;
        for (int i = 1; i <= count; i++)
        {
            string shard = Path.Join(dir, $"{prefix}-{i.ToString("D5", CultureInfo.InvariantCulture)}-of-{countS}.gguf");
            if (!File.Exists(shard)) return null;
            total += new FileInfo(shard).Length;
        }
        return total;
    }
}
