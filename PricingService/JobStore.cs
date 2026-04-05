using System.Collections.Concurrent;
using System.Text.Json;
using Shared;

namespace PricingService;

/// <summary>
/// In-memory job store with file-based persistence
/// Manages bulk quote processing jobs
/// </summary>
public sealed class JobStore
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "jobs.json");

    private readonly ConcurrentDictionary<string, JobRecord> _jobs;
    private readonly ConcurrentDictionary<string, Queue<QuoteRequest>> _pending;
    private readonly Lock _fileLock = new();

    public JobStore()
    {
        _pending = new();

        if (File.Exists(FilePath))
        {
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<JobRecord>>(json, AppJsonContext.Options) ?? [];
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
            var json = JsonSerializer.Serialize(
                _jobs.Values.ToList(), AppJsonContext.Options);
            File.WriteAllText(FilePath, json);
        }
    }
}
