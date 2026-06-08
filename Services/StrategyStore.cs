using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RSIMasterpro.Configuration;

namespace RSIMasterpro.Services;

// Holds the live strategy parameters. Initialized from appsettings, then overridden
// by strategy-settings.json (if present) so edits made from the dashboard survive restarts.
// StrategyService reads Current on every run, so changes take effect on the next run.
public class StrategyStore
{
    private readonly string _path;
    private readonly ILogger<StrategyStore> _logger;
    private readonly object _lock = new();
    private StrategySettings _current;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public StrategyStore(IOptions<AppSettings> opts, IHostEnvironment env, ILogger<StrategyStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(env.ContentRootPath, "strategy-settings.json");
        _current = LoadOrDefault(opts.Value.Strategy);
    }

    public StrategySettings Current
    {
        get { lock (_lock) return _current; }
    }

    public void Update(StrategySettings updated)
    {
        lock (_lock)
        {
            _current = updated;
            try
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(updated, JsonOpts));
                _logger.LogInformation("Strategy parameters updated and saved to {Path}.", _path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist strategy parameters to {Path}.", _path);
            }
        }
    }

    private StrategySettings LoadOrDefault(StrategySettings fallback)
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<StrategySettings>(json, JsonOpts);
                if (loaded != null)
                {
                    _logger.LogInformation("Loaded strategy parameters from {Path}.", _path);
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read {Path}; using appsettings defaults.", _path);
        }

        return fallback;
    }
}
