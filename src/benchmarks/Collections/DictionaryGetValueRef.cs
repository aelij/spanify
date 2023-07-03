public class DictionaryGetValueRef
{
    private static readonly List<int> a = Enumerable.Range(0, 10000).ToList();
    private static readonly List<int> b = Enumerable.Range(0, 10000).Reverse().ToList();

    [Benchmark]
    public bool UnorderedSequenceEqualNaive() => UnorderedSequenceEqualNaive(a, b);

    [Benchmark(Baseline = true)]
    public bool UnorderedSequenceEqualRef() => a.UnorderedSequenceEqual(b); // from CollectionExtensions

    private static bool UnorderedSequenceEqualNaive(IEnumerable<int> a, IEnumerable<int> b)
    {
        var counts = new Dictionary<int, int>();

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
