using Xunit.Sdk;

namespace Sigil.Sound.Tests;

/// <summary>
/// The allocation tests retry, so these check that retrying did not blunt them: real allocation still fails, a clean loop still passes,
/// and one stray allocation does not fail a test.
/// </summary>
public class AllocationAssertTests
{
    // Keeps the allocation observable so the JIT cannot remove it.
    private static byte[]? _sink;

    [Fact]
    public void Work_that_allocates_every_time_fails()
    {
        var error = Assert.Throws<FailException>(() => AllocationAssert.None(() =>
        {
            for (int i = 0; i < 100; i++)
                _sink = new byte[64];
        }));

        Assert.Contains("each of 5 attempts", error.Message);
    }

    [Fact]
    public void Work_that_allocates_a_little_in_every_attempt_fails()
    {
        // One small allocation per attempt is the leak this guards against, and it must not slip through.
        Assert.Throws<FailException>(() => AllocationAssert.None(() => _sink = new byte[16]));
    }

    [Fact]
    public void Work_that_allocates_nothing_passes()
    {
        var total = 0;
        AllocationAssert.None(() =>
        {
            for (int i = 0; i < 1000; i++)
                total += i;
        });

        Assert.True(total > 0);
    }

    [Fact]
    public void One_stray_allocation_in_the_first_attempt_does_not_fail()
    {
        var attempts = 0;
        AllocationAssert.None(() =>
        {
            if (attempts++ == 0)
                _sink = new byte[4096];
        });

        Assert.Equal(2, attempts);
    }

    [Fact]
    public void The_parts_that_count_can_be_measured_separately()
    {
        var attempts = 0;
        AllocationAssert.NoneOf(() =>
        {
            _sink = new byte[1024]; // outside the measured part
            long before = GC.GetAllocatedBytesForCurrentThread();
            attempts++;
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });

        Assert.Equal(1, attempts);
    }
}
