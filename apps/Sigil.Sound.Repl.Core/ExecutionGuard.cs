namespace Sigil.Sound.Repl;

/// <summary>
/// Thrown when a snippet exceeds loop step or recursion depth limits,
/// preventing infinite execution freezes in the single-threaded REPL environment.
/// </summary>
public sealed class ExecutionLimitException(string message) : Exception(message);

/// <summary>
/// Thread-local execution guards injected into user snippets at compile time.
/// Prevents denial-of-service, runaway loops, and stack overflows.
/// </summary>
public static class ExecutionGuard
{
    /// <summary>Loop iterations allowed for one run of a snippet, such as building its sound.</summary>
    public const int MaxLoopSteps = 200_000;

    /// <summary>
    /// Extra loop iterations allowed for every frame the host is about to render. Per-sample code with an inner loop
    /// (an additive voice with 128 partials takes 128 steps per frame) needs a budget that grows with the block,
    /// while a loop that never ends still stops within a few million steps.
    /// </summary>
    public const int MaxStepsPerFrame = 1024;

    /// <summary>Maximum call stack depth permitted for user functions.</summary>
    public const int MaxRecursionDepth = 256;

    [ThreadStatic]
    private static int _stepCount;

    [ThreadStatic]
    private static int _callDepth;

    // Zero until the first Reset on a thread, which means "use MaxLoopSteps".
    [ThreadStatic]
    private static int _stepBudget;

    /// <summary>
    /// Resets the loop step and call depth counters before running a snippet or rendering audio.
    /// Pass the number of frames about to be rendered so the step budget covers per-sample loops; the counters live
    /// on the calling thread, so a host that renders several snippets on one thread resets before each of them.
    /// </summary>
    /// <param name="frames">Frames the next call will render; zero for running a snippet to build its sound.</param>
    public static void Reset(int frames = 0)
    {
        _stepCount = 0;
        _callDepth = 0;
        _stepBudget = (int)Math.Min(int.MaxValue, MaxLoopSteps + (long)Math.Max(0, frames) * MaxStepsPerFrame);
    }

    /// <summary>
    /// Injected at the head of every loop (while, for, foreach, do-while).
    /// Throws <see cref="ExecutionLimitException"/> if the loop step budget is exceeded.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static void CountStep()
    {
        int budget = _stepBudget;
        if (budget == 0)
            budget = MaxLoopSteps;

        if (++_stepCount > budget)
        {
            _stepCount = 0;
            throw new ExecutionLimitException(
                $"Loop execution step limit exceeded (maximum {budget:N0} steps). Snippet execution was stopped.");
        }
    }

    /// <summary>
    /// Injected at the entrance of user-defined local functions, methods and lambdas.
    /// Throws <see cref="ExecutionLimitException"/> if the recursion depth limit is exceeded.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static void EnterCall()
    {
        if (++_callDepth > MaxRecursionDepth)
        {
            _callDepth = 0;
            throw new ExecutionLimitException(
                $"Recursion depth limit exceeded (maximum {MaxRecursionDepth} nested calls). Snippet execution was stopped.");
        }
    }

    /// <summary>
    /// Injected into the finally block of user-defined functions to decrement the call depth.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static void ExitCall()
    {
        if (_callDepth > 0)
            _callDepth--;
    }
}

