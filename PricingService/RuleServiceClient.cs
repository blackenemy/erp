using System.Text.Json;
using System.Text.Json.Serialization;
using Shared;

namespace PricingService;

/// <summary>
/// Typed HTTP client for communicating with RuleService
/// Fetches pricing rules for quote calculations
/// </summary>
public sealed class RuleServiceClient(HttpClient http)
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public async Task<List<Rule>> GetRulesAsync()
    {
        var stream = await http.GetStreamAsync("/rules");
        return await JsonSerializer.DeserializeAsync<List<Rule>>(stream, _options) ?? [];
    }
}
