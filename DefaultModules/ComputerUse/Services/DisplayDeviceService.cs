using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using SharpClaw.Application.Infrastructure.Models.Resources;
using SharpClaw.Contracts.DTOs.DisplayDevices;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Modules.ComputerUse.Services;

/// <summary>
/// Manages display device CRUD and auto-detection. Screen capture
/// execution is handled by the Computer Use module via the
/// job/permission system.
/// </summary>
public sealed class DisplayDeviceService(SharpClawDbContext db)
{
    // ═══════════════════════════════════════════════════════════════
    // CRUD
    // ═══════════════════════════════════════════════════════════════

    public async Task<DisplayDeviceResponse> CreateAsync(
        CreateDisplayDeviceRequest request, CancellationToken ct = default)
    {
        var device = new DisplayDeviceDB
        {
            Name = request.Name,
            DeviceIdentifier = request.DeviceIdentifier,
            DisplayIndex = request.DisplayIndex,
            Description = request.Description
        };

        db.DisplayDevices.Add(device);
        await db.SaveChangesAsync(ct);
        return ToResponse(device);
    }

    public async Task<IReadOnlyList<DisplayDeviceResponse>> ListAsync(
        CancellationToken ct = default)
    {
        var devices = await db.DisplayDevices
            .OrderBy(d => d.DisplayIndex)
            .ToListAsync(ct);
        return devices.Select(ToResponse).ToList();
    }

    public async Task<DisplayDeviceResponse?> GetByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var device = await db.DisplayDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        return device is not null ? ToResponse(device) : null;
    }

    public async Task<DisplayDeviceResponse?> UpdateAsync(
        Guid id, UpdateDisplayDeviceRequest request, CancellationToken ct = default)
    {
        var device = await db.DisplayDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return null;

        if (request.Name is not null) device.Name = request.Name;
        if (request.DeviceIdentifier is not null) device.DeviceIdentifier = request.DeviceIdentifier;
        if (request.DisplayIndex.HasValue) device.DisplayIndex = request.DisplayIndex.Value;
        if (request.Description is not null) device.Description = request.Description;

        await db.SaveChangesAsync(ct);
        return ToResponse(device);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var device = await db.DisplayDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return false;

        db.DisplayDevices.Remove(device);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    // Sync — discover system monitors and import new ones
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Discovers all display outputs on the current system and imports
    /// any that are not already in the database.  Duplicates (matched
    /// by <see cref="DisplayDeviceDB.DeviceIdentifier"/>) are skipped.
    /// <para>
    /// On Windows, uses <c>EnumDisplayDevices</c> via P/Invoke.
    /// On other platforms, falls back to a single "Primary" display.
    /// </para>
    /// </summary>
    public async Task<DisplayDeviceSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var systemDisplays = EnumerateSystemDisplays();

        var existingIds = await db.DisplayDevices
            .Where(d => d.DeviceIdentifier != null)
            .Select(d => d.DeviceIdentifier!)
            .ToListAsync(ct);

        var existingSet = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);

        var imported = new List<string>();
        var skipped = new List<string>();

        foreach (var (deviceId, name, index) in systemDisplays)
        {
            if (existingSet.Contains(deviceId))
            {
                skipped.Add(name);
                continue;
            }

            db.DisplayDevices.Add(new DisplayDeviceDB
            {
                Name = name,
                DeviceIdentifier = deviceId,
                DisplayIndex = index,
                Description = "Synced from system display devices"
            });
            imported.Add(name);
        }

        if (imported.Count > 0)
            await db.SaveChangesAsync(ct);

        return new DisplayDeviceSyncResult(
            imported.Count, skipped.Count, imported, skipped);
    }

    // ═══════════════════════════════════════════════════════════════
    // System enumeration
    // ═══════════════════════════════════════════════════════════════

    private static List<(string DeviceId, string Name, int Index)> EnumerateSystemDisplays()
    {
        if (OperatingSystem.IsWindows())
            return EnumerateWindowsDisplays();

        // Fallback for non-Windows: single primary display.
        return [("display-0", "Primary Display", 0)];
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<(string DeviceId, string Name, int Index)> EnumerateWindowsDisplays()
    {
        var results = new List<(string, string, int)>();
        var device = new DISPLAY_DEVICE();
        device.cb = Marshal.SizeOf(device);

        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            // Only active (attached) monitors.
            if ((device.StateFlags & 0x00000001) == 0) // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
            {
                device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                continue;
            }

            var deviceName = device.DeviceName.TrimEnd('\0');
            var deviceString = device.DeviceString.TrimEnd('\0');
            var friendlyName = !string.IsNullOrWhiteSpace(deviceString)
                ? $"{deviceString} ({deviceName})"
                : deviceName;

            results.Add((deviceName, friendlyName, (int)i));
            device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        }

        if (results.Count == 0)
            results.Add((@"\\.\DISPLAY1", "Primary Display", 0));

        return results;
    }

    // ═══════════════════════════════════════════════════════════════
    // Win32 P/Invoke
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern bool EnumDisplayDevices(
        string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    // ═══════════════════════════════════════════════════════════════

    private static DisplayDeviceResponse ToResponse(DisplayDeviceDB d) =>
        new(d.Id, d.Name, d.DeviceIdentifier, d.DisplayIndex, d.Description, d.SkillId, d.CreatedAt);
}
