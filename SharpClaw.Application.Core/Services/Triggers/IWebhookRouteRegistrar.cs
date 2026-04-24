namespace SharpClaw.Application.Core.Services.Triggers;

/// <summary>
/// Abstracts the minimal-API route registration surface so that
/// <see cref="WebhookTriggerSource"/> can be tested without a real
/// <c>WebApplication</c>.
/// </summary>
public interface IWebhookRouteRegistrar
{
    /// <summary>
    /// Register a POST route at the given path if it has not already been
    /// registered during this application lifetime.
    /// </summary>
    /// <param name="routePath">Absolute path, e.g. <c>/webhooks/tasks/my-hook</c>.</param>
    void EnsureRegistered(string routePath);
}
