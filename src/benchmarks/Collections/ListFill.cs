public class ListFill
{
    private const int value = 42;
    private static readonly List<int> list = Enumerable.Range(0, 10000).ToList();

    [Benchmark]
    public void ListFillNaive()
    {
        for (int i = 0; i < list.Count; i++)
        {
            list[i] = value;
        }
    }

    [Benchmark(Baseline = true)]
    public void ListFillSpan() => list.Fill(value); // from CollectionExtensions
}
