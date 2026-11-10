namespace Sigil.Sound.Tests;

/// <summary>
/// Asserts that a piece of work allocates nothing on the calling thread, which is the audio-thread rule for <c>RenderBlock</c>.
/// </summary>
/// <remarks>
/// A single measurement is not reliable on its own. <see cref="GC.GetAllocatedBytesForCurrentThread"/> reads a counter that the runtime
/// adjusts when a garbage collection (started by other test classes running in parallel) or other runtime work touches the thread's
/// allocation chunk, so a loop that allocates nothing can still read up to one chunk (about 8 KB) once in a while. Across 60 full runs
/// of this project about one run in ten failed one of these tests, in a different test each time, and the byte counts repeated.
/// <para>
/// So the work is measured up to <paramref name="attempts"/> times and the assertion passes if any attempt allocates nothing. Code that
/// really allocates does so in every attempt, so a real leak still fails; a one-off disturbance cannot fail a test.
/// </para>
/// </remarks>
internal static class AllocationAssert
{
    /// <summary>Passes when <paramref name="work"/> allocates nothing on this thread in at least one of <paramref name="attempts"/> runs.</summary>
    public static void None(Action work, int attempts = 5) =>
        NoneOf(() =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            work();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }, attempts);

    /// <summary>
    /// Like <see cref="None"/>, for work where only parts of one attempt should be measured (setup that may allocate sits between them).
    /// <paramref name="attempt"/> runs the work once and returns the bytes allocated by the parts that count.
    /// </summary>
    public static void NoneOf(Func<long> attempt, int attempts = 5)
    {
        long smallest = long.MaxValue;
        for (int i = 0; i < attempts; i++)
        {
            long allocated = attempt();
            if (allocated == 0)
                return;
            smallest = Math.Min(smallest, allocated);
        }

        Assert.Fail($"Allocated at least {smallest} bytes on the calling thread in each of {attempts} attempts; expected none.");
    }
}
