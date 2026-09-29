namespace XBullet.EasyTesting.Http;

internal sealed record StubRequestPredicate(
    string Description,
    Func<StubRequestMatchContext, bool> Matches);
