namespace Sigil.Sound.Tests;

/// <summary>
/// A test that only makes sense when a developer asks for it: it reports as skipped, with the variable to set, unless the
/// environment variable is set to something other than empty or "0". A test that silently returns would pass and look like a check.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EnvironmentFactAttribute : FactAttribute
{
    public EnvironmentFactAttribute(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(value) || value == "0")
            Skip = $"Manual run: set {variable} to run this.";
    }
}
