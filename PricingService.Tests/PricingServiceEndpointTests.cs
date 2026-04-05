using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace PricingService.Tests;

/// <summary>
/// Integration tests for PricingService API endpoints using WebApplicationFactory.
/// RuleServiceClient is replaced with a mock HTTP handler to avoid external dependency.
/// </summary>
public class PricingServiceEndpointTests : IClassFixture<PricingServiceFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public PricingServiceEndpointTests(PricingServiceFactory factory)
    {
        _client = factory.CreateClient();
    }

    private StringContent JsonBody(string json) =>
        new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Health_ShouldReturn200()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
        Assert.Contains("PricingService", body);
    }

    [Fact]
    public async Task QuotePrice_ShouldReturnCalculatedResult()
    {
        var json = """
        {
            "weightKg": 10,
            "originZip": "10100",
            "destinationZip": "20000"
        }
        """;

        var response = await _client.PostAsync("/quotes/price", JsonBody(json));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<QuoteResult>(body, _jsonOptions);
        Assert.NotNull(result);
        // 10 kg falls in tier 5-20 @ 15/kg => basePrice = 150
        Assert.Equal(150m, result.BasePrice);
        Assert.Contains("WeightTier", result.AppliedRules[0]);
    }

    [Fact]
    public async Task QuotePrice_RemoteArea_ShouldApplySurcharge()
    {
        var json = """
        {
            "weightKg": 10,
            "originZip": "10100",
            "destinationZip": "95120"
        }
        """;

        var response = await _client.PostAsync("/quotes/price", JsonBody(json));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonSerializer.Deserialize<QuoteResult>(
            await response.Content.ReadAsStringAsync(), _jsonOptions)!;
        Assert.Equal(150m, result.BasePrice);
        Assert.Equal(50m, result.Surcharge);
        Assert.True(result.AppliedRules.Exists(r => r.Contains("RemoteAreaSurcharge")));
    }

    [Fact]
    public async Task BulkQuote_ShouldReturn202WithJobId()
    {
        var json = """
        {
            "items": [
                { "weightKg": 5, "originZip": "10100", "destinationZip": "20000" },
                { "weightKg": 15, "originZip": "10100", "destinationZip": "95120" }
            ]
        }
        """;

        var response = await _client.PostAsync("/quotes/bulk", JsonBody(json));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("jobId", body);
        Assert.Contains("pending", body);
    }

    [Fact]
    public async Task BulkQuote_ThenGetJob_ShouldReturnCompletedResults()
    {
        var json = """
        {
            "items": [
                { "weightKg": 10, "originZip": "10100", "destinationZip": "20000" }
            ]
        }
        """;

        var bulkResponse = await _client.PostAsync("/quotes/bulk", JsonBody(json));
        var bulkBody = await bulkResponse.Content.ReadAsStringAsync();
        var jobId = JsonDocument.Parse(bulkBody).RootElement.GetProperty("jobId").GetString()!;

        // Poll for completion (background worker processes quickly in tests)
        JobRecord? job = null;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(100);
            var jobResponse = await _client.GetAsync($"/jobs/{jobId}");
            var jobBody = await jobResponse.Content.ReadAsStringAsync();
            job = JsonSerializer.Deserialize<JobRecord>(jobBody, _jsonOptions);
            if (job?.Status is "completed" or "failed")
                break;
        }

        Assert.NotNull(job);
        Assert.Equal("completed", job.Status);
        Assert.Single(job.Results);
        Assert.Equal(150m, job.Results[0].BasePrice);
    }

    [Fact]
    public async Task GetJob_NonExistent_ShouldReturn404()
    {
        var response = await _client.GetAsync($"/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>
/// Custom WebApplicationFactory that replaces the RuleServiceClient HTTP handler
/// with a mock handler returning predefined rules, so tests don't need a running RuleService.
/// </summary>
public class PricingServiceFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing RuleServiceClient registrations
            var descriptors = services
                .Where(d => d.ServiceType == typeof(RuleServiceClient)
                         || d.ServiceType == typeof(IHttpClientFactory))
                .ToList();

            // Configure the existing named HttpClient to use a mock handler
            services.ConfigureHttpClientDefaults(b =>
                b.ConfigurePrimaryHttpMessageHandler(() => new MockRuleServiceHandler()));
        });
    }
}

/// <summary>
/// Mock HTTP handler that returns predefined rules for /rules endpoint.
/// </summary>
internal class MockRuleServiceHandler : HttpMessageHandler
{
    private static readonly string RulesJson = @"
[
  {
    ""$type"": ""WeightTier"",
    ""id"": ""mock-wt1"",
    ""type"": ""WeightTier"",
    ""name"": ""Mock Weight Pricing"",
    ""enabled"": true,
    ""tiers"": [
      { ""minKg"": 0, ""maxKg"": 5, ""pricePerKg"": 20 },
      { ""minKg"": 5.01, ""maxKg"": 20, ""pricePerKg"": 15 },
      { ""minKg"": 20.01, ""maxKg"": 100, ""pricePerKg"": 10 }
    ]
  },
  {
    ""$type"": ""RemoteAreaSurcharge"",
    ""id"": ""mock-ra1"",
    ""type"": ""RemoteAreaSurcharge"",
    ""name"": ""Mock Remote Area"",
    ""enabled"": true,
    ""remoteZipPrefixes"": [""95"", ""96""],
    ""surchargeFlat"": 50
  }
]";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath == "/rules")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(RulesJson, Encoding.UTF8, "application/json")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
