namespace SharpClaw.Contracts.DTOs.Threads;

/// <summary>
/// Request body for <c>POST /channels/{channelId}/threads</c>.
/// <para>
/// The thread label field is named <b><c>Name</c></b>, not <c>Title</c>. ASP.NET
/// Core's default JSON binding silently drops unrecognised fields, so sending
/// <c>{"Title":"Foo"}</c> is not a 400 — the <c>Name</c> is left null and the
/// service falls back to an auto-generated <c>"Thread yyyy-MM-dd HH:mm"</c>
/// label. See bug #3 in docs/internal/local-inference-pipeline-debug-report.md.
/// </para>
/// </summary>
/// <param name="Name">Human-readable thread label. Null/omitted → auto-generated.</param>
/// <param name="MaxMessages">Optional per-thread message limit. Oldest messages trimmed first. <c>null</c> = inherit system default, <c>0</c> = reset to default.</param>
/// <param name="MaxCharacters">Optional per-thread character limit across all messages. Same <c>null</c>/<c>0</c> semantics as <paramref name="MaxMessages"/>.</param>
/// <param name="CustomId">Optional caller-supplied stable identifier (for external correlation; not indexed).</param>
public sealed record CreateThreadRequest(
    string? Name = null,
    int? MaxMessages = null,
    int? MaxCharacters = null,
    string? CustomId = null);

/// <summary>
/// Request body for <c>PUT /channels/{channelId}/threads/{threadId}</c>.
/// <para>
/// All fields are optional. Omitted fields are left unchanged; explicitly
/// sending <c>null</c> clears the field. Same <c>Name</c>-vs-<c>Title</c>
/// caveat as <see cref="CreateThreadRequest"/>.
/// </para>
/// </summary>
public sealed record UpdateThreadRequest(
    string? Name = null,
    int? MaxMessages = null,
    int? MaxCharacters = null,
    string? CustomId = null);

public sealed record ThreadResponse(
    Guid Id,
    string Name,
    Guid ChannelId,
    int? MaxMessages,
    int? MaxCharacters,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CustomId = null);
