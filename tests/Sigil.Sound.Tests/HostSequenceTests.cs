using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class HostSequenceTests
{
    private static Sound Gate(Voice v) => Constant(1).Adsr(0, 0, 1, 0);

    private static Pattern Written() => Notes.Parse("C4 E4").Every(Beats.Quarter).Loop();

    [Fact]
    public void A_pattern_played_without_a_tempo_reports_that_it_follows_the_host()
    {
        var reports = 0;
        using (HostSequence.Observe(() => reports++))
            Written().Play(Gate);

        Assert.Equal(1, reports);
    }

    [Fact]
    public void A_note_pattern_played_without_a_tempo_reports_too()
    {
        var reports = 0;
        using (HostSequence.Observe(() => reports++))
            Mini.Notes("c3 e3").Play(Gate);

        Assert.Equal(1, reports);
    }

    [Fact]
    public void A_pattern_with_its_own_tempo_does_not_report()
    {
        var reports = 0;
        using (HostSequence.Observe(() => reports++))
        {
            Written().Play(Gate, bpm: 120);
            Mini.Notes("c3 e3").Play(Gate, bpm: 120);
        }

        Assert.Equal(0, reports);
    }

    [Fact]
    public void Disposing_the_scope_stops_the_reports()
    {
        var reports = 0;
        using (HostSequence.Observe(() => reports++)) { }
        Written().Play(Gate);

        Assert.Equal(0, reports);
    }

    [Fact]
    public void Scopes_nest_and_restore_the_outer_observer()
    {
        var outer = 0;
        var inner = 0;
        using (HostSequence.Observe(() => outer++))
        {
            using (HostSequence.Observe(() => inner++))
                Written().Play(Gate);
            Written().Play(Gate);
        }

        Assert.Equal(1, inner);
        Assert.Equal(1, outer);
    }

    [Fact]
    public void An_observer_only_hears_the_thread_it_was_set_on()
    {
        var reports = 0;
        using (HostSequence.Observe(() => Interlocked.Increment(ref reports)))
        {
            // A thread of its own, so it cannot be the one the observer was set on. A pool task could be: after an await
            // the pool may run the lambda on the very thread that is still waiting here, and the test would hear itself.
            var other = new Thread(() => Written().Play(Gate));
            other.Start();
            other.Join();
        }

        Assert.Equal(0, reports);
    }

    [Fact]
    public void A_null_observer_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => HostSequence.Observe(null!));
    }
}
