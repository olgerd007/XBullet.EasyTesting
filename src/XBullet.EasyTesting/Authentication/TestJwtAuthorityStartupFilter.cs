using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace XBullet.EasyTesting.Authentication;

internal sealed class TestJwtAuthorityStartupFilter(TestJwtAuthority authority) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
    {
        application.Use(async (context, continuePipeline) =>
        {
            if (context.Request.Path.Equals(
                TestJwtAuthority.NormalizePath(authority.Options.DiscoveryPath),
                StringComparison.OrdinalIgnoreCase))
            {
                await context.Response.WriteAsJsonAsync(authority.GetDiscoveryDocument());
                return;
            }

            if (context.Request.Path.Equals(
                TestJwtAuthority.NormalizePath(authority.Options.JwksPath),
                StringComparison.OrdinalIgnoreCase))
            {
                await context.Response.WriteAsJsonAsync(authority.GetJsonWebKeySet());
                return;
            }

            await continuePipeline();
        });
        next(application);
    };
}
