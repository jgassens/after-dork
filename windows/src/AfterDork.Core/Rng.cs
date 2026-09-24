namespace AfterDork;

/// <summary>
/// The savers' random source (Swift's SystemRandomNumberGenerator). Unseeded
/// by default; Seed() makes renders and tests reproducible.
/// </summary>
public static class Rng
{
    [ThreadStatic] static Random? random;
    static int? seed;

    static Random R => random ??= seed is int s ? new Random(s) : new Random();

    public static void Seed(int s) { seed = s; random = new Random(s); }
    public static void Unseed() { seed = null; random = new Random(); }

    /// <summary>CGFloat/Double.random(in: lo...hi).</summary>
    public static double Range(double lo, double hi) => lo + R.NextDouble() * (hi - lo);

    /// <summary>Int.random(in: lo..&lt;hi).</summary>
    public static int Int(int lo, int hiExclusive) => R.Next(lo, hiExclusive);

    /// <summary>Int.random(in: lo...hi).</summary>
    public static int IntInclusive(int lo, int hi) => R.Next(lo, hi + 1);

    public static bool Bool() => R.Next(2) == 1;

    /// <summary>Double.random(in: 0...1) &lt; p.</summary>
    public static bool Chance(double p) => R.NextDouble() < p;

    /// <summary>randomElement()! — throws on an empty list, as Swift would crash.</summary>
    public static T Element<T>(IReadOnlyList<T> list) => list[R.Next(list.Count)];

    public static T? ElementOrDefault<T>(IReadOnlyList<T> list) => list.Count == 0 ? default : list[R.Next(list.Count)];

    /// <summary>shuffled(): returns a new shuffled array.</summary>
    public static T[] Shuffled<T>(IEnumerable<T> items)
    {
        var a = items.ToArray();
        R.Shuffle(a);
        return a;
    }
}
