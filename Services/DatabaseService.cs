using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using RSIMasterpro.Configuration;
using RSIMasterpro.Models;

namespace RSIMasterpro.Services;

// PostgreSQL schema is created/updated automatically by EnsureSchemaAsync at startup.
// Required columns: symbol (VARCHAR), active (BOOLEAN). Extra user-defined columns are preserved.
public class DatabaseService
{
    private readonly ILogger<DatabaseService> _logger;
    private readonly DatabaseSettings _settings;

    public DatabaseService(ILogger<DatabaseService> logger, IOptions<AppSettings> opts)
    {
        _logger = logger;
        _settings = opts.Value.Database;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);

        var table = QuoteIdent(_settings.CryptosTable);
        var symCol = QuoteIdent(_settings.SymbolColumn);
        var actCol = QuoteIdent(_settings.ActiveColumn);
        var idxName = QuoteIdent($"{_settings.CryptosTable}_{_settings.SymbolColumn}_uq");

        var statements = new[]
        {
            $@"CREATE TABLE IF NOT EXISTS {table} (
                id           SERIAL PRIMARY KEY,
                {symCol}     VARCHAR(32) NOT NULL,
                {actCol}     BOOLEAN     NOT NULL DEFAULT TRUE,
                created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )",
            $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS {symCol} VARCHAR(32) NOT NULL DEFAULT ''",
            $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS {actCol} BOOLEAN NOT NULL DEFAULT TRUE",
            $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()",
            $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()",
            $"CREATE UNIQUE INDEX IF NOT EXISTS {idxName} ON {table} ({symCol})",

            // Sanal işlemler tablosu
            @"CREATE TABLE IF NOT EXISTS trades (
                id            SERIAL PRIMARY KEY,
                symbol        VARCHAR(32)      NOT NULL,
                side          VARCHAR(8)       NOT NULL,
                entry_price   DOUBLE PRECISION NOT NULL,
                quantity      DOUBLE PRECISION NOT NULL,
                stop_loss     DOUBLE PRECISION NOT NULL,
                take_profit   DOUBLE PRECISION NOT NULL,
                status        VARCHAR(16)      NOT NULL DEFAULT 'OPEN',
                entry_time    TIMESTAMPTZ      NOT NULL DEFAULT NOW(),
                exit_price    DOUBLE PRECISION,
                exit_time     TIMESTAMPTZ,
                exit_reason   VARCHAR(16),
                pnl           DOUBLE PRECISION
            )",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS symbol VARCHAR(32) NOT NULL DEFAULT ''",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS side VARCHAR(8) NOT NULL DEFAULT 'LONG'",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS entry_price DOUBLE PRECISION NOT NULL DEFAULT 0",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS quantity DOUBLE PRECISION NOT NULL DEFAULT 0",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS stop_loss DOUBLE PRECISION NOT NULL DEFAULT 0",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS take_profit DOUBLE PRECISION NOT NULL DEFAULT 0",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS status VARCHAR(16) NOT NULL DEFAULT 'OPEN'",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS entry_time TIMESTAMPTZ NOT NULL DEFAULT NOW()",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS exit_price DOUBLE PRECISION",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS exit_time TIMESTAMPTZ",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS exit_reason VARCHAR(16)",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS pnl DOUBLE PRECISION",
            "ALTER TABLE trades ADD COLUMN IF NOT EXISTS tracking_mode VARCHAR(16) NOT NULL DEFAULT 'STANDARD'",
            "CREATE INDEX IF NOT EXISTS trades_status_idx ON trades (status)",
            "CREATE INDEX IF NOT EXISTS trades_exit_time_idx ON trades (exit_time DESC)",
            "CREATE INDEX IF NOT EXISTS trades_mode_idx ON trades (tracking_mode)"
        };

        foreach (var sql in statements)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation("Database schema verified/updated for table {Table}.", _settings.CryptosTable);
    }

    public async Task UpsertSymbolsAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);

        var table = QuoteIdent(_settings.CryptosTable);
        var symCol = QuoteIdent(_settings.SymbolColumn);
        var actCol = QuoteIdent(_settings.ActiveColumn);

        var sql = $"INSERT INTO {table} ({symCol}, {actCol}) VALUES (@sym, TRUE) " +
                  $"ON CONFLICT ({symCol}) DO UPDATE SET {actCol} = TRUE, updated_at = NOW()";

        int affected = 0;
        foreach (var raw in symbols)
        {
            var sym = (raw ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(sym)) continue;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("sym", sym);
            await cmd.ExecuteNonQueryAsync(ct);
            affected++;
            _logger.LogInformation("Upserted symbol: {Symbol}", sym);
        }
        _logger.LogInformation("Upsert complete: {Count} symbol(s).", affected);
    }

    private static string QuoteIdent(string ident)
    {
        if (string.IsNullOrWhiteSpace(ident))
            throw new InvalidOperationException("Identifier (table/column name) cannot be empty.");
        return "\"" + ident.Replace("\"", "\"\"") + "\"";
    }

    public async Task<List<CryptoSymbol>> GetActiveCryptosAsync(CancellationToken ct)
    {
        var list = new List<CryptoSymbol>();
        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT {QuoteIdent(_settings.SymbolColumn)}, {QuoteIdent(_settings.ActiveColumn)}, created_at " +
                  $"FROM {QuoteIdent(_settings.CryptosTable)} " +
                  $"WHERE {QuoteIdent(_settings.ActiveColumn)} = TRUE " +
                  $"ORDER BY {QuoteIdent(_settings.SymbolColumn)}";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            if (reader.IsDBNull(0)) continue;
            list.Add(new CryptoSymbol
            {
                Symbol = reader.GetString(0).Trim().ToUpperInvariant(),
                Active = ReadAsBool(reader, 1),
                CreatedAt = reader.GetDateTime(2)
            });
        }

        _logger.LogInformation("Loaded {Count} active crypto symbols from DB.", list.Count);
        return list;
    }

    public async Task<List<CryptoSymbol>> GetAllCryptosAsync(CancellationToken ct)
    {
        var list = new List<CryptoSymbol>();
        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT {QuoteIdent(_settings.SymbolColumn)}, {QuoteIdent(_settings.ActiveColumn)}, created_at " +
                  $"FROM {QuoteIdent(_settings.CryptosTable)} " +
                  $"ORDER BY {QuoteIdent(_settings.SymbolColumn)}";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            if (reader.IsDBNull(0)) continue;
            list.Add(new CryptoSymbol
            {
                Symbol = reader.GetString(0).Trim().ToUpperInvariant(),
                Active = ReadAsBool(reader, 1),
                CreatedAt = reader.GetDateTime(2)
            });
        }
        return list;
    }

    public async Task<int> DeleteSymbolAsync(string symbol, CancellationToken ct)
    {
        var sym = (symbol ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(sym)) return 0;

        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);

        var sql = $"DELETE FROM {QuoteIdent(_settings.CryptosTable)} " +
                  $"WHERE {QuoteIdent(_settings.SymbolColumn)} = @sym";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("sym", sym);
        var deleted = await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Deleted {Count} row(s) for symbol {Symbol}.", deleted, sym);
        return deleted;
    }

    public async Task<int> InsertTradeAsync(Trade trade, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);

        var sql = @"INSERT INTO trades
            (symbol, side, entry_price, quantity, stop_loss, take_profit, tracking_mode, status, entry_time)
            VALUES (@sym, @side, @entry, @qty, @sl, @tp, @mode, 'OPEN', NOW())
            RETURNING id";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("sym", trade.Symbol);
        cmd.Parameters.AddWithValue("side", trade.Side);
        cmd.Parameters.AddWithValue("entry", trade.EntryPrice);
        cmd.Parameters.AddWithValue("qty", trade.Quantity);
        cmd.Parameters.AddWithValue("sl", trade.StopLoss);
        cmd.Parameters.AddWithValue("tp", trade.TakeProfit);
        cmd.Parameters.AddWithValue("mode", trade.TrackingMode);

        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        _logger.LogInformation("Trade opened #{Id} {Symbol} {Side} ({Mode}) @ {Entry}",
            id, trade.Symbol, trade.Side, trade.TrackingMode, trade.EntryPrice);
        return id;
    }

    public async Task<List<Trade>> GetOpenTradesAsync(CancellationToken ct)
    {
        var list = new List<Trade>();
        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);

        var sql = @"SELECT id, symbol, side, entry_price, quantity, stop_loss, take_profit,
                           tracking_mode, status, entry_time, exit_price, exit_time, exit_reason, pnl
                    FROM trades WHERE status = 'OPEN' ORDER BY entry_time DESC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapTrade(reader));
        return list;
    }

    public async Task<List<Trade>> GetLastClosedTradesAsync(int limit, CancellationToken ct)
    {
        var list = new List<Trade>();
        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);

        var sql = @"SELECT id, symbol, side, entry_price, quantity, stop_loss, take_profit,
                           tracking_mode, status, entry_time, exit_price, exit_time, exit_reason, pnl
                    FROM trades WHERE status = 'CLOSED'
                    ORDER BY exit_time DESC LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapTrade(reader));
        return list;
    }

    public async Task CloseTradeAsync(int id, double exitPrice, string reason, double pnl, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);

        var sql = @"UPDATE trades
                    SET status='CLOSED', exit_price=@exit, exit_time=NOW(), exit_reason=@reason, pnl=@pnl
                    WHERE id=@id AND status='OPEN'";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("exit", exitPrice);
        cmd.Parameters.AddWithValue("reason", reason);
        cmd.Parameters.AddWithValue("pnl", pnl);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> DeleteTradesByModeAsync(string mode, CancellationToken ct)
    {
        // Sadece bilinen modlara izin ver.
        if (mode != "STANDARD" && mode != "NO_SL")
            throw new ArgumentException($"Unsupported tracking mode: {mode}", nameof(mode));

        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM trades WHERE tracking_mode = @mode", conn);
        cmd.Parameters.AddWithValue("mode", mode);
        var deleted = await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Deleted {Count} trade(s) for mode {Mode}.", deleted, mode);
        return deleted;
    }

    public async Task<Dictionary<string, TradeStats>> GetStatsAsync(CancellationToken ct)
    {
        var dict = new Dictionary<string, TradeStats>
        {
            ["STANDARD"] = new TradeStats(),
            ["NO_SL"] = new TradeStats()
        };

        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);

        var sql = @"SELECT tracking_mode,
            COUNT(*) FILTER (WHERE status = 'OPEN'),
            COUNT(*) FILTER (WHERE status = 'CLOSED'),
            COUNT(*) FILTER (WHERE status = 'CLOSED' AND pnl > 0),
            COUNT(*) FILTER (WHERE status = 'CLOSED' AND pnl <= 0),
            COALESCE(SUM(pnl) FILTER (WHERE status = 'CLOSED'), 0)
            FROM trades GROUP BY tracking_mode";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var mode = reader.GetString(0);
            dict[mode] = new TradeStats
            {
                OpenCount = (int)reader.GetInt64(1),
                ClosedCount = (int)reader.GetInt64(2),
                PositiveCount = (int)reader.GetInt64(3),
                NegativeCount = (int)reader.GetInt64(4),
                TotalPnl = reader.GetDouble(5)
            };
        }
        return dict;
    }

    private static Trade MapTrade(NpgsqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Symbol = r.GetString(1),
        Side = r.GetString(2),
        EntryPrice = r.GetDouble(3),
        Quantity = r.GetDouble(4),
        StopLoss = r.GetDouble(5),
        TakeProfit = r.GetDouble(6),
        TrackingMode = r.GetString(7),
        Status = r.GetString(8),
        EntryTime = r.GetDateTime(9),
        ExitPrice = r.IsDBNull(10) ? null : r.GetDouble(10),
        ExitTime = r.IsDBNull(11) ? null : r.GetDateTime(11),
        ExitReason = r.IsDBNull(12) ? null : r.GetString(12),
        Pnl = r.IsDBNull(13) ? null : r.GetDouble(13)
    };

    private static bool ReadAsBool(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return false;
        var value = reader.GetValue(ordinal);
        return value switch
        {
            bool b => b,
            short s => s != 0,
            int i => i != 0,
            long l => l != 0,
            decimal d => d != 0,
            string str => bool.TryParse(str, out var bv) ? bv : str != "0" && !string.IsNullOrEmpty(str),
            _ => Convert.ToInt64(value) != 0
        };
    }
}
