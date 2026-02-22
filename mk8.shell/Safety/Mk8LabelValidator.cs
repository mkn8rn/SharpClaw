using Mk8.Shell.Engine;

namespace Mk8.Shell.Safety;

/// <summary>
/// Validates step labels and <c>onFailure</c> jump targets at compile time.
/// <para>
/// <b>Rules:</b>
/// <list type="bullet">
///   <item>Labels must be unique within a script.</item>
///   <item>Jump targets must reference existing labels.</item>
///   <item>Only forward jumps allowed — no backward jumps (prevents loops).</item>
///   <item>The jump graph must be a DAG — no cycles.</item>
///   <item>Labels are metadata — not executable. No new attack surface.</item>
/// </list>
/// </para>
/// </summary>
public static class Mk8LabelValidator
{
    /// <summary>
    /// Validates all labels and jump targets in a flat operation list.
    /// Returns a label-to-index mapping for the executor.
    /// </summary>
    /// <param name="operations">
    /// The expanded (flat) operation list — after ForEach/If/Batch expansion.
    /// </param>
    /// <returns>
    /// A dictionary mapping label names to their 0-based step index.
    /// Empty if no labels are defined.
    /// </returns>
    public static Dictionary<string, int> Validate(
        IReadOnlyList<Mk8ShellOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var labelIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Pass 1: collect all labels and check uniqueness.
        for (var i = 0; i < operations.Count; i++)
        {
            var label = operations[i].Label;
            if (string.IsNullOrWhiteSpace(label))
                continue;

            ValidateLabelName(label);

            if (!labelIndex.TryAdd(label, i))
                throw new Mk8CompileException(operations[i].Verb,
                    $"Duplicate label '{label}' at step {i}. " +
                    $"Label was already defined at step {labelIndex[label]}.");
        }

        // Pass 2: validate all jump targets.
        for (var i = 0; i < operations.Count; i++)
        {
            var onFailure = operations[i].OnFailure;
            if (string.IsNullOrWhiteSpace(onFailure))
                continue;

            var target = ParseJumpTarget(onFailure, operations[i].Verb);

            if (!labelIndex.TryGetValue(target, out var targetIndex))
                throw new Mk8CompileException(operations[i].Verb,
                    $"Jump target '{target}' at step {i} does not match " +
                    "any label in the script.");

            // Only forward jumps allowed — prevents loops.
            if (targetIndex <= i)
                throw new Mk8CompileException(operations[i].Verb,
                    $"Backward jump from step {i} to label '{target}' " +
                    $"at step {targetIndex} is not allowed. " +
                    "Only forward jumps are permitted.");
        }

        return labelIndex;
    }

    /// <summary>
    /// Parses a jump target from the <c>onFailure</c> value.
    /// Expected format: <c>"goto:labelName"</c>.
    /// </summary>
    private static string ParseJumpTarget(string onFailure, Mk8ShellVerb verb)
    {
        const string prefix = "goto:";

        if (!onFailure.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new Mk8CompileException(verb,
                $"Invalid onFailure value '{onFailure}'. " +
                $"Expected format: 'goto:<label>'.");

        var target = onFailure[prefix.Length..];

        if (string.IsNullOrWhiteSpace(target))
            throw new Mk8CompileException(verb,
                "Empty jump target in onFailure.");

        return target;
    }

    /// <summary>
    /// Validates a label name contains only safe characters.
    /// Labels are compile-time metadata — alphanumeric, hyphens,
    /// underscores only.
    /// </summary>
    private static void ValidateLabelName(string label)
    {
        if (label.Length > 64)
            throw new Mk8CompileException(Mk8ShellVerb.FileRead,
                $"Label '{label}' exceeds maximum length of 64 characters.");

        foreach (var c in label)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                throw new Mk8CompileException(Mk8ShellVerb.FileRead,
                    $"Label '{label}' contains invalid character '{c}'. " +
                    "Labels may only contain letters, digits, hyphens, and underscores.");
        }
    }
}
