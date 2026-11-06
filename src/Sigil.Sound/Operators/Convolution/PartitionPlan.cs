namespace Sigil.Sound.Operators;

/// <summary>
/// Splits an impulse response of N taps into a zero-latency non-uniform partition schedule:
/// a direct-form FIR head followed by FFT-partitioned stages with growing partition sizes.
/// Pure function of N, so it can be unit-tested without audio. <see cref="ConvolutionKernel"/> and
/// <see cref="ConvolutionEngine"/> build their buffers from the stages.
/// </summary>
internal sealed class PartitionPlan
{
    /// <summary>One FFT-partitioned stage covering taps [Start, Start + Length).</summary>
    /// <param name="Start">First tap offset O. Always a multiple of the partition size.</param>
    /// <param name="Length">Number of real taps covered (the last partition may be partial).</param>
    /// <param name="PartitionSize">Partition size B. The FFT size is 2B (overlap-save).</param>
    /// <param name="PartitionCount">Number of partitions M = ceil(Length / B).</param>
    /// <param name="Sliced">True when O >= 2B, which leaves a whole block of slack so the
    /// partition multiply-accumulate loop can be spread over the block instead of landing at one boundary.</param>
    public readonly record struct Stage(int Start, int Length, int PartitionSize, int PartitionCount, bool Sliced)
    {
        public int FftSize => PartitionSize * 2;
        public int End => Start + Length;
    }

    /// <summary>Taps handled by the time-domain head FIR when the IR is long enough.</summary>
    public const int HeadLength = 128;

    /// <summary>Default (stage start, partition size) table, tuned by benchmark in the Audio Workbench.</summary>
    private static readonly (int Start, int Partition)[] DefaultTable =
    {
        (128, 128),
        (1024, 512),
        (4096, 2048),
        (16384, 8192),
    };

    public int TotalTaps { get; }
    public int HeadTaps { get; }
    public IReadOnlyList<Stage> Stages { get; }

    private PartitionPlan(int totalTaps, int headTaps, Stage[] stages)
    {
        TotalTaps = totalTaps;
        HeadTaps = headTaps;
        Stages = stages;
    }

    /// <summary>Total number of IR partitions across all stages.</summary>
    public int TotalPartitions
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < Stages.Count; i++) sum += Stages[i].PartitionCount;
            return sum;
        }
    }

    public static PartitionPlan Create(int totalTaps) => Create(totalTaps, DefaultTable);

    /// <summary>
    /// Builds a plan from a custom table, for tests and benchmarks that compare stage ratios.
    /// Every entry must satisfy Start == B or Start == 2B so a stage needs at most one
    /// job in flight.
    /// </summary>
    internal static PartitionPlan Create(int totalTaps, (int Start, int Partition)[] table)
    {
        if (totalTaps < 1) throw new ArgumentOutOfRangeException(nameof(totalTaps));

        int head = Math.Min(totalTaps, HeadLength);
        var stages = new List<Stage>(table.Length);
        for (int i = 0; i < table.Length; i++)
        {
            int start = table[i].Start;
            int b = table[i].Partition;
            if (start != b && start != 2 * b)
                throw new ArgumentException($"Stage {i}: start must equal B or 2B (start={start}, B={b}).");
            if (start >= totalTaps) break;

            int end = i + 1 < table.Length ? Math.Min(totalTaps, table[i + 1].Start) : totalTaps;
            int length = end - start;
            int count = (length + b - 1) / b;

            // A last stage that would need only one mostly-empty big partition costs more (a large FFT pair
            // per block) than letting the previous stage's smaller partitions run on a little further.
            if (end == totalTaps && stages.Count > 0 && count == 1 && length <= b / 2)
            {
                var prev = stages[^1];
                int mergedLength = end - prev.Start;
                stages[^1] = prev with
                {
                    Length = mergedLength,
                    PartitionCount = (mergedLength + prev.PartitionSize - 1) / prev.PartitionSize,
                };
                break;
            }

            stages.Add(new Stage(start, length, b, count, start >= 2 * b));
        }

        return new PartitionPlan(totalTaps, head, stages.ToArray());
    }
}
