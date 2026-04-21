using System.Text;

namespace SharpClaw.Application.Core.LocalInference;

/// <summary>
/// Declares how the model should select a tool for a tool-calling turn.
/// Mirrors OpenAI's <c>tool_choice</c> field: <c>"auto"</c>, <c>"none"</c>,
/// <c>"required"</c>, or a named function.
/// </summary>
public enum ToolChoiceMode
{
    /// <summary>Default — model chooses whether and which tool(s) to call.</summary>
    Auto = 0,
    /// <summary>Model must not call any tool this turn.</summary>
    None,
    /// <summary>Model must call at least one tool this turn.</summary>
    Required,
    /// <summary>Model must call the single named function.</summary>
    Named,
}

/// <summary>
/// Tool-selection policy attached to a completion call.
/// </summary>
public sealed record ToolChoice(
    ToolChoiceMode Mode,
    string? NamedFunction = null)
{
    public static ToolChoice Auto { get; } = new(ToolChoiceMode.Auto);
    public static ToolChoice None { get; } = new(ToolChoiceMode.None);
    public static ToolChoice Required { get; } = new(ToolChoiceMode.Required);
    public static ToolChoice ForFunction(string name) => new(ToolChoiceMode.Named, name);
}

/// <summary>
/// Builds the GBNF grammar string that constrains LLamaSharp inference
/// to the SharpClaw tool-call envelope:
/// <code>
/// { "mode": "message",     "text": "prose",  "calls": [] }
/// { "mode": "tool_calls",  "text": "",        "calls": [{ "id": "…", "name": "…", "args": {} }] }
/// </code>
/// Applied via <c>DefaultSamplingPipeline.Grammar</c> to guarantee
/// that the model always emits well-formed JSON in this shape,
/// regardless of quantization level.
/// <para>
/// Phase 2/3 — the grammar is specialised at build time according to
/// the caller's <see cref="ToolChoice"/> and <c>parallel_tool_calls</c>
/// policy. Reference implementation: llama.cpp's
/// <c>common/chat.cpp::build_grammar_from_tools</c>.
/// </para>
/// </summary>
internal static class LlamaSharpToolGrammar
{
    private static readonly string _defaultGrammar = BuildGrammar(ToolChoice.Auto, parallelCalls: true);

    /// <summary>
    /// Returns the default (auto / parallel-enabled) grammar. Cached.
    /// </summary>
    public static string Build() => _defaultGrammar;

    /// <summary>
    /// Returns the grammar for the requested tool-selection policy.
    /// <para>
    /// When <paramref name="choice"/> is <see cref="ToolChoiceMode.Named"/>
    /// the <c>name</c> terminal is pinned to a string literal so the
    /// sampler cannot produce a different function. When
    /// <paramref name="parallelCalls"/> is <see langword="false"/> the
    /// <c>calls</c> array is restricted to exactly one element.
    /// </para>
    /// </summary>
    public static string Build(ToolChoice choice, bool parallelCalls = true)
    {
        if (choice.Mode == ToolChoiceMode.Auto && parallelCalls)
            return _defaultGrammar;

        return BuildGrammar(choice, parallelCalls);
    }

    private static string BuildGrammar(ToolChoice choice, bool parallelCalls)
    {
        // Mode alternatives.
        var allowMessage   = choice.Mode is ToolChoiceMode.Auto or ToolChoiceMode.None;
        var allowToolCalls = choice.Mode is ToolChoiceMode.Auto or ToolChoiceMode.Required or ToolChoiceMode.Named;

        string modeVal = (allowMessage, allowToolCalls) switch
        {
            (true, true)   => "\"\\\"message\\\"\" | \"\\\"tool_calls\\\"\"",
            (true, false)  => "\"\\\"message\\\"\"",
            (false, true)  => "\"\\\"tool_calls\\\"\"",
            _              => throw new InvalidOperationException("ToolChoice produced an unreachable grammar."),
        };

        // Calls array shape.
        // - Auto: empty or one-or-more calls.
        // - None: must be empty (message mode).
        // - Required / Named / !parallelCalls: at least one, optionally one-only.
        var singleCallOnly = !parallelCalls || choice.Mode == ToolChoiceMode.Named;
        string callsArr = choice.Mode switch
        {
            ToolChoiceMode.None =>
                "\"[\" ws \"]\"",
            ToolChoiceMode.Auto when parallelCalls =>
                "\"[\" ws \"]\"\n            | \"[\" ws call-obj ( ws \",\" ws call-obj )* ws \"]\"",
            ToolChoiceMode.Auto =>
                "\"[\" ws \"]\"\n            | \"[\" ws call-obj ws \"]\"",
            _ when singleCallOnly =>
                "\"[\" ws call-obj ws \"]\"",
            _ =>
                "\"[\" ws call-obj ( ws \",\" ws call-obj )* ws \"]\"",
        };

        // Name terminal — literal when a function is pinned.
        string nameTerminal = choice.Mode == ToolChoiceMode.Named
            ? $"\"{EscapeForGbnfStringLiteral(choice.NamedFunction!)}\"".Replace("\"", "\\\"", StringComparison.Ordinal)
            : "string";

        // Manual escape: we want the pinned name to appear in-grammar as
        // the GBNF sequence  "\""  <name>  "\""  so the sampler emits
        // a JSON-quoted string with exactly that content.
        if (choice.Mode == ToolChoiceMode.Named)
        {
            nameTerminal = $"\"\\\"{EscapeForJsonInsideGbnf(choice.NamedFunction!)}\\\"\"";
        }

        var sb = new StringBuilder();
        sb.Append("root   ::= \"{\" ws \"\\\"mode\\\"\" ws \":\" ws mode-val \",\" ws\n");
        sb.Append("               \"\\\"text\\\"\" ws \":\" ws string     \",\" ws\n");
        sb.Append("               \"\\\"calls\\\"\" ws \":\" ws calls-arr\n");
        sb.Append("           \"}\"\n\n");
        sb.Append($"mode-val ::= {modeVal}\n\n");
        sb.Append($"calls-arr ::= {callsArr}\n\n");
        sb.Append("call-obj  ::= \"{\" ws \"\\\"id\\\"\"   ws \":\" ws string \",\" ws\n");
        sb.Append($"                   \"\\\"name\\\"\"  ws \":\" ws {nameTerminal} \",\" ws\n");
        sb.Append("                   \"\\\"args\\\"\"  ws \":\" ws obj\n");
        sb.Append("               \"}\"\n\n");
        sb.Append("# ── JSON primitives ──────────────────────────────────────────\n\n");
        sb.Append("obj    ::= \"{\" ws \"}\"\n");
        sb.Append("         | \"{\" ws kv-pair ( ws \",\" ws kv-pair )* ws \"}\"\n\n");
        sb.Append("kv-pair ::= string ws \":\" ws value\n\n");
        sb.Append("value  ::= string | number | obj | arr | \"true\" | \"false\" | \"null\"\n\n");
        sb.Append("arr    ::= \"[\" ws \"]\"\n");
        sb.Append("         | \"[\" ws value ( ws \",\" ws value )* ws \"]\"\n\n");
        sb.Append("string ::= \"\\\"\" char* \"\\\"\"\n\n");
        sb.Append("char   ::= [^\"\\\\]\n");
        sb.Append("         | \"\\\\\" ( \"\\\"\" | \"\\\\\" | \"/\" | \"b\" | \"f\" | \"n\" | \"r\" | \"t\" )\n");
        sb.Append("         | \"\\\\\" \"u\" [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F]\n\n");
        sb.Append("number ::= \"-\"? ( \"0\" | [1-9] [0-9]* ) ( \".\" [0-9]+ )? ( [eE] [+-]? [0-9]+ )?\n\n");
        sb.Append("ws     ::= [ \\t\\n\\r]*\n");
        return sb.ToString();
    }

    /// <summary>
    /// Escapes a function name so it can be embedded inside a GBNF
    /// literal that itself represents a JSON-quoted string. Allows
    /// ASCII letters/digits/underscore/hyphen/dot; rejects anything
    /// else because OpenAI tool names already constrain to that set.
    /// </summary>
    private static string EscapeForJsonInsideGbnf(string name)
    {
        foreach (var ch in name)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.'))
                throw new ArgumentException(
                    $"Invalid character '{ch}' in function name '{name}' — tool-choice Named requires a plain [A-Za-z0-9_.-] identifier.",
                    nameof(name));
        }
        return name;
    }

    private static string EscapeForGbnfStringLiteral(string s) => s;
}

