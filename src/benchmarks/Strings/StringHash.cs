using System.Security.Cryptography;
using System.Text;

[MemoryDiagnoser]
public class StringHash
{
    [ParamsSource(nameof(Values))]
    public string text = string.Empty;

    public static IEnumerable<string> Values => new[]
    {
        "abcdefghijklmnopqrstuvwxyz",
        string.Join("", Enumerable.Repeat("abcdefghijklmnopqrstuvwxyz", 1000)),
    };

    [Benchmark]
    public string Sha256Naive()
    {
        var inputBytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(inputBytes);
        return Convert.ToHexString(hash);
    }

    [Benchmark(Baseline = true)]
    public string Sha256Span() => text.AsSpan().GetSha256(); // from StringExtensions

}
