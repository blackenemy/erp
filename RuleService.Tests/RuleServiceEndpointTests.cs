using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Shared;
using Xunit;

namespace RuleService.Tests;

/// <summary>
/// Integration tests for RuleService API endpoints using WebApplicationFactory
/// </summary>
public class RuleServiceEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new DateOnlyJsonConverter(), new NullableDateOnlyJsonConverter() }
    };

    public RuleServiceEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private StringContent JsonBody(string json) =>
        new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Health_ShouldReturn200WithHealthStatus()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
        Assert.Contains("RuleService", body);
    }

    [Fact]
    public async Task GetAllRules_ShouldReturnOkWithList()
    {
        var response = await _client.GetAsync("/rules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.NotNull(body);
        // Should be a valid JSON array
        var rules = JsonSerializer.Deserialize<List<Rule>>(body, _jsonOptions);
        Assert.NotNull(rules);
    }

    [Fact]
    public async Task CreateRule_ValidWeightTierRule_ShouldReturn201()
    {
        var json = """
        {
            "$type": "WeightTier",
            "name": "Integration Test Weight Rule",
            "type": "WeightTier",
            "is_active": true,
            "tiers": [
                { "minKg": 0, "maxKg": 10, "pricePerKg": 12 }
            ]
        }
        """;

        var response = await _client.PostAsync("/rules", JsonBody(json));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var rule = JsonSerializer.Deserialize<Rule>(body, _jsonOptions);
        Assert.NotNull(rule);
        Assert.False(string.IsNullOrEmpty(rule.Id));
        Assert.Equal("Integration Test Weight Rule", rule.Name);

        // Location header should point to created resource
        Assert.Contains($"/rules/{rule.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CreateRule_TimeWindowPromotionRule_ShouldReturn201()
    {
        var json = """
        {
            "$type": "TimeWindowPromotion",
            "name": "Integration Test Promo",
            "type": "TimeWindowPromotion",
            "is_active": true,
            "startTime": "09:00",
            "endTime": "17:00",
            "discountPercent": 25
        }
        """;

        var response = await _client.PostAsync("/rules", JsonBody(json));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateRule_RemoteAreaSurchargeRule_ShouldReturn201()
    {
        var json = """
        {
            "$type": "RemoteAreaSurcharge",
            "name": "Integration Test Remote",
            "type": "RemoteAreaSurcharge",
            "is_active": true,
            "remoteZipPrefixes": ["95", "96"],
            "surchargeFlat": 40
        }
        """;

        var response = await _client.PostAsync("/rules", JsonBody(json));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetRuleById_ExistingRule_ShouldReturn200()
    {
        // Create a rule first
        var createJson = """
        {
            "$type": "WeightTier",
            "name": "GetById Test",
            "type": "WeightTier",
            "is_active": true,
            "tiers": [{ "minKg": 0, "maxKg": 50, "pricePerKg": 8 }]
        }
        """;
        var createResponse = await _client.PostAsync("/rules", JsonBody(createJson));
        var created = JsonSerializer.Deserialize<Rule>(
            await createResponse.Content.ReadAsStringAsync(), _jsonOptions)!;

        // Get by ID
        var response = await _client.GetAsync($"/rules/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rule = JsonSerializer.Deserialize<Rule>(
            await response.Content.ReadAsStringAsync(), _jsonOptions);
        Assert.NotNull(rule);
        Assert.Equal(created.Id, rule.Id);
        Assert.Equal("GetById Test", rule.Name);
    }

    [Fact]
    public async Task GetRuleById_NonExistentRule_ShouldReturn404()
    {
        var response = await _client.GetAsync($"/rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRule_ExistingRule_ShouldReturn200()
    {
        // Create a rule first
        var createJson = """
        {
            "$type": "WeightTier",
            "name": "Before Update",
            "type": "WeightTier",
            "is_active": true,
            "tiers": [{ "minKg": 0, "maxKg": 50, "pricePerKg": 8 }]
        }
        """;
        var createResponse = await _client.PostAsync("/rules", JsonBody(createJson));
        var created = JsonSerializer.Deserialize<Rule>(
            await createResponse.Content.ReadAsStringAsync(), _jsonOptions)!;

        // Update the rule
        var updateJson = """
        {
            "$type": "WeightTier",
            "name": "After Update",
            "type": "WeightTier",
            "is_active": false,
            "tiers": [{ "minKg": 0, "maxKg": 100, "pricePerKg": 5 }]
        }
        """;
        var response = await _client.PutAsync($"/rules/{created.Id}", JsonBody(updateJson));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify the update persisted
        var getResponse = await _client.GetAsync($"/rules/{created.Id}");
        var updated = JsonSerializer.Deserialize<Rule>(
            await getResponse.Content.ReadAsStringAsync(), _jsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("After Update", updated.Name);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task UpdateRule_NonExistentRule_ShouldReturn404()
    {
        var updateJson = """
        {
            "$type": "WeightTier",
            "name": "Ghost Rule",
            "type": "WeightTier",
            "is_active": true,
            "tiers": []
        }
        """;

        var response = await _client.PutAsync($"/rules/{Guid.NewGuid()}", JsonBody(updateJson));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRule_ExistingRule_ShouldReturn204()
    {
        // Create a rule first
        var createJson = """
        {
            "$type": "WeightTier",
            "name": "To Delete",
            "type": "WeightTier",
            "is_active": true,
            "tiers": []
        }
        """;
        var createResponse = await _client.PostAsync("/rules", JsonBody(createJson));
        var created = JsonSerializer.Deserialize<Rule>(
            await createResponse.Content.ReadAsStringAsync(), _jsonOptions)!;

        // Delete
        var response = await _client.DeleteAsync($"/rules/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's gone
        var getResponse = await _client.GetAsync($"/rules/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteRule_NonExistentRule_ShouldReturn404()
    {
        var response = await _client.DeleteAsync($"/rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateAndListRules_ShouldIncludeCreatedRule()
    {
        // Create a rule with a unique name
        var uniqueName = $"List Test {Guid.NewGuid():N}";
        var json = $$"""
        {
            "$type": "RemoteAreaSurcharge",
            "name": "{{uniqueName}}",
            "type": "RemoteAreaSurcharge",
            "is_active": true,
            "remoteZipPrefixes": ["99"],
            "surchargeFlat": 100
        }
        """;
        await _client.PostAsync("/rules", JsonBody(json));

        // List all rules
        var response = await _client.GetAsync("/rules");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(uniqueName, body);
    }
}
