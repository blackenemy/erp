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
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 0;
    [JsonPropertyName("effective_from")]
    public DateOnly? EffectiveFrom { get; set; } = null;
    [JsonPropertyName("effective_to")]
    public DateOnly? EffectiveTo { get; set; } = null;
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
//  JSON serialization options
// ══════════════════════════════════════════════

public sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    private const string Format = "yyyy-MM-dd";

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (string.IsNullOrEmpty(str))
            throw new JsonException("DateOnly value cannot be null or empty");
        return DateOnly.ParseExact(str, Format, System.Globalization.CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(Format, System.Globalization.CultureInfo.InvariantCulture));
}

public sealed class NullableDateOnlyJsonConverter : JsonConverter<DateOnly?>
{
    private const string Format = "yyyy-MM-dd";

    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var str = reader.GetString();
        if (string.IsNullOrEmpty(str))
            return null;

        return DateOnly.ParseExact(str, Format, System.Globalization.CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value.ToString(Format, System.Globalization.CultureInfo.InvariantCulture));
    }
}

public static class AppJsonContext
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new DateOnlyJsonConverter(), new NullableDateOnlyJsonConverter() }
    };
}
