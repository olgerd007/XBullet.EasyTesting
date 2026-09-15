using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;

namespace XBullet.EasyTesting.Hosting;

internal static class TestHttpClientFactory
{
    internal static HttpClient Create<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory,
        WebApplicationFactoryClientOptions options,
        IReadOnlyCollection<DelegatingHandler> additionalHandlers)
        where TEntryPoint : class
    {
        if (additionalHandlers.Count == 0)
        {
            return factory.CreateClient(options);
        }

        var handlers = new List<DelegatingHandler>();
        if (options.AllowAutoRedirect)
        {
            handlers.Add(new RedirectHandler(options.MaxAutomaticRedirections));
        }

        if (options.HandleCookies)
        {
            handlers.Add(new CookieContainerHandler(new CookieContainer()));
        }

        handlers.AddRange(additionalHandlers);
        return factory.CreateDefaultClient(options.BaseAddress, handlers.ToArray());
    }
}
