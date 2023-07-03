public class ListMultiply
{
    private const int factor = 42;
    private readonly List<int> list = Enumerable.Range(0, 10000).ToList();

    [Benchmark]
    public void ListMultiplyFor()
    {
        for (int i = 0; i < list.Count; i++)
        {
            list[i] *= factor;
        }
    }

    [Benchmark(Baseline = true)]
    public void ListMultiplySpan() => list.Multiply(factor); // from CollectionExtensions
}
