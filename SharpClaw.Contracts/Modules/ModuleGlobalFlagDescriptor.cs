namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Describes a global boolean flag owned by a module.
/// Registered in <c>ModuleRegistry</c> at startup.
/// Replaces the 18 hardcoded boolean properties on <c>PermissionSetDB</c>.
/// <para>
/// Modules return these from <see cref="ISharpClawModule.GetGlobalFlagDescriptors"/>
/// during registration. The host stores them in <c>ModuleRegistry</c>
/// and consumers (<c>AgentActionService</c>, <c>ChatService</c>,
/// <c>HeaderTagProcessor</c>, <c>SeedingService</c>, <c>RoleService</c>)
/// query the registry instead of hardcoded property names.
/// </para>
/// See Module-System-Design §12.4.1.
/// </summary>
/// <param name="FlagKey">
/// Canonical flag identifier (e.g. <c>"CanClickDesktop"</c>). Used as the storage
/// discriminator in <c>GlobalFlagDB.FlagKey</c> and as the
/// API property name in generic permission DTOs.
/// </param>
/// <param name="DisplayName">
/// Human-readable label for UI display (e.g. <c>"Click Desktop"</c>).
/// </param>
/// <param name="Description">
/// Human-readable tooltip/description for the UI
/// (e.g. <c>"Simulate mouse clicks on desktop displays"</c>).
/// </param>
/// <param name="DelegateMethodName">
/// The <see cref="ModuleToolPermission.DelegateTo"/> method name
/// that maps to this flag. Used by <c>AgentActionService</c>
/// for dynamic delegation lookup
/// (e.g. <c>"ClickDesktopAsync"</c> for <c>"CanClickDesktop"</c>).
/// </param>
public sealed record ModuleGlobalFlagDescriptor(
    string FlagKey,
    string DisplayName,
    string Description,
    string DelegateMethodName);
