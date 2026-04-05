using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shared;
using static Shared.AppJsonContext;

namespace RuleService;

/// <summary>
/// In-memory rule store with file-based persistence
/// Provides thread-safe CRUD operations for pricing rules
/// </summary>
public sealed class RuleStore
{
    private static readonly string DataDir =
        Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string FilePath =
        Path.Combine(DataDir, "rules.json");
    private static readonly string SeedFilePath =
        Path.Combine(AppContext.BaseDirectory, "seed-rules.json");

    private readonly ConcurrentDictionary<string, Rule> _rules;
    private readonly Lock _fileLock = new();
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new DateOnlyJsonConverter(), new NullableDateOnlyJsonConverter() }
    };

    public RuleStore()
    {
        Directory.CreateDirectory(DataDir);

        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<Rule>>(json, _options) ?? [];
                _rules = new(list.ToDictionary(r => r.Id));
            }
            else if (File.Exists(SeedFilePath))
            {
                var json = File.ReadAllText(SeedFilePath);
                var list = JsonSerializer.Deserialize<List<Rule>>(json, _options) ?? [];
                _rules = new(list.ToDictionary(r => r.Id));
                Flush();
            }
            else
            {
                _rules = new();
                Flush();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading rules: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            _rules = new();
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
            var list = _rules.Values.ToList();
            var json = JsonSerializer.Serialize(list, _options);
            File.WriteAllText(FilePath, json);
        }
    }
}

