namespace Sigil.Sound.Tests;

/// <summary>The scheduler behind delays, echoes, strums and lengths: earliest first, and first come first served at the same time.</summary>
public class ScheduledQueueTests
{
    private static NoteEvent Note(long id) => NoteEvent.Start(id, 60);

    [Fact]
    public void Items_come_out_earliest_first_and_in_the_order_added_when_they_are_due_together()
    {
        var random = new Random(11);
        var queue = new ScheduledQueue();
        var added = new List<(double Due, long Id)>();
        for (long id = 0; id < ScheduledQueue.Capacity; id++)
        {
            double due = random.Next(0, 40) / 4.0; // many ties
            Assert.True(queue.Add(due, ScheduledQueue.Op.Start, id, Note(id)));
            added.Add((due, id));
        }

        var expected = added.OrderBy(a => a.Due).ThenBy(a => a.Id).Select(a => a.Id).ToArray();
        var taken = new List<long>();
        while (queue.TryTake(double.MaxValue, out var item))
            taken.Add(item.Owner);

        Assert.Equal(expected, taken);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Only_items_due_before_the_limit_are_taken_and_the_rest_wait()
    {
        var queue = new ScheduledQueue();
        queue.Add(3.0, ScheduledQueue.Op.Start, 3, Note(3));
        queue.Add(1.0, ScheduledQueue.Op.Start, 1, Note(1));
        queue.Add(2.0, ScheduledQueue.Op.End, 2, Note(2));

        Assert.False(queue.TryTake(1.0, out _)); // due at 1.0 is not before 1.0
        Assert.True(queue.TryTake(2.5, out var first));
        Assert.True(queue.TryTake(2.5, out var second));
        Assert.False(queue.TryTake(2.5, out _));

        Assert.Equal([1L, 2L], [first.Owner, second.Owner]);
        Assert.Equal(ScheduledQueue.Op.End, second.Op);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void A_full_queue_refuses_more_without_losing_what_it_holds_and_can_be_cleared()
    {
        var queue = new ScheduledQueue();
        for (int i = 0; i < ScheduledQueue.Capacity; i++)
            Assert.True(queue.Add(i, ScheduledQueue.Op.Start, i, Note(i)));

        Assert.False(queue.Add(0, ScheduledQueue.Op.Start, 999, Note(999)));
        Assert.Equal(ScheduledQueue.Capacity, queue.Count);
        Assert.True(queue.TryTake(1, out var item));
        Assert.Equal(0, item.Owner);

        queue.Clear();
        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryTake(double.MaxValue, out _));
    }

    [Fact]
    public void Adding_and_taking_in_any_mix_keeps_the_order()
    {
        var random = new Random(5);
        var queue = new ScheduledQueue();
        var waiting = new List<(double Due, long Seq, long Id)>();
        long seq = 0;
        double clock = 0;
        for (int step = 0; step < 5000; step++)
        {
            if (random.Next(3) != 0 && queue.Count < ScheduledQueue.Capacity)
            {
                double due = clock + random.Next(0, 20) / 2.0;
                queue.Add(due, ScheduledQueue.Op.Start, step, Note(step));
                waiting.Add((due, seq++, step));
            }
            else
            {
                clock += 0.5;
                while (queue.TryTake(clock, out var item))
                {
                    var next = waiting.OrderBy(w => w.Due).ThenBy(w => w.Seq).First();
                    Assert.Equal(next.Id, item.Owner);
                    waiting.Remove(next);
                }

                Assert.DoesNotContain(waiting, w => w.Due < clock);
            }
        }
    }
}
