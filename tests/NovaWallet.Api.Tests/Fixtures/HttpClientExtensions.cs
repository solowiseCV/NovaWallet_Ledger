using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NovaWallet.Api.Tests.Fixtures;

internal static class HttpClientExtensions
{
    /// <summary>Calls the real POST /api/auth/token endpoint to mint a token and
    /// attaches it as a Bearer header — exercises the actual auth pipeline rather
    /// than faking a ClaimsPrincipal directly.</summary>
    public static async Task<HttpClient> AuthenticatedAsAsync(this HttpClient client, string customerId)
    {
        var response = await client.PostAsJsonAsync("/api/auth/token", new { customerId });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
        return client;
    }

    private record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
