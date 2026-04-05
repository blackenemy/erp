using System.Text.Json;
using Shared;
using Xunit;

namespace Shared.Tests;

/// <summary>
/// Unit tests for Quote models
/// </summary>
public class QuoteModelTests
{
    [Fact]
    public void QuoteRequest_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var request = new QuoteRequest(10.5m, "10100", "95120");

        // Act
        var json = JsonSerializer.Serialize(request, AppJsonContext.Options);
        var deserialized = JsonSerializer.Deserialize<QuoteRequest>(json, AppJsonContext.Options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(10.5m, deserialized.WeightKg);
        Assert.Equal("10100", deserialized.OriginZip);
        Assert.Equal("95120", deserialized.DestinationZip);
    }

    [Fact]
    public void QuoteResult_ShouldHaveCorrectCalculations()
    {
        // Arrange & Act
        var result = new QuoteResult
        {
            BasePrice = 100m,
            Discount = 15m,
            Surcharge = 20m,
            FinalPrice = 105m,
            AppliedRules = new() { "WeightTier", "Discount" }
        };

        // Assert
        Assert.Equal(100m, result.BasePrice);
        Assert.Equal(15m, result.Discount);
        Assert.Equal(20m, result.Surcharge);
        Assert.Equal(105m, result.FinalPrice);
        Assert.Equal(2, result.AppliedRules.Count);
    }

    [Fact]
    public void BulkQuoteRequest_ShouldContainMultipleItems()
    {
        // Arrange
        var items = new List<QuoteRequest>
        {
            new(5m, "10100", "95120"),
            new(10m, "20100", "84000"),
            new(15m, "30100", "73000")
        };
        var bulk = new BulkQuoteRequest(items);

        // Act & Assert
        Assert.Equal(3, bulk.Items.Count);
        Assert.Equal(5m, bulk.Items[0].WeightKg);
        Assert.Equal(15m, bulk.Items[2].WeightKg);
    }

    [Fact]
    public void JobRecord_ShouldTrackStatus()
    {
        // Arrange
        var job = new JobRecord();

        // Act
        var initialStatus = job.Status;
        job.Status = "processing";
        job.Results.Add(new QuoteResult
        {
            BasePrice = 100m,
            Discount = 0m,
            Surcharge = 0m,
            FinalPrice = 100m
        });
        job.CompletedAt = DateTime.UtcNow;
        job.Status = "completed";

        // Assert
        Assert.Equal("pending", initialStatus);
        Assert.Equal("completed", job.Status);
        Assert.Single(job.Results);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public void JobRecord_ShouldHaveUniqueJobIds()
    {
        // Arrange & Act
        var job1 = new JobRecord();
        var job2 = new JobRecord();

        // Assert
        Assert.NotEqual(job1.JobId, job2.JobId);
    }
}
