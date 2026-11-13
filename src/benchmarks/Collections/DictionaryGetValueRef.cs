public class DictionaryGetValueRef
{
    private static readonly List<string> a = Enumerable.Range(0, 10000)
        .Select(i => $"https://example.com/api/catalog/products/{i % 1000:D4}/availability?region=north-america&currency=USD")
        .ToList();
    private static readonly List<string> b = a.AsEnumerable().Reverse().ToList();

    [Benchmark]
    public bool UnorderedSequenceEqualNaive() => UnorderedSequenceEqualNaive(a, b);

    [Benchmark(Baseline = true)]
    public bool UnorderedSequenceEqualRef() => a.UnorderedSequenceEqual(b); // from CollectionExtensions

    private static bool UnorderedSequenceEqualNaive(IEnumerable<string> a, IEnumerable<string> b)
    {
        var counts = new Dictionary<string, int>(a.TryGetNonEnumeratedCount(out var capacity) ? capacity : 0);

        foreach (var item in a)
        {
            counts[item] = (counts.TryGetValue(item, out var count) ? count : 0) + 1;
        }

        foreach (var item in b)
        {
            if (!counts.TryGetValue(item, out var value))
                return false;

            counts[item] = value - 1;
        }

        foreach (var item in counts)
        {
            if (item.Value != 0)
            {
                return false;
            }
        }

        return true;
    }
}
