using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.RateLimiting;
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
});

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

// ── Typed HttpClient → RuleService with Retry (3 attempts, exponential backoff) ──
builder.Services.AddHttpClient<RuleServiceClient>(client =>
{
    var url = builder.Configuration["RuleServiceUrl"] ?? "http://localhost:5002";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(5);
})
.ConfigureAdditionalHttpMessageHandlers((handler, services) =>
{
    var loggerFactory = services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("RuleServiceClient");
    handler.Add(new RetryHandler(maxRetries: 3, logger: logger));
});

builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton(Channel.CreateUnbounded<string>());
builder.Services.AddHostedService<BulkQuoteWorker>();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("PricingService starting... Environment: {Env}", app.Environment.EnvironmentName);
logger.LogInformation("RuleService URL: {Url}", app.Configuration["RuleServiceUrl"]);

app.UseCors();
app.UseRateLimiter();
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

// Required for WebApplicationFactory in integration tests
public partial class Program { }

// ══════════════════════════════════════════════
//  Typed HTTP client for RuleService
// ══════════════════════════════════════════════
public sealed class RuleServiceClient(HttpClient http)
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<List<Rule>> GetRulesAsync()
    {
        var stream = await http.GetStreamAsync("/rules");
        return await JsonSerializer.DeserializeAsync<List<Rule>>(stream, _options) ?? [];
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
            BasePrice = basePrice,
            Discount = discount,
            Surcharge = surcharge,
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
            catch (Exception e)
            {
                logger.LogError(e, "Job {JobId} failed", jobId);
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
    private readonly ConcurrentDictionary<string, Queue<QuoteRequest>> _pending;
    private readonly Lock _fileLock = new();
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JobStore()
    {
        _pending = new();

        if (File.Exists(FilePath))
        {
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<JobRecord>>(json, _options) ?? [];
            _jobs = new(list.ToDictionary(j => j.JobId));
        }
        else
        {
            _jobs = new();
            Flush();
        }
    }

    public void Add(JobRecord job)
    {
        _jobs[job.JobId] = job;
        Flush();
    }

    public JobRecord? GetById(string jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    public void SetPending(string jobId, List<QuoteRequest> items)
    {
        var queue = new Queue<QuoteRequest>(items);
        _pending[jobId] = queue;
    }

    public List<QuoteRequest> TakePending(string jobId)
    {
        var result = new List<QuoteRequest>();
        if (_pending.TryGetValue(jobId, out var queue))
        {
            while (queue.Count > 0)
                result.Add(queue.Dequeue());
            _pending.TryRemove(jobId, out _);
        }
        return result;
    }

    public void Flush()
    {
        lock (_fileLock)
        {
            var list = _jobs.Values.ToList();
            var json = JsonSerializer.Serialize(list, _options);
            File.WriteAllText(FilePath, json);
        }
    }
}

// ══════════════════════════════════════════════
//  Retry Handler — HTTP client retry with exponential backoff
// ══════════════════════════════════════════════
public sealed class RetryHandler : DelegatingHandler
{
    private readonly int _maxRetries;
    private readonly ILogger _logger;

    public RetryHandler(int maxRetries, ILogger logger)
    {
        _maxRetries = maxRetries;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < _maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(
                    "RuleService request failed (attempt {Attempt}/{Max}). Retrying after {Delay}ms",
                    attempt + 1, _maxRetries + 1, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
        // If we reach here, all retries failed
        throw new HttpRequestException("RuleService unavailable after all retry attempts");
    }
}
