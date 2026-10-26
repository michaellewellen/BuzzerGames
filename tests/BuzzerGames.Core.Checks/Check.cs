using BuzzerGames.Core.Jeopardy;

/// <summary>A tiny stand-in for a test framework: each helper throws with a clear message if it is wrong.</summary>
static class Check
{
    public static void True(bool condition, string what)
    {
        if (!condition) throw new Exception($"Expected: {what}");
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: expected {expected}, got {actual}");
    }

    public static void Ok(IResult result)
    {
        if (!result.Ok) throw new Exception($"Expected success but it failed: {Describe(result)}");
    }

    public static void Fails(IResult result, string what)
    {
        if (result.Ok) throw new Exception($"Expected a failure ({what}) but it succeeded");
    }

    private static string Describe(IResult result) => result switch
    {
        Result r => r.Error ?? "(no message)",
        JoinResult j => j.Error ?? "(no message)",
        _ => "(unknown)"
    };
}
