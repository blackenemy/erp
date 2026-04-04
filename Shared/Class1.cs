using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared;

// ══════════════════════════════════════════════
//  Rule Models — polymorphic via JsonDerivedType
// ══════════════════════════════════════════════

[JsonDerivedType(typeof(TimeWindowPromotionRule), "TimeWindowPromotion")]
[JsonDerivedType(typeof(RemoteAreaSurchargeRule), "RemoteAreaSurcharge")]
[JsonDerivedType(typeof(WeightTierRule), "WeightTier")]
public abstract record Rule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Type { get; init; }
    public required string Name { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed record TimeWindowPromotionRule : Rule
{
    public TimeWindowPromotionRule() { Type = "TimeWindowPromotion"; }
    public string StartTime { get; init; } = "00:00";      // HH:mm
    public string EndTime { get; init; } = "23:59";
    public decimal DiscountPercent { get; init; }           // 10 = 10 %
}

public sealed record RemoteAreaSurchargeRule : Rule
{
    public RemoteAreaSurchargeRule() { Type = "RemoteAreaSurcharge"; }
    public List<string> RemoteZipPrefixes { get; init; } = [];
    public decimal SurchargeFlat { get; init; }
}

public sealed record WeightTierRule : Rule
{
    public WeightTierRule() { Type = "WeightTier"; }
    public List<WeightTier> Tiers { get; init; } = [];
}

public sealed record WeightTier(decimal MinKg, decimal MaxKg, decimal PricePerKg);

// ══════════════════════════════════════════════
//  Quote Models
// ══════════════════════════════════════════════

public sealed record QuoteRequest(
    decimal WeightKg,
    string OriginZip,
    string DestinationZip);

public sealed record QuoteResult
{
    public required decimal BasePrice { get; init; }
    public required decimal Discount { get; init; }
    public required decimal Surcharge { get; init; }
    public required decimal FinalPrice { get; init; }
    public List<string> AppliedRules { get; init; } = [];
}

// ══════════════════════════════════════════════
//  Bulk / Job Models
// ══════════════════════════════════════════════

public sealed record BulkQuoteRequest(List<QuoteRequest> Items);

public sealed record JobRecord
{
    public string JobId { get; init; } = Guid.NewGuid().ToString();
    public string Status { get; set; } = "pending";         // pending → processing → completed | failed
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<QuoteResult> Results { get; init; } = [];
}

// ══════════════════════════════════════════════
//  Source-generated JSON context (AOT-friendly)
// ══════════════════════════════════════════════

[JsonSerializable(typeof(Rule))]
[JsonSerializable(typeof(List<Rule>))]
[JsonSerializable(typeof(TimeWindowPromotionRule))]
[JsonSerializable(typeof(RemoteAreaSurchargeRule))]
[JsonSerializable(typeof(WeightTierRule))]
[JsonSerializable(typeof(QuoteRequest))]
[JsonSerializable(typeof(QuoteResult))]
[JsonSerializable(typeof(BulkQuoteRequest))]
[JsonSerializable(typeof(JobRecord))]
[JsonSerializable(typeof(List<JobRecord>))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class AppJsonContext : JsonSerializerContext;
