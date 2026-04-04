using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Scalar.AspNetCore;
using Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Add(AppJsonContext.Default));

// Typed HttpClient → RuleService
builder.Services.AddHttpClient<RuleServiceClient>(client =>
{
    var url = builder.Configuration["RuleServiceUrl"] ?? "http://localhost:5002";
    client.BaseAddress = new Uri(url);
});

builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton(Channel.CreateUnbounded<string>());
builder.Services.AddHostedService<BulkQuoteWorker>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

// ── Health ──
app.MapGet("/health", () => new { status = "healthy", service = "PricingService" })
   .WithTags("Health");

// ── Quotes ──
var quotes = app.MapGroup("/quotes").WithTags("Quotes");

quotes.MapPost("/price", async (QuoteRequest req, RuleServiceClient ruleClient) =>
{
    var rules = await ruleClient.GetRulesAsync();
    return PricingEngine.Calculate(req, rules);
});

quotes.MapPost("/bulk", async (
    BulkQuoteRequest bulk,
    JobStore jobs,
    Channel<string> channel) =>
{
    var job = new JobRecord();
    jobs.Add(job);

    // Seed items so the worker can find them
    jobs.SetPending(job.JobId, bulk.Items);
    await channel.Writer.WriteAsync(job.JobId);

    return Results.Accepted($"/jobs/{job.JobId}",
        new { jobId = job.JobId, status = job.Status });
});

// ── Jobs ──
app.MapGet("/jobs/{jobId}", (string jobId, JobStore jobs) =>
    jobs.GetById(jobId) is { } job
        ? Results.Ok(job)
        : Results.NotFound(new { error = "Job not found" }))
   .WithTags("Jobs");

app.Run();

// ══════════════════════════════════════════════
//  Typed HTTP client for RuleService
// ══════════════════════════════════════════════
public sealed class RuleServiceClient(HttpClient http)
{
    public async Task<List<Rule>> GetRulesAsync()
    {
        var stream = await http.GetStreamAsync("/rules");
        return await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.ListRule) ?? [];
    }
}

// ══════════════════════════════════════════════
//  Pricing Engine — pure static logic
// ══════════════════════════════════════════════
public static class PricingEngine
{
    private const decimal BaseFlatRate = 50m;

    public static QuoteResult Calculate(QuoteRequest req, List<Rule> rules)
    {
        var basePrice = BaseFlatRate;
        var discount = 0m;
        var surcharge = 0m;
        var applied = new List<string>();

        foreach (var rule in rules.Where(r => r.Enabled))
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
            BasePrice  = basePrice,
            Discount   = discount,
            Surcharge  = surcharge,
            FinalPrice = basePrice - discount + surcharge,
            AppliedRules = applied
        };
    }
}

// ══════════════════════════════════════════════
//  Background worker — processes bulk jobs via Channel
// ══════════════════════════════════════════════
public sealed class BulkQuoteWorker(
    Channel<string> channel,
    JobStore jobs,
    RuleServiceClient ruleClient,
    ILogger<BulkQuoteWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var jobId in channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                var job = jobs.GetById(jobId);
                if (job is null) continue;

                job.Status = "processing";
                jobs.Flush();

                var rules = await ruleClient.GetRulesAsync();
                var pending = jobs.TakePending(jobId);

                foreach (var item in pending)
                    job.Results.Add(PricingEngine.Calculate(item, rules));

                job.Status = "completed";
                job.CompletedAt = DateTime.UtcNow;
                jobs.Flush();

                logger.LogInformation("Job {JobId} completed — {Count} quotes", jobId, job.Results.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {JobId} failed", jobId);
                if (jobs.GetById(jobId) is { } failedJob)
                {
                    failedJob.Status = "failed";
                    jobs.Flush();
                }
            }
        }
    }
}

// ══════════════════════════════════════════════
//  JobStore — ConcurrentDictionary + JSON file
// ══════════════════════════════════════════════
public sealed class JobStore
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "jobs.json");

    private readonly ConcurrentDictionary<string, JobRecord> _jobs;
    private readonly ConcurrentDictionary<string, List<QuoteRequest>> _pending = new();
    private readonly Lock _fileLock = new();

    public JobStore()
    {
        if (File.Exists(FilePath))
        {
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListJobRecord) ?? [];
            _jobs = new(list.ToDictionary(j => j.JobId));
        }
        else
        {
            _jobs = new();
        }
    }

    public JobRecord? GetById(string id) =>
        _jobs.TryGetValue(id, out var job) ? job : null;

    public void Add(JobRecord job)
    {
        _jobs[job.JobId] = job;
        Flush();
    }

    public void SetPending(string jobId, List<QuoteRequest> items) =>
        _pending[jobId] = items;

    public List<QuoteRequest> TakePending(string jobId) =>
        _pending.TryRemove(jobId, out var items) ? items : [];

    public void Flush()
    {
        lock (_fileLock)
        {
            var json = JsonSerializer.Serialize(
                [.. _jobs.Values], AppJsonContext.Default.ListJobRecord);
            File.WriteAllText(FilePath, json);
        }
    }
}
