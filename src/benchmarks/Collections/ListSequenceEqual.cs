public class ListSequenceEqual
{
    private static readonly List<int> listA = Enumerable.Range(0, 10000).ToList();
    private static readonly List<int> listB = Enumerable.Range(0, 10000).ToList();

    [Benchmark]
    public bool ListEqualsEnumerable() => listA.AsEnumerable().SequenceEqual(listB);

    [Benchmark(Baseline = true)]
    public bool ListEqualsSpan() => listA.SequenceEqual(listB); // from CollectionExtensions

}
