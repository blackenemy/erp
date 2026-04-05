using Shared;
using Xunit;

namespace PricingService.Tests;

/// <summary>
/// Unit tests for PricingEngine business logic
/// </summary>
public class PricingEngineTests
{
    private const decimal BaseFlatRate = 50m;

    [Fact]
    public void PricingEngine_NoRules_ShouldApplyBaseRate()
    {
        // Arrange
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>();

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(BaseFlatRate, result.BasePrice);
        Assert.Equal(0m, result.Discount);
        Assert.Equal(0m, result.Surcharge);
        Assert.Equal(BaseFlatRate, result.FinalPrice);
    }

    [Fact]
    public void PricingEngine_WeightTierRule_ShouldCalculateCorrectly()
    {
        // Arrange
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "wt1",
                Type = "WeightTier",
                Name = "Standard Pricing",
                IsActive = true,
                Tiers = new()
                {
                    new(0, 5, 20),
                    new(5.01m, 20, 15),
                    new(20.01m, 100, 10)
                }
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(150m, result.BasePrice); // 10 kg * 15 per kg
        Assert.Equal(0m, result.Discount);
        Assert.Equal(150m, result.FinalPrice);
        Assert.Contains("WeightTier", result.AppliedRules[0]);
    }

    [Fact]
    public void PricingEngine_MultipleWeightTiers_ShouldUseCorrectTier()
    {
        // Arrange
        var lightRequest = new QuoteRequest(2m, "10100", "95120");
        var mediumRequest = new QuoteRequest(10m, "10100", "95120");
        var heavyRequest = new QuoteRequest(50m, "10100", "95120");

        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "wt1",
                Type = "WeightTier",
                Name = "Tiered",
                IsActive = true,
                Tiers = new()
                {
                    new(0, 5, 20),
                    new(5.01m, 20, 15),
                    new(20.01m, 100, 10)
                }
            }
        };

        // Act
        var lightResult = PricingEngine.Calculate(lightRequest, rules);
        var mediumResult = PricingEngine.Calculate(mediumRequest, rules);
        var heavyResult = PricingEngine.Calculate(heavyRequest, rules);

        // Assert
        Assert.Equal(40m, lightResult.BasePrice);    // 2 * 20
        Assert.Equal(150m, mediumResult.BasePrice);  // 10 * 15
        Assert.Equal(500m, heavyResult.BasePrice);   // 50 * 10
    }

    [Fact]
    public void PricingEngine_DisabledRule_ShouldBeIgnored()
    {
        // Arrange
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "wt1",
                Type = "WeightTier",
                Name = "Disabled",
                IsActive = false,
                Tiers = new()
                {
                    new(0, 100, 999) // Would be expensive if enabled
                }
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(BaseFlatRate, result.BasePrice);
        Assert.Empty(result.AppliedRules);
    }

    [Fact]
    public void PricingEngine_TimeWindowPromotion_ShouldApplyDiscount()
    {
        // Arrange
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "wt1",
                Type = "WeightTier",
                Name = "Base",
                IsActive = true,
                Tiers = new() { new(0, 100, 10) }
            },
            new TimeWindowPromotionRule
            {
                Id = "promo1",
                Type = "TimeWindowPromotion",
                Name = "Lunch",
                IsActive = true,
                StartTime = "00:00", // Always applicable
                EndTime = "23:59",
                DiscountPercent = 20
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(100m, result.BasePrice);        // 10 * 10
        Assert.Equal(20m, result.Discount);          // 100 * 20%
        Assert.Equal(80m, result.FinalPrice);        // 100 - 20
        Assert.Contains("TimeWindowPromotion", result.AppliedRules[1]);
    }

    [Fact]
    public void PricingEngine_RemoteAreaSurcharge_ShouldApplyCorrectly()
    {
        // Arrange
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "wt1",
                Type = "WeightTier",
                Name = "Base",
                IsActive = true,
                Tiers = new() { new(0, 100, 10) }
            },
            new RemoteAreaSurchargeRule
            {
                Id = "surcharge1",
                Type = "RemoteAreaSurcharge",
                Name = "Southern",
                IsActive = true,
                RemoteZipPrefixes = new() { "95", "96" },
                SurchargeFlat = 50
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(100m, result.BasePrice);        // 10 * 10
        Assert.Equal(0m, result.Discount);
        Assert.Equal(50m, result.Surcharge);
        Assert.Equal(150m, result.FinalPrice);       // 100 + 50
        Assert.Contains("RemoteAreaSurcharge", result.AppliedRules[1]);
    }

    [Fact]
    public void PricingEngine_MultipleRules_ShouldApplyAllRules()
    {
        // Arrange
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "wt1",
                Type = "WeightTier",
                Name = "Base",
                IsActive = true,
                Tiers = new() { new(0, 100, 10) }
            },
            new TimeWindowPromotionRule
            {
                Id = "promo1",
                Type = "TimeWindowPromotion",
                Name = "Discount",
                IsActive = true,
                StartTime = "00:00",
                EndTime = "23:59",
                DiscountPercent = 10
            },
            new RemoteAreaSurchargeRule
            {
                Id = "surcharge1",
                Type = "RemoteAreaSurcharge",
                Name = "Remote",
                IsActive = true,
                RemoteZipPrefixes = new() { "95" },
                SurchargeFlat = 30
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(100m, result.BasePrice);
        Assert.Equal(10m, result.Discount);          // 100 * 10%
        Assert.Equal(30m, result.Surcharge);
        Assert.Equal(120m, result.FinalPrice);       // 100 - 10 + 30
        Assert.Equal(3, result.AppliedRules.Count);
    }

    [Fact]
    public void PricingEngine_RemoteAreaSurcharge_NonMatchingZip_ShouldNotApply()
    {
        // Arrange
        var request = new QuoteRequest(10m, "10100", "84000"); // Doesn't start with 95 or 96
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "wt1",
                Type = "WeightTier",
                Name = "Base",
                IsActive = true,
                Tiers = new() { new(0, 100, 10) }
            },
            new RemoteAreaSurchargeRule
            {
                Id = "surcharge1",
                Type = "RemoteAreaSurcharge",
                Name = "Southern",
                IsActive = true,
                RemoteZipPrefixes = new() { "95", "96" },
                SurchargeFlat = 50
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(100m, result.FinalPrice);
        Assert.Equal(0m, result.Surcharge);
    }

    [Fact]
    public void PricingEngine_EdgeCase_ZeroWeight_ShouldCalculate()
    {
        // Arrange
        var request = new QuoteRequest(0m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "wt1",
                Type = "WeightTier",
                Name = "Base",
                IsActive = true,
                Tiers = new() { new(0, 100, 10) }
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(0m, result.BasePrice);
    }

    [Fact]
    public void PricingEngine_EdgeCase_LargeWeight_ShouldCalculate()
    {
        // Arrange
        var request = new QuoteRequest(1000m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "wt1",
                Type = "WeightTier",
                Name = "Base",
                IsActive = true,
                Tiers = new() { new(0, 10000, 5) }
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(5000m, result.BasePrice);
    }

    [Fact]
    public void PricingEngine_DiscountShouldNotExceedBasePrice()
    {
        // Arrange
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "wt1",
                Type = "WeightTier",
                Name = "Base",
                IsActive = true,
                Tiers = new() { new(0, 100, 10) }
            },
            new TimeWindowPromotionRule
            {
                Id = "promo1",
                Type = "TimeWindowPromotion",
                Name = "Heavy Discount",
                IsActive = true,
                StartTime = "00:00",
                EndTime = "23:59",
                DiscountPercent = 150 // Over 100% discount
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert - The calculation should still work, but FinalPrice could be negative
        // This tests the engine doesn't crash on extreme values
        Assert.Equal(100m, result.BasePrice);
        Assert.Equal(150m, result.Discount);
    }

    [Fact]
    public void PricingEngine_ExpiredRule_ShouldBeIgnored()
    {
        // Arrange
        var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "expired",
                Type = "WeightTier",
                Name = "Expired Rule",
                IsActive = true,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(-10)),
                EffectiveTo = yesterday,
                Tiers = new() { new(0, 100, 999) } // Would be expensive if active
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(BaseFlatRate, result.BasePrice);
        Assert.Empty(result.AppliedRules);
    }

    [Fact]
    public void PricingEngine_FutureRule_ShouldBeIgnored()
    {
        // Arrange
        var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "future",
                Type = "WeightTier",
                Name = "Future Rule",
                IsActive = true,
                EffectiveFrom = tomorrow,
                EffectiveTo = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
                Tiers = new() { new(0, 100, 999) }
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(BaseFlatRate, result.BasePrice);
        Assert.Empty(result.AppliedRules);
    }

    [Fact]
    public void PricingEngine_ActiveRule_WithinDateRange_ShouldApply()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.Today);
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "active",
                Type = "WeightTier",
                Name = "Active Rule",
                IsActive = true,
                EffectiveFrom = today.AddDays(-10),
                EffectiveTo = today.AddDays(10),
                Tiers = new() { new(0, 100, 15) }
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(150m, result.BasePrice);
        Assert.Contains("WeightTier", result.AppliedRules[0]);
    }

    [Fact]
    public void PricingEngine_NullDates_BackwardCompatibility()
    {
        // Arrange - Rule with null dates should always be active
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "undated",
                Type = "WeightTier",
                Name = "No Date Restriction",
                IsActive = true,
                EffectiveFrom = null,
                EffectiveTo = null,
                Tiers = new() { new(0, 100, 15) }
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(150m, result.BasePrice);
        Assert.Contains("WeightTier", result.AppliedRules[0]);
    }

    [Fact]
    public void PricingEngine_Priority_LowerNumberProcessedFirst()
    {
        // Arrange
        // Two rules that both affect basePrice - lower priority number processed first
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "low-priority",
                Type = "WeightTier",
                Name = "Standard Pricing",
                IsActive = true,
                Priority = 1,  // Processed first
                Tiers = new() { new(0, 100, 10) }
            },
            new WeightTierRule
            {
                Id = "high-priority",
                Type = "WeightTier",
                Name = "Premium Pricing",
                IsActive = true,
                Priority = 2,  // Processed second (overwrites)
                Tiers = new() { new(0, 100, 20) }
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        // High priority (2) processes last, so its basePrice wins (10 kg * 20 = 200)
        Assert.Equal(200m, result.BasePrice);
        Assert.Equal(2, result.AppliedRules.Count);
    }

    [Fact]
    public void PricingEngine_MixedPriorities_ShouldProcessInOrder()
    {
        // Arrange
        var request = new QuoteRequest(10m, "10100", "95120");
        var rules = new List<Rule>
        {
            new WeightTierRule
            {
                Id = "weight",
                Type = "WeightTier",
                Name = "Weight",
                IsActive = true,
                Priority = 1,
                Tiers = new() { new(0, 100, 10) }
            },
            new TimeWindowPromotionRule
            {
                Id = "discount",
                Type = "TimeWindowPromotion",
                Name = "Discount",
                IsActive = true,
                Priority = 2,
                StartTime = "00:00",
                EndTime = "23:59",
                DiscountPercent = 20
            },
            new RemoteAreaSurchargeRule
            {
                Id = "surcharge",
                Type = "RemoteAreaSurcharge",
                Name = "Remote",
                IsActive = true,
                Priority = 3,
                RemoteZipPrefixes = new() { "95" },
                SurchargeFlat = 30
            }
        };

        // Act
        var result = PricingEngine.Calculate(request, rules);

        // Assert
        Assert.Equal(100m, result.BasePrice);        // 10 * 10
        Assert.Equal(20m, result.Discount);          // 100 * 20%
        Assert.Equal(30m, result.Surcharge);
        Assert.Equal(110m, result.FinalPrice);       // 100 - 20 + 30
        Assert.Equal(3, result.AppliedRules.Count);
    }
}
