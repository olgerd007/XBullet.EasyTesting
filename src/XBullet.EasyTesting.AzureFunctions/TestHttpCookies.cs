using Microsoft.Azure.Functions.Worker.Http;

namespace XBullet.EasyTesting.AzureFunctions;

internal sealed class TestHttpCookies : HttpCookies
{
    private readonly List<IHttpCookie> _items = [];

    internal IReadOnlyList<IHttpCookie> Items => _items;

    public override void Append(string name, string value) =>
        _items.Add(new HttpCookie(name, value));

    public override void Append(IHttpCookie cookie)
    {
        ArgumentNullException.ThrowIfNull(cookie);
        _items.Add(cookie);
    }

    public override IHttpCookie CreateNew() => new HttpCookie(string.Empty, string.Empty);
}
