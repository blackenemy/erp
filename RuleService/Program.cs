using System.Collections.Concurrent;
using System.Text.Json;
using Scalar.AspNetCore;
using Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Add(AppJsonContext.Default));

builder.Services.AddSingleton<RuleStore>();

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

// ── Health ──
app.MapGet("/health", () => new { status = "healthy", service = "RuleService" })
   .WithName("Health");

// ── Rules CRUD ──
app.MapGet("/rules", (RuleStore store) =>
    Results.Ok(store.GetAll()))
   .WithName("GetAllRules");

app.MapGet("/rules/{id}", (string id, RuleStore store) =>
    store.GetById(id) is { } rule
        ? Results.Ok(rule)
        : Results.NotFound())
   .WithName("GetRuleById");

app.MapPost("/rules", (Rule rule, RuleStore store) =>
{
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

// ══════════════════════════════════════════════
//  RuleStore — ConcurrentDictionary + JSON file
// ══════════════════════════════════════════════
public sealed class RuleStore
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "rules.json");

    private readonly ConcurrentDictionary<string, Rule> _rules;
    private readonly Lock _fileLock = new();

    public RuleStore()
    {
        if (File.Exists(FilePath))
        {
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<Rule>>(json, AppJsonContext.Default.ListRule) ?? [];
            _rules = new(list.ToDictionary(r => r.Id));
        }
        else
        {
            _rules = new();
            Flush();
        }
    }

    public List<Rule> GetAll() => [.. _rules.Values];

    public Rule? GetById(string id) =>
        _rules.TryGetValue(id, out var rule) ? rule : null;

    public void Upsert(Rule rule)
    {
        _rules[rule.Id] = rule;
        Flush();
    }

    public void Remove(string id)
    {
        _rules.TryRemove(id, out _);
        Flush();
    }

    private void Flush()
    {
        lock (_fileLock)
        {
            var json = JsonSerializer.Serialize(
                [.. _rules.Values], AppJsonContext.Default.ListRule);
            File.WriteAllText(FilePath, json);
        }
    }
}
