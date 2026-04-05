using System.Collections.Concurrent;
using System.Text.Json;
using Shared;

namespace PricingService;

/// <summary>
/// Pure static pricing calculation engine
/// All logic is deterministic and side-effect free
/// </summary>
public static class PricingEngine
{
    private const decimal BaseFlatRate = 50m;

    public static QuoteResult Calculate(QuoteRequest req, List<Rule> rules)
    {
        var basePrice = BaseFlatRate;
        var discount = 0m;
        var surcharge = 0m;
        var applied = new List<string>();

        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var rule in rules
            .Where(r => r.IsActive
                     && (r.EffectiveFrom == null || today >= r.EffectiveFrom)
                     && (r.EffectiveTo == null || today <= r.EffectiveTo))
            .OrderBy(r => r.Priority))
        {
            switch (rule)
            {
                case WeightTierRule wt:
                    var matched = wt.Tiers.FirstOrDefault(t =>
                        req.WeightKg >= t.MinKg && req.WeightKg <= t.MaxKg);
                    if (matched is not null)
                    {
                        basePrice = req.WeightKg * matched.PricePerKg;
                        applied.Add($"WeightTier: {matched.PricePerKg}/kg");
                    }
                    break;

                case TimeWindowPromotionRule tw:
                    var now = TimeProvider.System.GetLocalNow().TimeOfDay;
                    if (TimeSpan.TryParse(tw.StartTime, out var start) &&
                        TimeSpan.TryParse(tw.EndTime, out var end) &&
                        now >= start && now <= end)
                    {
                        discount += basePrice * (tw.DiscountPercent / 100m);
                        applied.Add($"TimeWindowPromotion: -{tw.DiscountPercent}%");
                    }
                    break;

                case RemoteAreaSurchargeRule ra:
                    if (ra.RemoteZipPrefixes.Exists(p => req.DestinationZip.StartsWith(p)))
                    {
                        surcharge += ra.SurchargeFlat;
                        applied.Add($"RemoteAreaSurcharge: +{ra.SurchargeFlat}");
                    }
                    break;
            }
        }

        return new QuoteResult
        {
            BasePrice = basePrice,
            Discount = discount,
            Surcharge = surcharge,
            FinalPrice = basePrice - discount + surcharge,
            AppliedRules = applied
        };
    }
}
