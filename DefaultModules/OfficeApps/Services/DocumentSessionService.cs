using Microsoft.EntityFrameworkCore;
using SharpClaw.Modules.OfficeApps.Dtos;
using SharpClaw.Modules.OfficeApps.Enums;
using SharpClaw.Modules.OfficeApps.Models;
using SharpClaw.Utils.Security;

namespace SharpClaw.Modules.OfficeApps.Services;

/// <summary>
/// CRUD for <see cref="DocumentSessionDB"/> resources.
/// Document sessions are registered file paths that agents can
/// operate on using spreadsheet and document tools.
/// </summary>
public sealed class DocumentSessionService(OfficeAppsDbContext db)
{
    public async Task<DocumentSessionResponse> CreateAsync(
        CreateDocumentSessionRequest request, CancellationToken ct = default)
    {
        var filePath = PathGuard.EnsureAbsolutePath(request.FilePath, nameof(request.FilePath));
        var docType = DetectDocumentType(filePath);

        var session = new DocumentSessionDB
        {
            Name = request.Name ?? Path.GetFileName(filePath),
            FilePath = filePath,
            DocumentType = docType,
            Description = request.Description,
        };

        db.DocumentSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return ToResponse(session);
    }

    public async Task<IReadOnlyList<DocumentSessionResponse>> ListAsync(
        CancellationToken ct = default)
    {
        var sessions = await db.DocumentSessions
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
        return sessions.Select(ToResponse).ToList();
    }

    public async Task<DocumentSessionResponse?> GetByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var session = await db.DocumentSessions.FindAsync([id], ct);
        return session is null ? null : ToResponse(session);
    }

    public async Task<DocumentSessionResponse?> UpdateAsync(
        Guid id, UpdateDocumentSessionRequest request,
        CancellationToken ct = default)
    {
        var session = await db.DocumentSessions.FindAsync([id], ct);
        if (session is null) return null;

        if (request.Name is not null) session.Name = request.Name;
        if (request.Description is not null) session.Description = request.Description;

        await db.SaveChangesAsync(ct);
        return ToResponse(session);
    }

    public async Task<bool> DeleteAsync(
        Guid id, CancellationToken ct = default)
    {
        var session = await db.DocumentSessions.FindAsync([id], ct);
        if (session is null) return false;

        db.DocumentSessions.Remove(session);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Detects the <see cref="DocumentType"/> from a file extension.
    /// </summary>
    public static DocumentType DetectDocumentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".xlsx" or ".xlsm" => DocumentType.Spreadsheet,
            ".csv" => DocumentType.Csv,
            ".docx" => DocumentType.Document,
            ".pptx" => DocumentType.Presentation,
            _ => throw new ArgumentException($"Unsupported document extension: '{ext}'"),
        };
    }

    private static DocumentSessionResponse ToResponse(DocumentSessionDB s) => new(
        s.Id, s.Name, s.FilePath, s.DocumentType,
        s.Description, s.CreatedAt, s.UpdatedAt);
}
