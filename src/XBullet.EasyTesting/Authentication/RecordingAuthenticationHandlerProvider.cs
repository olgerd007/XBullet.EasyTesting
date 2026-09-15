using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace XBullet.EasyTesting.Authentication;

internal sealed class RecordingAuthenticationHandlerProvider(
    IAuthenticationHandlerProvider inner,
    TestAuthenticationEventRecorder recorder) : IAuthenticationHandlerProvider
{
    private readonly ConcurrentDictionary<string, IAuthenticationHandler> _handlers =
        new(StringComparer.Ordinal);

    public async Task<IAuthenticationHandler?> GetHandlerAsync(
        HttpContext context,
        string authenticationScheme)
    {
        if (_handlers.TryGetValue(authenticationScheme, out var cached))
        {
            return cached;
        }

        var handler = await inner.GetHandlerAsync(context, authenticationScheme);
        if (handler is null)
        {
            return null;
        }

        var recordingHandler = new RecordingAuthenticationHandler(
                handler,
                recorder,
                context,
                authenticationScheme);
        _handlers.TryAdd(authenticationScheme, recordingHandler);
        return recordingHandler;
    }

    private sealed class RecordingAuthenticationHandler(
        IAuthenticationHandler inner,
        TestAuthenticationEventRecorder recorder,
        HttpContext context,
        string authenticationScheme) : IAuthenticationHandler
    {
        private int _authenticateRecorded;
        private int _challengeRecorded;
        private int _forbidRecorded;

        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext httpContext) =>
            inner.InitializeAsync(scheme, httpContext);

        public async Task<AuthenticateResult> AuthenticateAsync()
        {
            var result = await inner.AuthenticateAsync();
            if (Interlocked.Exchange(ref _authenticateRecorded, 1) == 0)
            {
                if (result.Succeeded)
                {
                    Record(TestAuthenticationEventKind.TokenValidated);
                }
                else if (result.Failure is not null)
                {
                    Record(TestAuthenticationEventKind.ValidationFailed, result.Failure);
                }
            }

            return result;
        }

        public async Task ChallengeAsync(AuthenticationProperties? properties)
        {
            await inner.ChallengeAsync(properties);
            if (Interlocked.Exchange(ref _challengeRecorded, 1) == 0)
            {
                Record(TestAuthenticationEventKind.Challenge);
            }
        }

        public async Task ForbidAsync(AuthenticationProperties? properties)
        {
            await inner.ForbidAsync(properties);
            if (Interlocked.Exchange(ref _forbidRecorded, 1) == 0)
            {
                Record(TestAuthenticationEventKind.Forbidden);
            }
        }

        private void Record(TestAuthenticationEventKind kind, Exception? failure = null) =>
            recorder.Record(
                kind,
                authenticationScheme,
                context.Request.Method,
                context.Request.Path,
                failure);
    }
}
