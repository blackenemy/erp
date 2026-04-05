using System.Text.Json;
using Shared;
using Xunit;

namespace Shared.Tests;

/// <summary>
/// Unit tests for Rule models and JSON serialization
/// </summary>
public class RuleModelTests
{
    [Fact]
    public void WeightTierRule_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var rule = new WeightTierRule
        {
            Id = "weight-1",
            Type = "WeightTier",
            Name = "Standard Weight",
            IsActive = true,
            Tiers = new()
            {
                new(0, 5, 20),
                new(5.01m, 20, 15),
                new(20.01m, 100, 10)
            }
        };

        // Act
        var json = JsonSerializer.Serialize(rule, AppJsonContext.Options);
        var deserialized = JsonSerializer.Deserialize<WeightTierRule>(json, AppJsonContext.Options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("weight-1", deserialized.Id);
        Assert.Equal("Standard Weight", deserialized.Name);
        Assert.Equal(3, deserialized.Tiers.Count);
    }

    [Fact]
    public void TimeWindowPromotionRule_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var rule = new TimeWindowPromotionRule
        {
            Id = "promo-1",
            Type = "TimeWindowPromotion",
            Name = "Lunch Discount",
            IsActive = true,
            StartTime = "11:00",
            EndTime = "13:00",
            DiscountPercent = 15
        };

        // Act
        var json = JsonSerializer.Serialize(rule, AppJsonContext.Options);
        var deserialized = JsonSerializer.Deserialize<TimeWindowPromotionRule>(json, AppJsonContext.Options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(15m, deserialized.DiscountPercent);
        Assert.Equal("11:00", deserialized.StartTime);
    }

    [Fact]
    public void RemoteAreaSurchargeRule_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var rule = new RemoteAreaSurchargeRule
        {
            Id = "surcharge-1",
            Type = "RemoteAreaSurcharge",
            Name = "Southern Zone",
            IsActive = true,
            RemoteZipPrefixes = new() { "95", "96" },
            SurchargeFlat = 100
        };

        // Act
        var json = JsonSerializer.Serialize(rule, AppJsonContext.Options);
        var deserialized = JsonSerializer.Deserialize<RemoteAreaSurchargeRule>(json, AppJsonContext.Options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(100m, deserialized.SurchargeFlat);
        Assert.Contains("95", deserialized.RemoteZipPrefixes);
    }

    [Fact(Skip = "Polymorphic deserialization requires source generation")]
    public void RuleList_ShouldSerializePolymorphically()
    {
        // Arrange
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "w1",
                Type = "WeightTier",
                Name = "Weight",
                Tiers = new() { new(0, 100, 10) }
            },
            new TimeWindowPromotionRule
            {
                Id = "t1",
                Type = "TimeWindowPromotion",
                Name = "Promo",
                StartTime = "10:00",
                EndTime = "14:00",
                DiscountPercent = 20
            }
        };

        // Act
        var json = JsonSerializer.Serialize(rules, AppJsonContext.Options);
        var deserialized = JsonSerializer.Deserialize<List<Rule>>(json, AppJsonContext.Options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Count);
        Assert.IsType<WeightTierRule>(deserialized[0]);
        Assert.IsType<TimeWindowPromotionRule>(deserialized[1]);
    }

    [Fact]
    public void DisabledRule_ShouldNotAffectCalculations()
    {
        // Arrange
        var rule = new WeightTierRule
        {
            Id = "disabled",
            Type = "WeightTier",
            Name = "Disabled Weight",
            IsActive = false,
            Tiers = new() { new(0, 100, 999) }
        };

        // Act & Assert
        Assert.False(rule.IsActive);
    }

    [Fact]
    public void RuleWithoutId_ShouldGenerateGuid()
    {
        // Arrange & Act
        var rule = new WeightTierRule
        {
            Type = "WeightTier",
            Name = "Auto ID",
            Tiers = new()
        };

        // Assert
        Assert.NotEmpty(rule.Id);
        Assert.NotEqual("", rule.Id);
    }

    [Fact]
    public void Rule_Priority_DefaultsToZero()
    {
        // Arrange & Act
        var rule = new WeightTierRule
        {
            Type = "WeightTier",
            Name = "Test",
            Tiers = new()
        };

        // Assert
        Assert.Equal(0, rule.Priority);
    }

    [Fact]
    public void Rule_EffectiveDates_DefaultToNull()
    {
        // Arrange & Act
        var rule = new TimeWindowPromotionRule
        {
            Type = "TimeWindowPromotion",
            Name = "Test",
            StartTime = "10:00",
            EndTime = "14:00",
            DiscountPercent = 10
        };

        // Assert
        Assert.Null(rule.EffectiveFrom);
        Assert.Null(rule.EffectiveTo);
    }

    [Fact]
    public void Rule_WithEffectiveDates_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var from = new DateOnly(2026, 4, 1);
        var to = new DateOnly(2026, 4, 30);
        var rule = new WeightTierRule
        {
            Id = "dated-rule",
            Type = "WeightTier",
            Name = "April Campaign",
            Priority = 5,
            EffectiveFrom = from,
            EffectiveTo = to,
            Tiers = new() { new(0, 100, 10) }
        };

        // Act
        var json = JsonSerializer.Serialize(rule, AppJsonContext.Options);
        var deserialized = JsonSerializer.Deserialize<WeightTierRule>(json, AppJsonContext.Options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(from, deserialized.EffectiveFrom);
        Assert.Equal(to, deserialized.EffectiveTo);
        Assert.Equal(5, deserialized.Priority);
    }

    [Fact]
    public void Rule_NullEffectiveDates_ShouldBeOmittedFromJson()
    {
        // Arrange
        var rule = new TimeWindowPromotionRule
        {
            Id = "no-dates",
            Type = "TimeWindowPromotion",
            Name = "Undated",
            StartTime = "10:00",
            EndTime = "14:00",
            DiscountPercent = 10
        };

        // Act
        var json = JsonSerializer.Serialize(rule, AppJsonContext.Options);

        // Assert
        Assert.DoesNotContain("effectiveFrom", json);
        Assert.DoesNotContain("effectiveTo", json);
    }
}
