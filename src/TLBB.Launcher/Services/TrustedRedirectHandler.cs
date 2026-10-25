using System.Net;

namespace TLBB.Launcher.Services;

public sealed class TrustedRedirectHandler : DelegatingHandler
{
    private const int MaxRedirects = 5;
    private readonly Func<Uri?, bool> _isTrustedHttpsUrl;

    public TrustedRedirectHandler(HttpMessageHandler innerHandler, Func<Uri?, bool> isTrustedHttpsUrl)
        : base(innerHandler)
    {
        _isTrustedHttpsUrl = isTrustedHttpsUrl;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var currentUri = request.RequestUri;
        if (!_isTrustedHttpsUrl(currentUri))
        {
            throw new HttpRequestException("Yêu cầu HTTPS tới host chưa được tin cậy bị chặn.");
        }

        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        for (var redirectCount = 0; ; redirectCount++)
        {
            HttpResponseMessage response;
            if (redirectCount == 0)
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            else
            {
                using var redirectedRequest = CreateRedirectRequest(request, currentUri!);
                response = await base.SendAsync(redirectedRequest, cancellationToken);
            }

            if (!IsRedirect(response.StatusCode) || response.Headers.Location is null)
            {
                return response;
            }

            if (redirectCount >= MaxRedirects)
            {
                response.Dispose();
                throw new HttpRequestException("Máy chủ chuyển hướng quá nhiều lần.");
            }

            var nextUri = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(currentUri!, response.Headers.Location);
            response.Dispose();

            if (!_isTrustedHttpsUrl(nextUri))
            {
                throw new HttpRequestException($"Chuyển hướng tới host HTTPS chưa được tin cậy bị chặn: {nextUri.Host}.");
            }

            currentUri = nextUri;
        }
    }

    private static HttpRequestMessage CreateRedirectRequest(HttpRequestMessage original, Uri target)
    {
        var redirected = new HttpRequestMessage(original.Method, target)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy
        };

        foreach (var header in original.Headers)
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            redirected.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return redirected;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Redirect or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;
}
