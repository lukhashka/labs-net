using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace Platform.Aggregator.Security;

// Lab 09, Client Credentials Flow: the Aggregator calls the services in its OWN name, not in the name of the user.
// POST /connect/token with grant_type=client_credentials returns a token with scopes only, without "sub":
// there is no user context in it, only the identity of the service.
public sealed class ClientCredentialsTokenProvider(IHttpClientFactory httpClients, IConfiguration configuration,
    ILogger<ClientCredentialsTokenProvider> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTime _expiresAt;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTime.UtcNow < _expiresAt)
            return _token;

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTime.UtcNow < _expiresAt)
                return _token;                                    // another request has just refreshed it

            var authority = configuration["services:identityserver:http:0"] ?? configuration["IdentityServer:Authority"]
                ?? throw new InvalidOperationException("The address of IdentityServer is not configured");
            var client = httpClients.CreateClient(nameof(ClientCredentialsTokenProvider));
            using var response = await client.PostAsync($"{authority.TrimEnd('/')}/connect/token", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = configuration["IdentityServer:ClientId"] ?? "aggregator_service",
                    ["client_secret"] = configuration["IdentityServer:ClientSecret"] ?? "",
                    ["scope"] = "orders.read products.read reviews.read"
                }), ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new InvalidOperationException("Empty token response");
            _token = body.AccessToken;
            _expiresAt = DateTime.UtcNow.AddSeconds(Math.Max(body.ExpiresIn - 60, 30));   // renewed a minute before expiry
            logger.LogInformation("Client credentials token received, valid for {Seconds} s", body.ExpiresIn);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

// Adds the token of the Aggregator to every gRPC call to the services
public sealed class ClientCredentialsHandler(ClientCredentialsTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(ct));
        return await base.SendAsync(request, ct);
    }
}
