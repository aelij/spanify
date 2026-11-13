public class ListSequenceEqual
{
    private const int count = 1000;
    private static readonly List<int> listA = Enumerable.Range(0, 10000).ToList();
    private static readonly List<int> listB = Enumerable.Range(0, 10000).ToList();

    [Benchmark]
    public bool ListPrefixEqualsEnumerable() => listA.Take(count).SequenceEqual(listB.Take(count));

    [Benchmark(Baseline = true)]
    public bool ListPrefixEqualsSpan() => listA.SequenceEqual(listB, count); // from CollectionExtensions

}
