namespace XBullet.EasyTesting.Http;

internal sealed record StubRequestPredicate(
    string Description,
    Func<StubHttpRequest, bool> Matches);
