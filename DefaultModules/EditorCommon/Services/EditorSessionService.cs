using Microsoft.EntityFrameworkCore;
using SharpClaw.Contracts.DTOs.Editor;
using SharpClaw.Contracts.Enums;
using SharpClaw.Modules.EditorCommon.Models;

namespace SharpClaw.Modules.EditorCommon.Services;

/// <summary>
/// CRUD for <see cref="EditorSessionDB"/> resources. Sessions are
/// typically auto-created when an IDE extension connects via the
/// <see cref="EditorBridgeService"/>, but can also be managed manually.
/// </summary>
public sealed class EditorSessionService(EditorCommonDbContext db)
{
    public async Task<EditorSessionResponse> CreateAsync(
        CreateEditorSessionRequest request, CancellationToken ct = default)
    {
        var session = new EditorSessionDB
        {
            Name = request.Name,
            EditorType = request.EditorType,
            EditorVersion = request.EditorVersion,
            WorkspacePath = request.WorkspacePath,
            Description = request.Description
        };

        db.EditorSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return ToResponse(session);
    }

    public async Task<IReadOnlyList<EditorSessionResponse>> ListAsync(
        CancellationToken ct = default)
    {
        var sessions = await db.EditorSessions
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
        return sessions.Select(ToResponse).ToList();
    }

    public async Task<EditorSessionResponse?> GetByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var session = await db.EditorSessions.FindAsync([id], ct);
        return session is null ? null : ToResponse(session);
    }

    public async Task<EditorSessionResponse?> UpdateAsync(
        Guid id, UpdateEditorSessionRequest request,
        CancellationToken ct = default)
    {
        var session = await db.EditorSessions.FindAsync([id], ct);
        if (session is null) return null;

        if (request.Name is not null) session.Name = request.Name;
        if (request.Description is not null) session.Description = request.Description;

        await db.SaveChangesAsync(ct);
        return ToResponse(session);
    }

    public async Task<bool> DeleteAsync(
        Guid id, CancellationToken ct = default)
    {
        var session = await db.EditorSessions.FindAsync([id], ct);
        if (session is null) return false;

        db.EditorSessions.Remove(session);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Finds an existing session by workspace path + editor type, or
    /// creates a new one.  Used by <see cref="EditorBridgeService"/>
    /// during auto-registration.
    /// </summary>
    public async Task<EditorSessionDB> GetOrCreateAsync(
        string name,
        EditorType editorType,
        string? editorVersion,
        string? workspacePath,
        CancellationToken ct = default)
    {
        // Try to find an existing session with the same workspace + editor type
        var existing = await db.EditorSessions
            .FirstOrDefaultAsync(s =>
                s.EditorType == editorType &&
                s.WorkspacePath == workspacePath, ct);

        if (existing is not null)
        {
            // Update version in case it changed
            existing.EditorVersion = editorVersion;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var session = new EditorSessionDB
        {
            Name = name,
            EditorType = editorType,
            EditorVersion = editorVersion,
            WorkspacePath = workspacePath
        };

        db.EditorSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    internal static EditorSessionResponse ToResponse(EditorSessionDB s) =>
        new(s.Id, s.Name, s.EditorType, s.EditorVersion,
            s.WorkspacePath, s.Description,
            s.ConnectionId is not null, s.CreatedAt);
}
