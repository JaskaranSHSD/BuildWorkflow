using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace BuildWorkflow.AICenter.Helpers;

public class ApiToolHelper
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;

    public ApiToolHelper(HttpClient httpClient, IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
    }

    public async Task<string> GetAsync(string url, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(ResolveUrl(url), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> PostJsonAsync(string url, object payload, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(ResolveUrl(url), payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private string ResolveUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            return url;
        }

        string baseUrl;
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request != null)
        {
            baseUrl = $"{request.Scheme}://{request.Host}";
        }
        else
        {
            baseUrl = _configuration["BaseUrl"] ?? _configuration["App:BaseUrl"] ?? "http://localhost:5000";
        }

        return new Uri(new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/"), url.TrimStart('/')).ToString();
    }

    public static string PrettyPrintJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }
}
