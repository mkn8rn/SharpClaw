using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using SharpClaw.Gateway.Configuration;
using SharpClaw.Gateway.Modules;
using SharpClaw.Gateway.Security;

namespace SharpClaw.Tests.Gateway;

/// <summary>
/// Future-proofing guardrails for the gateway endpoint kill-switch surface.
/// These tests use reflection over <see cref="GatewayEndpointOptions"/> and
/// the gateway MVC controller set, so adding a new toggle property or a new
/// controller automatically participates without a manual test edit. They
/// exist to catch the silent-bypass trap called out in
/// <c>docs/internal/gateway-drift-and-publish-plan-v2.md §4.3</c>: a new
/// toggle that is not mapped in <see cref="GatewayEndpointOptions.IsEndpointEnabled"/>
/// or a new controller whose route prefix is not mapped in
/// <c>EndpointGateMiddleware.ResolveGroup</c> would otherwise fall through
/// the gate ungated.
/// </summary>
[TestFixture]
public sealed class GatewayEndpointToggleTests
{
    /// <summary>
    /// Properties on <see cref="GatewayEndpointOptions"/> that are not
    /// per-group toggles. The master switch is excluded from this fixture
    /// because it is exercised separately.
    /// </summary>
    private static readonly HashSet<string> NonGroupProperties = new(StringComparer.Ordinal)
    {
        nameof(GatewayEndpointOptions.Enabled),
    };

    /// <summary>
    /// Gateway controllers whose route prefix is intentionally not mapped
    /// to a <see cref="GatewayEndpointOptions"/> group. These controllers
    /// are gateway-internal metadata surfaces and are exempt from the
    /// kill-switch by design.
    /// </summary>
    private static readonly HashSet<string> UngatedControllers = new(StringComparer.Ordinal)
    {
        "GatewayController",
    };

    private static IEnumerable<PropertyInfo> ToggleProperties()
        => typeof(GatewayEndpointOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool)
                        && p.CanWrite
                        && !NonGroupProperties.Contains(p.Name));

    private static IEnumerable<TestCaseData> ToggleCases()
        => ToggleProperties().Select(p => new TestCaseData(p).SetName(
            $"Toggle_WhenFalse_DisablesEndpoint({p.Name})"));

    [TestCaseSource(nameof(ToggleCases))]
    public void Toggle_WhenFalse_DisablesEndpoint(PropertyInfo toggle)
    {
        var opts = new GatewayEndpointOptions { Enabled = true };

        // Set every toggle to true first so the only "false" knob is the
        // one under test. If IsEndpointEnabled forgets to map this toggle
        // it falls through to the `_ => true` branch and returns true,
        // which fails the assertion.
        foreach (var p in ToggleProperties())
            p.SetValue(opts, true);
        toggle.SetValue(opts, false);

        opts.IsEndpointEnabled(toggle.Name).Should().BeFalse(
            $"GatewayEndpointOptions.{toggle.Name} must have a matching case " +
            $"in IsEndpointEnabled. Without one, flipping the toggle off does " +
            $"nothing and the kill-switch silently leaks the endpoint.");
    }

    [TestCaseSource(nameof(ToggleCases))]
    public void Toggle_WhenTrue_EnablesEndpoint_WithMasterOn(PropertyInfo toggle)
    {
        var opts = new GatewayEndpointOptions { Enabled = true };

        // Mirror image: every toggle false except the one under test.
        // Confirms IsEndpointEnabled actually reads the property rather
        // than returning a constant.
        foreach (var p in ToggleProperties())
            p.SetValue(opts, false);
        toggle.SetValue(opts, true);

        opts.IsEndpointEnabled(toggle.Name).Should().BeTrue(
            $"GatewayEndpointOptions.{toggle.Name} must read its own backing " +
            $"property in IsEndpointEnabled.");
    }

    [TestCaseSource(nameof(ToggleCases))]
    public void Toggle_MasterOff_OverridesEveryGroup(PropertyInfo toggle)
    {
        var opts = new GatewayEndpointOptions { Enabled = false };
        foreach (var p in ToggleProperties())
            p.SetValue(opts, true);

        opts.IsEndpointEnabled(toggle.Name).Should().BeFalse(
            "the master kill-switch must dominate every group toggle.");
    }

    [Test]
    public void IsEndpointEnabled_UnknownGroup_ReturnsTrue_ButOnlyWithMasterOn()
    {
        // Documents (and pins) the current `_ => true` fallback. If this
        // behaviour is ever tightened to default-deny, update this test
        // and tier-zero hygiene in the plan together.
        var opts = new GatewayEndpointOptions { Enabled = true };
        opts.IsEndpointEnabled("ThisGroupDoesNotExist").Should().BeTrue();

        opts.Enabled = false;
        opts.IsEndpointEnabled("ThisGroupDoesNotExist").Should().BeFalse();
    }

    /// <summary>
    /// Walks every gateway MVC controller, builds a representative request
    /// path from its <see cref="RouteAttribute"/> + an HTTP method
    /// attribute template, and pushes that path through
    /// <see cref="EndpointGateMiddleware"/> with the master switch on and
    /// every group toggle off. A path the gate fails to recognize falls
    /// through to <c>next(context)</c> (200), which fails this assertion;
    /// a recognized-but-disabled path responds with 503. This catches a
    /// new controller landing without a matching <c>ResolveGroup</c>
    /// prefix mapping.
    /// </summary>
    [TestCaseSource(nameof(GatewayControllerRouteCases))]
    public async Task EveryGatewayController_RoutePrefix_IsGated(string controllerName, string samplePath)
    {
        UngatedControllers.Should().NotContain(controllerName,
            "[sanity] ungated controllers should not be supplied to this test.");

        var optsValue = new GatewayEndpointOptions { Enabled = true };
        // Every group toggle defaults to false, which is exactly what we want.
        var optionsMonitor = new StaticOptionsMonitor<GatewayEndpointOptions>(optsValue);
        var moduleOptions = new StaticOptionsMonitor<GatewayModuleOptions>(new GatewayModuleOptions());
        var catalog = new GatewayEndpointGroupCatalog(moduleOptions);

        var nextCalled = false;
        var gate = new EndpointGateMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            optionsMonitor,
            catalog,
            NullLogger<EndpointGateMiddleware>.Instance);

        var ctx = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() },
        };
        ctx.Request.Path = samplePath;
        ctx.Request.Method = "GET";

        await gate.InvokeAsync(ctx);

        nextCalled.Should().BeFalse(
            $"{controllerName}'s route prefix '{samplePath}' was not recognized " +
            $"by EndpointGateMiddleware.ResolveGroup. Add a matching prefix " +
            $"check there so the kill-switch can gate this controller.");
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable,
            $"a recognized but disabled controller must respond 503, not " +
            $"{ctx.Response.StatusCode}.");
    }

    public static IEnumerable<TestCaseData> GatewayControllerRouteCases()
    {
        var asm = typeof(SharpClaw.Gateway.Controllers.AuthController).Assembly;
        foreach (var type in asm.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract) continue;
            if (UngatedControllers.Contains(type.Name)) continue;

            var routeAttr = type.GetCustomAttribute<RouteAttribute>();
            if (routeAttr is null) continue;

            var controllerName = type.Name.EndsWith("Controller", StringComparison.Ordinal)
                ? type.Name[..^"Controller".Length]
                : type.Name;
            var classRoute = (routeAttr.Template ?? string.Empty).Replace(
                "[controller]", controllerName, StringComparison.OrdinalIgnoreCase);

            // Pick the first http-method template so we exercise a real
            // sub-route (e.g. /api/auth/login) rather than just the class
            // prefix; some prefixes only resolve once a sub-path is present.
            var methodTemplate = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
                .Select(a => a.Template ?? string.Empty)
                .FirstOrDefault(t => t.Length > 0) ?? string.Empty;

            var combined = CombinePath(classRoute, methodTemplate);
            // Replace any route parameters with stable placeholders so the
            // gate sees a literal-looking path.
            var path = ReplaceRouteParameters(combined);
            if (!path.StartsWith('/')) path = "/" + path;

            yield return new TestCaseData(type.Name, path)
                .SetName($"EveryGatewayController_RoutePrefix_IsGated({type.Name})");
        }
    }

    private static string CombinePath(string a, string b)
    {
        var left = (a ?? string.Empty).TrimEnd('/');
        var right = (b ?? string.Empty).TrimStart('/');
        if (right.Length == 0) return left.Length == 0 ? "/" : left;
        if (left.Length == 0) return "/" + right;
        return $"{left}/{right}";
    }

    private static string ReplaceRouteParameters(string template)
    {
        var sb = new System.Text.StringBuilder(template.Length);
        var depth = 0;
        var hadParam = false;
        foreach (var ch in template)
        {
            if (ch == '{') { depth++; hadParam = true; continue; }
            if (ch == '}') { if (depth > 0) depth--; if (depth == 0 && hadParam) { sb.Append("00000000-0000-0000-0000-000000000000"); hadParam = false; } continue; }
            if (depth == 0) sb.Append(ch);
        }
        return sb.ToString();
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
