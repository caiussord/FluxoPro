using System.Globalization;
using Microsoft.Data.Sqlite;

sealed class Database(IConfiguration config)
{
    private SqliteConnection Open()
    {
        var path = config["FLUXOPRO_DB_PATH"] ?? Path.Combine(AppContext.BaseDirectory, "data", "fluxopro.db");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var db = new SqliteConnection($"Data Source={path}"); db.Open(); return db;
    }
    public async Task Init()
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS quotes (id TEXT PRIMARY KEY, name TEXT NOT NULL, email TEXT NOT NULL, phone TEXT, service TEXT NOT NULL, description TEXT NOT NULL, quantity INTEGER NOT NULL, value TEXT NOT NULL, status TEXT NOT NULL, created TEXT NOT NULL, updated TEXT NOT NULL, idem_key TEXT NOT NULL UNIQUE, payload_hash TEXT NOT NULL, pdf_generated INTEGER NOT NULL DEFAULT 0, attempts INTEGER NOT NULL DEFAULT 0, next_dispatch TEXT, last_error TEXT)";
        await cmd.ExecuteNonQueryAsync();
    }
    public async Task<(Quote? quote, bool conflict, bool existing)> Create(CreateQuote q, string key, string hash, decimal unit)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        using var find = db.CreateCommand(); find.Transaction = tx; find.CommandText = "SELECT id,payload_hash FROM quotes WHERE idem_key=$key"; find.Parameters.AddWithValue("$key", key);
        using (var reader = await find.ExecuteReaderAsync()) if (await reader.ReadAsync())
        {
            var id = Guid.Parse(reader.GetString(0)); var conflict = reader.GetString(1) != hash; reader.Close(); tx.Commit();
            return (conflict ? null : await Get(id), conflict, true);
        }
        var newId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow.ToString("O");
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO quotes (id,name,email,phone,service,description,quantity,value,status,created,updated,idem_key,payload_hash,next_dispatch) VALUES ($id,$name,$email,$phone,$service,$description,$quantity,$value,'Recebida',$now,$now,$key,$hash,$now)";
        cmd.Parameters.AddWithValue("$id", newId.ToString()); cmd.Parameters.AddWithValue("$name", q.Nome); cmd.Parameters.AddWithValue("$email", q.Email);
        cmd.Parameters.AddWithValue("$phone", (object?)q.Telefone ?? DBNull.Value); cmd.Parameters.AddWithValue("$service", q.Servico); cmd.Parameters.AddWithValue("$description", q.Descricao);
        cmd.Parameters.AddWithValue("$quantity", q.Quantidade); cmd.Parameters.AddWithValue("$value", (unit * q.Quantidade).ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$now", now); cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$hash", hash);
        await cmd.ExecuteNonQueryAsync(); tx.Commit(); return (await Get(newId), false, false);
    }
    public async Task<Quote?> Get(Guid id)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,name,email,phone,service,description,quantity,value,status,created,updated,pdf_generated,last_error FROM quotes WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id.ToString());
        using var r = await cmd.ExecuteReaderAsync(); return await r.ReadAsync() ? Read(r) : null;
    }
    public async Task<List<Quote>> List()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,name,email,phone,service,description,quantity,value,status,created,updated,pdf_generated,last_error FROM quotes ORDER BY created DESC LIMIT 100";
        using var r = await cmd.ExecuteReaderAsync(); var list = new List<Quote>(); while (await r.ReadAsync()) list.Add(Read(r)); return list;
    }
    private static Quote Read(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6), decimal.Parse(r.GetString(7), CultureInfo.InvariantCulture), r.GetString(8), DateTimeOffset.Parse(r.GetString(9)), DateTimeOffset.Parse(r.GetString(10)), r.GetInt32(11) == 1, r.IsDBNull(12) ? null : r.GetString(12));
    public async Task MarkPdf(Guid id) => await Update("UPDATE quotes SET pdf_generated=1,updated=$now WHERE id=$id", id);
    public async Task<bool> Claim(Guid id) => await Update("UPDATE quotes SET status='Em processamento',next_dispatch=NULL,updated=$now WHERE id=$id AND status='Recebida'", id) > 0;
    private async Task<int> Update(string sql, Guid id) { using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.Parameters.AddWithValue("$id", id.ToString()); cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O")); return await cmd.ExecuteNonQueryAsync(); }
    public async Task<bool> Status(Guid id, string status, string? error)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE quotes SET status=$status,last_error=$error,updated=$now,next_dispatch=NULL WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id.ToString()); cmd.Parameters.AddWithValue("$status", status); cmd.Parameters.AddWithValue("$error", (object?)error?[..Math.Min(error.Length, 300)] ?? DBNull.Value); cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O")); return await cmd.ExecuteNonQueryAsync() > 0;
    }
    public async Task<List<Guid>> Due()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id FROM quotes WHERE status='Recebida' AND next_dispatch <= $now AND attempts < 5 ORDER BY created LIMIT 20"; cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        using var r = await cmd.ExecuteReaderAsync(); var ids = new List<Guid>(); while (await r.ReadAsync()) ids.Add(Guid.Parse(r.GetString(0))); return ids;
    }
    public async Task DispatchResult(Guid id, bool success, string? error)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = success ? "UPDATE quotes SET attempts=attempts+1,next_dispatch=NULL,updated=$now WHERE id=$id AND status='Recebida'" : "UPDATE quotes SET attempts=attempts+1,next_dispatch=$next,status=CASE WHEN attempts+1>=5 THEN 'Falha' ELSE status END,last_error=$error,updated=$now WHERE id=$id AND status='Recebida'";
        var now = DateTimeOffset.UtcNow; cmd.Parameters.AddWithValue("$id", id.ToString()); cmd.Parameters.AddWithValue("$now", now.ToString("O")); cmd.Parameters.AddWithValue("$next", now.AddMinutes(2).ToString("O")); cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value); await cmd.ExecuteNonQueryAsync();
    }
    public async Task ExpireStale()
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE quotes SET status='Falha',last_error='Tempo de processamento excedido',updated=$now WHERE status='Em processamento' AND updated < $cutoff";
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddMinutes(-30).ToString("O")); await cmd.ExecuteNonQueryAsync();
    }
}

sealed class Dispatcher(Database db, IHttpClientFactory clients, IConfiguration config, ILogger<Dispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await db.ExpireStale();
            if (Uri.TryCreate(config["FLUXOPRO_N8N_WEBHOOK_URL"], UriKind.Absolute, out var url) && !string.IsNullOrWhiteSpace(config["FLUXOPRO_N8N_WEBHOOK_KEY"]))
                foreach (var id in await db.Due())
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Post, url);
                        req.Headers.Add("X-Webhook-Key", config["FLUXOPRO_N8N_WEBHOOK_KEY"]);
                        req.Content = System.Net.Http.Json.JsonContent.Create(new { id, eventId = id });
                        using var response = await clients.CreateClient().SendAsync(req, stop);
                        await db.DispatchResult(id, response.IsSuccessStatusCode, response.IsSuccessStatusCode ? null : $"Webhook HTTP {(int)response.StatusCode}");
                    }
                    catch (Exception ex) { log.LogWarning("Webhook falhou para {Id}: {Type}", id, ex.GetType().Name); await db.DispatchResult(id, false, ex.GetType().Name); }
                }
            await Task.Delay(TimeSpan.FromSeconds(10), stop);
        }
    }
}
