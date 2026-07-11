using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Configuration;

namespace RestLib.Endpoints;

/// <summary>
/// Evaluates authorization after a shared batch endpoint has parsed its requested action.
/// </summary>
internal static class BatchAuthorizationHelper
{
    /// <summary>
    /// Authorizes the requested batch operation using its operation-specific configuration.
    /// </summary>
    /// <typeparam name="TEntity">The exposed entity type.</typeparam>
    /// <typeparam name="TKey">The resource key type.</typeparam>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="options">The resolved RestLib options.</param>
    /// <param name="operation">The parsed batch operation.</param>
    /// <returns>An authorization failure result, or <c>null</c> when access is allowed.</returns>
    internal static async Task<IResult?> AuthorizeAsync<TEntity, TKey>(
        HttpContext httpContext,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        RestLibOptions options,
        RestLibOperation operation)
        where TEntity : class
        where TKey : notnull
    {
        var endpointMetadata = httpContext.GetEndpoint()?.Metadata;
        var hasExternalAnonymousMetadata = endpointMetadata?
            .GetOrderedMetadata<IAllowAnonymous>()
            .Any(static metadata => metadata is not BatchAuthorizationBypassMetadata) == true;
        if (config.IsAnonymous(operation) || hasExternalAnonymousMetadata)
        {
            return null;
        }

        var authorizeData = endpointMetadata?
            .GetOrderedMetadata<IAuthorizeData>()
            .ToList() ?? [];
        var authorizationPolicies = endpointMetadata?
            .GetOrderedMetadata<AuthorizationPolicy>()
            .ToList() ?? [];
        var requirementData = endpointMetadata?
            .GetOrderedMetadata<IAuthorizationRequirementData>();
        if (requirementData is { Count: > 0 })
        {
            authorizationPolicies.Add(new AuthorizationPolicy(
                requirementData.SelectMany(static data => data.GetRequirements()),
                []));
        }

        var policies = config.GetPolicies(operation);

        if (policies is { Length: > 0 })
        {
            authorizeData.AddRange(policies.Select(static policy => new AuthorizeAttribute(policy)));
        }
        else if (options.RequireAuthorizationByDefault)
        {
            authorizeData.Add(new AuthorizeAttribute());
        }

        var policyProvider = httpContext.RequestServices.GetService<IAuthorizationPolicyProvider>();
        if (policyProvider is null)
        {
            if (authorizeData.Count == 0 && authorizationPolicies.Count == 0)
            {
                return null;
            }

            throw new InvalidOperationException(
                "Authorization services are required for the configured batch operation. " +
                "Register them by calling AddAuthorization().");
        }

        var policy = await AuthorizationPolicy.CombineAsync(
            policyProvider,
            authorizeData,
            authorizationPolicies);
        if (policy is null)
        {
            return null;
        }

        var policyEvaluator = httpContext.RequestServices.GetRequiredService<IPolicyEvaluator>();
        var authenticationResult = await policyEvaluator.AuthenticateAsync(policy, httpContext);
        var authorizationResult = await policyEvaluator.AuthorizeAsync(
            policy,
            authenticationResult,
            httpContext,
            httpContext);

        if (authorizationResult.Succeeded)
        {
            return null;
        }

        var resultHandler = httpContext.RequestServices
            .GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        await resultHandler.HandleAsync(
            static _ => Task.CompletedTask,
            httpContext,
            policy,
            authorizationResult);

        return Results.Empty;
    }
}
