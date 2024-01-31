using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DotNext.Buffers;

[MemoryDiagnoser]
public partial class StringTryWrite
{
    private static Uri uri = new("https://github.com/aelij/spanify");

    [GeneratedRegex("^https?://(www\\.)?github\\.com(:\\d+)?$", RegexOptions.Compiled)]
    private static partial Regex GitHubUrlMatcher();

    private static IReadOnlyList<Regex> patterns = new[] { GitHubUrlMatcher() };

    [Benchmark]
    public bool MatchesUriPatternNaive() => MatchesUriPatternNaive(patterns, uri);

    [Benchmark(Baseline = true)]
    public bool MatchesUriPatternSpan() => MatchesUriPatternSpan(patterns, uri);

    private static bool MatchesUriPatternNaive(IEnumerable<Regex> patterns, Uri uri)
    {
        var hostAndPort = $"{uri.Scheme}://{uri.Host}:{uri.Port}";

        foreach (var pattern in patterns)
        {
            if (pattern.IsMatch(hostAndPort))
            {
                return true;
            }
        }

        return false;
    }


    [SkipLocalsInit]
    private static bool MatchesUriPatternSpan(IEnumerable<Regex> patterns, Uri uri)
    {
        var maxLength = uri.Scheme.Length + "://".Length + uri.Host.Length + ":".Length + 5;
        using SpanOwner<char> hostAndPort = maxLength <= 256 ? new(stackalloc char[256], maxLength) : new(maxLength);
        hostAndPort.Span.TryWrite($"{uri.Scheme}://{uri.Host}:{uri.Port}", out var written);
        var hostAndPortSpan = hostAndPort.Span[..written];

        foreach (var pattern in patterns)
        {
            if (pattern.IsMatch(hostAndPortSpan))
            {
                return true;
            }
        }

        return false;
    }
}
