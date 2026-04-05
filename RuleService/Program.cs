using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.RateLimiting;
using RuleService;
using Scalar.AspNetCore;
using Shared;

var builder = WebApplication.CreateBuilder(args);

// ── Logging Configuration ──
var logLevel = builder.Configuration["LOG_LEVEL"] ?? "Information";
if (Enum.TryParse<LogLevel>(logLevel, out var level))
    builder.Logging.SetMinimumLevel(level);

builder.Logging.AddConsole();
if (bool.TryParse(builder.Configuration["STRUCTURED_LOGGING"], out var structured) && structured)
    builder.Logging.AddJsonConsole();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.WriteIndented = true;
    o.SerializerOptions.Converters.Add(new DateOnlyJsonConverter());
    o.SerializerOptions.Converters.Add(new NullableDateOnlyJsonConverter());
    o.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver().WithAddedModifier(x =>
    {
        if (x.Type == typeof(Rule))
        {
            x.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(TimeWindowPromotionRule), "TimeWindowPromotion"),
                    new JsonDerivedType(typeof(RemoteAreaSurchargeRule), "RemoteAreaSurcharge"),
                    new JsonDerivedType(typeof(WeightTierRule), "WeightTier")
                }
            };
        }
    });
});

builder.Services.AddSingleton<RuleStore>();

// ── Rate Limiting (100 concurrent requests) ──
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetConcurrencyLimiter(
            partitionKey: string.Empty,
            factory: _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 100,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 50
            }));
});

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    var corsOrigins = (builder.Configuration["CORS_ORIGINS"] ?? "http://localhost:3000").Split(",");
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("RuleService starting... Environment: {Env}", app.Environment.EnvironmentName);

app.UseCors();
app.UseRateLimiter();
app.MapOpenApi();
app.MapScalarApiReference();

// ── Health ──
app.MapGet("/health", () => new { status = "healthy", service = "RuleService" })
   .WithName("Health")
   .Produces<object>();

// ── Rules CRUD ──
app.MapGet("/rules", (RuleStore store) =>
    Results.Ok(store.GetAll()))
   .WithName("GetAllRules");

app.MapGet("/rules/{id}", (string id, RuleStore store) =>
    store.GetById(id) is { } rule
        ? Results.Ok(rule)
        : Results.NotFound())
   .WithName("GetRuleById");

app.MapPost("/rules", async (HttpRequest request, RuleStore store) =>
{
    var json = await new StreamReader(request.Body).ReadToEndAsync();

    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new DateOnlyJsonConverter(), new NullableDateOnlyJsonConverter() }
    };

    options.TypeInfoResolver = new DefaultJsonTypeInfoResolver().WithAddedModifier(x =>
    {
        if (x.Type == typeof(Rule))
        {
            x.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(TimeWindowPromotionRule), "TimeWindowPromotion"),
                    new JsonDerivedType(typeof(RemoteAreaSurchargeRule), "RemoteAreaSurcharge"),
                    new JsonDerivedType(typeof(WeightTierRule), "WeightTier")
                }
            };
        }
    });

    var rule = JsonSerializer.Deserialize<Rule>(json, options);
    if (rule == null)
        return Results.BadRequest("Invalid rule data");

    rule.Id = Guid.NewGuid().ToString();
    store.Upsert(rule);
    return Results.Created($"/rules/{rule.Id}", rule);
})
   .WithName("CreateRule");

app.MapPut("/rules/{id}", (string id, Rule rule, RuleStore store) =>
{
    if (store.GetById(id) is null)
        return Results.NotFound();

    rule.Id = id;
    store.Upsert(rule);
    return Results.Ok(rule);
})
   .WithName("UpdateRule");

app.MapDelete("/rules/{id}", (string id, RuleStore store) =>
{
    if (store.GetById(id) is null)
        return Results.NotFound();

    store.Remove(id);
    return Results.NoContent();
})
   .WithName("DeleteRule");

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
