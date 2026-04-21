using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SharpClaw.Application.API.Routing;

public static class EndpointMapper
{
    /// <summary>
    /// Scans all static handler classes decorated with <see cref="RouteGroupAttribute"/>
    /// in the calling assembly and registers their methods as minimal API endpoints.
    /// <para>
    /// Each handler class is processed in isolation: failures in one class (e.g. a
    /// method whose signature can't be bound) are logged and skipped so the rest of
    /// the API remains operational. Within a handler class, each endpoint method is
    /// likewise wrapped so that a single broken route does not take down the sibling
    /// routes in the same group.
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapHandlers(this IEndpointRouteBuilder routes, Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();

        var logger = routes.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(EndpointMapper).FullName!);

        Type[] handlerClasses;
        try
        {
            handlerClasses = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: true, IsSealed: true } // static classes
                            && t.GetCustomAttribute<RouteGroupAttribute>() is not null)
                .ToArray();
        }
        catch (ReflectionTypeLoadException ex)
        {
            logger.LogError(ex, "Failed to enumerate handler classes in assembly {Assembly}. " +
                "Partial types will be used.", assembly.FullName);
            handlerClasses = [.. ex.Types.Where(t => t is not null
                && t.IsClass && t.IsAbstract && t.IsSealed
                && t.GetCustomAttribute<RouteGroupAttribute>() is not null)!];
        }

        var totalMapped = 0;
        var totalFailed = 0;

        foreach (var handlerClass in handlerClasses)
        {
            var groupAttr = handlerClass.GetCustomAttribute<RouteGroupAttribute>()!;

            RouteGroupBuilder group;
            try
            {
                group = routes.MapGroup(groupAttr.Prefix);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create route group '{Prefix}' for {Handler}. " +
                    "All endpoints in this group will be skipped.",
                    groupAttr.Prefix, handlerClass.FullName);
                totalFailed++;
                continue;
            }

            var methods = handlerClass.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<MapMethodAttribute>() is not null);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<MapMethodAttribute>()!;
                try
                {
                    var handler = method.CreateDelegate(CreateDelegateType(method), null);

                    _ = attr.HttpMethod switch
                    {
                        "GET" => group.MapGet(attr.Pattern, handler),
                        "POST" => group.MapPost(attr.Pattern, handler),
                        "PUT" => group.MapPut(attr.Pattern, handler),
                        "DELETE" => group.MapDelete(attr.Pattern, handler),
                        _ => throw new NotSupportedException($"HTTP method '{attr.HttpMethod}' is not supported.")
                    };

                    totalMapped++;
                }
                catch (Exception ex)
                {
                    totalFailed++;
                    logger.LogError(ex,
                        "Failed to map endpoint {Method} {Prefix}{Pattern} from {Handler}.{HandlerMethod}. " +
                        "This route will be unavailable; other routes are unaffected.",
                        attr.HttpMethod, groupAttr.Prefix, attr.Pattern,
                        handlerClass.FullName, method.Name);
                }
            }
        }

        if (totalFailed > 0)
        {
            logger.LogWarning("MapHandlers: mapped {Mapped} endpoints, {Failed} failed.",
                totalMapped, totalFailed);
        }
        else
        {
            logger.LogDebug("MapHandlers: mapped {Mapped} endpoints.", totalMapped);
        }

        return routes;
    }

    private static Type CreateDelegateType(MethodInfo method)
    {
        var paramTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
        var allTypes = paramTypes.Append(method.ReturnType).ToArray();

        return allTypes.Length switch
        {
            1 => typeof(Func<>).MakeGenericType(allTypes),
            2 => typeof(Func<,>).MakeGenericType(allTypes),
            3 => typeof(Func<,,>).MakeGenericType(allTypes),
            4 => typeof(Func<,,,>).MakeGenericType(allTypes),
            5 => typeof(Func<,,,,>).MakeGenericType(allTypes),
            6 => typeof(Func<,,,,,>).MakeGenericType(allTypes),
            7 => typeof(Func<,,,,,,>).MakeGenericType(allTypes),
            8 => typeof(Func<,,,,,,,>).MakeGenericType(allTypes),
            _ => throw new NotSupportedException($"Handler '{method.Name}' has too many parameters.")
        };
    }
}

