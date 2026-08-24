using Exb.Data.Services;

namespace Exb.Web.Api;

/// <summary>
/// Bearer-token authentication for the mobile API.
///
/// This is an endpoint filter rather than an authentication handler because the
/// admin console has no authentication scheme of its own to sit alongside, and
/// adding the full ASP.NET Core authentication pipeline for one token lookup
/// would spread a security decision across three files instead of keeping it in
/// one. What matters is that it cannot be forgotten: the endpoints live in
/// groups that carry the filter, so a new route added to a group is protected by
/// construction.
/// </summary>
public static class VisitorAuth
{
    private const string ItemKey = "exb.visitor";

    public static TBuilder RequireVisitor<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var service = http.RequestServices.GetRequiredService<MobileAuthService>();

            var identity = await service.ResolveAsync(MobileApi.BearerToken(http), http.RequestAborted);

            if (identity is null)
            {
                return Results.Json(
                    new { error = "Sign in with your registered email address to use this." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            http.Items[ItemKey] = identity;
            return await next(context);
        });

        return builder;
    }

    /// <summary>
    /// The signed-in visitor. Only valid inside an endpoint behind
    /// <see cref="RequireVisitor{TBuilder}"/>, which is why it throws rather
    /// than returning null: reaching here without one is a routing mistake, and
    /// it should fail loudly in testing rather than quietly serve somebody
    /// else's data.
    /// </summary>
    public static MobileIdentity Visitor(this HttpContext http)
        => http.Items[ItemKey] as MobileIdentity
           ?? throw new InvalidOperationException(
               "No authenticated visitor on this request. The endpoint is missing RequireVisitor().");
}
