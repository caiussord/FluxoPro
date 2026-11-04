using System.Globalization;
using FluxoPro.Api.Models;
using Microsoft.Data.Sqlite;

namespace FluxoPro.Api.Infrastructure;

public sealed class QuoteRepository
{
    private readonly string _path;
    private readonly IConfiguration _config;

    public QuoteRepository(IConfiguration config)
    {
        _config = config;
        _path = Path.GetFullPath(config["FLUXOPRO_DB_PATH"] ?? Path.Combine(AppContext.BaseDirectory, "data", "fluxopro.db"));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, DefaultTimeout = 10 }.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteCommand Command(SqliteConnection db, string sql, SqliteTransaction? tx = null, params (string name, object? value)[] args)
    {
        var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.Transaction = tx;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public async Task Initialize()
    {
        using var db = Open();
        using (var setup = Command(db, """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS quotes (
              id TEXT PRIMARY KEY, name TEXT NOT NULL, email TEXT NOT NULL, phone TEXT,
              service TEXT NOT NULL, description TEXT NOT NULL, quantity INTEGER NOT NULL,
              value TEXT NOT NULL, status TEXT NOT NULL, created TEXT NOT NULL, updated TEXT NOT NULL,
              idem_key TEXT NOT NULL UNIQUE, payload_hash TEXT NOT NULL,
              pdf_generated INTEGER NOT NULL DEFAULT 0, attempts INTEGER NOT NULL DEFAULT 0,
              next_dispatch TEXT, last_error TEXT, lease_owner TEXT, lease_until TEXT
            );
            CREATE TABLE IF NOT EXISTS quote_history (
              id INTEGER PRIMARY KEY AUTOINCREMENT, quote_id TEXT NOT NULL,
              at TEXT NOT NULL, from_status TEXT, to_status TEXT NOT NULL,
              source TEXT NOT NULL, detail TEXT,
              FOREIGN KEY(quote_id) REFERENCES quotes(id)
            );
            CREATE TABLE IF NOT EXISTS services (
              name TEXT PRIMARY KEY, price TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS email_templates (
              key TEXT PRIMARY KEY, subject TEXT NOT NULL, body TEXT NOT NULL
            );
            """)) await setup.ExecuteNonQueryAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pragma = Command(db, "PRAGMA table_info(quotes)"))
        using (var reader = await pragma.ExecuteReaderAsync())
            while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        foreach (var column in new[] { "lease_owner", "lease_until" })
            if (!columns.Contains(column))
                using (var alter = Command(db, $"ALTER TABLE quotes ADD COLUMN {column} TEXT")) await alter.ExecuteNonQueryAsync();
        using (var indexes = Command(db, "CREATE INDEX IF NOT EXISTS ix_quotes_dispatch ON quotes(status,next_dispatch,lease_until); CREATE INDEX IF NOT EXISTS ix_history_quote ON quote_history(quote_id,id)")) await indexes.ExecuteNonQueryAsync();

        using var tx = db.BeginTransaction();
        foreach (var (name, price) in new (string, decimal)[] {
            ("Site institucional", _config.GetValue("Prices:Site", 800m)),
            ("Landing page", _config.GetValue("Prices:LandingPage", 500m)),
            ("Automação simples", _config.GetValue("Prices:Automation", 350m)) })
            using (var seed = Command(db, "INSERT OR IGNORE INTO services(name,price) VALUES($n,$p)", tx, ("$n", name), ("$p", price.ToString(CultureInfo.InvariantCulture)))) await seed.ExecuteNonQueryAsync();
        foreach (var template in DefaultTemplates)
            using (var seed = Command(db, "INSERT OR IGNORE INTO email_templates(key,subject,body) VALUES($k,$s,$b)", tx, ("$k", template.Chave), ("$s", template.Assunto), ("$b", template.Corpo))) await seed.ExecuteNonQueryAsync();
        using (var history = Command(db, "INSERT INTO quote_history(quote_id,at,from_status,to_status,source,detail) SELECT q.id,q.created,NULL,q.status,'migração','Registro anterior ao histórico' FROM quotes q WHERE NOT EXISTS (SELECT 1 FROM quote_history h WHERE h.quote_id=q.id)", tx)) await history.ExecuteNonQueryAsync();
        tx.Commit();
    }

    public static readonly EmailTemplate[] DefaultTemplates = [
        new("cliente", "Orçamento demonstrativo FluxoPro - {{id}}", "Olá, {{nome}}. Recebemos sua solicitação demonstrativa. O PDF está em anexo. Este documento não tem validade comercial."),
        new("equipe", "Nova solicitação FluxoPro - {{id}}", "Nova solicitação demonstrativa de {{nome}}. Consulte a API para detalhes.")
    ];

    public async Task<Dictionary<string, decimal>> ActivePrices()
    {
        using var db = Open(); using var cmd = Command(db, "SELECT name,price FROM services WHERE active=1 ORDER BY name"); using var r = await cmd.ExecuteReaderAsync();
        var prices = new Dictionary<string, decimal>();
        while (await r.ReadAsync()) prices[r.GetString(0)] = decimal.Parse(r.GetString(1), CultureInfo.InvariantCulture);
        return prices;
    }

    public async Task<List<ServiceItem>> Services()
    {
        using var db = Open(); using var cmd = Command(db, "SELECT name,price,active FROM services ORDER BY name"); using var r = await cmd.ExecuteReaderAsync();
        var items = new List<ServiceItem>();
        while (await r.ReadAsync()) items.Add(new(r.GetString(0), decimal.Parse(r.GetString(1), CultureInfo.InvariantCulture), r.GetInt32(2) == 1));
        return items;
    }

    public async Task<bool> UpdateService(string name, PriceUpdate update)
    {
        using var db = Open(); using var cmd = Command(db, "UPDATE services SET price=$p,active=$a WHERE name=$n", null,
            ("$n", name), ("$p", update.Preco.ToString(CultureInfo.InvariantCulture)), ("$a", update.Ativo ? 1 : 0));
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<EmailTemplate>> Templates()
    {
        using var db = Open(); using var cmd = Command(db, "SELECT key,subject,body FROM email_templates ORDER BY key"); using var r = await cmd.ExecuteReaderAsync();
        var items = new List<EmailTemplate>(); while (await r.ReadAsync()) items.Add(new(r.GetString(0), r.GetString(1), r.GetString(2))); return items;
    }

    public async Task<bool> UpdateTemplate(string key, TemplateUpdate input)
    {
        using var db = Open(); using var cmd = Command(db, "UPDATE email_templates SET subject=$s,body=$b WHERE key=$k", null, ("$k", key), ("$s", input.Assunto), ("$b", input.Corpo));
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<(Quote? quote, bool conflict, bool existing)> Create(CreateQuote q, string key, string hash, decimal unit)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        using (var find = Command(db, "SELECT id,payload_hash FROM quotes WHERE idem_key=$k", tx, ("$k", key)))
        using (var r = await find.ExecuteReaderAsync())
            if (await r.ReadAsync())
            {
                var id = Guid.Parse(r.GetString(0)); var conflict = r.GetString(1) != hash;
                r.Close(); tx.Commit(); return (conflict ? null : await Get(id), conflict, true);
            }
        var newId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow.ToString("O");
        using (var insert = Command(db, """
            INSERT INTO quotes(id,name,email,phone,service,description,quantity,value,status,created,updated,idem_key,payload_hash,next_dispatch)
            VALUES($id,$n,$e,$phone,$s,$d,$q,$v,'Recebida',$now,$now,$key,$hash,$now)
            """, tx, ("$id", newId.ToString()), ("$n", q.Nome), ("$e", q.Email), ("$phone", q.Telefone),
            ("$s", q.Servico), ("$d", q.Descricao), ("$q", q.Quantidade),
            ("$v", (unit * q.Quantidade).ToString(CultureInfo.InvariantCulture)), ("$now", now), ("$key", key), ("$hash", hash)))
            await insert.ExecuteNonQueryAsync();
        await AddHistory(db, tx, newId, null, QuoteStatus.Received, "formulário", null);
        tx.Commit(); return (await Get(newId), false, false);
    }

    private const string QuoteColumns = "id,name,email,phone,service,description,quantity,value,status,created,updated,pdf_generated,last_error";
    private static Quote Read(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6), decimal.Parse(r.GetString(7), CultureInfo.InvariantCulture), r.GetString(8), DateTimeOffset.Parse(r.GetString(9)), DateTimeOffset.Parse(r.GetString(10)), r.GetInt32(11) == 1, r.IsDBNull(12) ? null : r.GetString(12));

    public async Task<Quote?> Get(Guid id)
    {
        using var db = Open(); using var cmd = Command(db, $"SELECT {QuoteColumns} FROM quotes WHERE id=$id", null, ("$id", id.ToString())); using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Read(r) : null;
    }

    public async Task<QuotePage> Search(string? search, string? status, int page, int pageSize)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 5000); search = search?.Trim();
        const string filter = "WHERE ($status IS NULL OR status=$status) AND ($search IS NULL OR name LIKE $pattern OR email LIKE $pattern OR service LIKE $pattern OR id LIKE $pattern)";
        using var db = Open();
        using var count = Command(db, "SELECT COUNT(*) FROM quotes " + filter, null, ("$status", string.IsNullOrWhiteSpace(status) ? null : status), ("$search", string.IsNullOrWhiteSpace(search) ? null : search), ("$pattern", "%" + search + "%"));
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        using var cmd = Command(db, $"SELECT {QuoteColumns} FROM quotes {filter} ORDER BY created DESC LIMIT $limit OFFSET $offset", null,
            ("$status", string.IsNullOrWhiteSpace(status) ? null : status), ("$search", string.IsNullOrWhiteSpace(search) ? null : search), ("$pattern", "%" + search + "%"),
            ("$limit", pageSize), ("$offset", (page - 1) * pageSize));
        using var r = await cmd.ExecuteReaderAsync(); var items = new List<Quote>(); while (await r.ReadAsync()) items.Add(Read(r));
        return new(items, total, page, pageSize);
    }

    public async Task<Dictionary<string, int>> Overview()
    {
        using var db = Open(); using var cmd = Command(db, "SELECT status,COUNT(*) FROM quotes GROUP BY status"); using var r = await cmd.ExecuteReaderAsync();
        var result = new Dictionary<string, int> { ["Total"] = 0, [QuoteStatus.Received] = 0, [QuoteStatus.Processing] = 0, [QuoteStatus.Completed] = 0, [QuoteStatus.Failed] = 0 };
        while (await r.ReadAsync()) { var count = r.GetInt32(1); result[r.GetString(0)] = count; result["Total"] += count; } return result;
    }

    public async Task<List<HistoryEntry>> History(Guid id)
    {
        using var db = Open(); using var cmd = Command(db, "SELECT at,from_status,to_status,source,detail FROM quote_history WHERE quote_id=$id ORDER BY id", null, ("$id", id.ToString()));
        using var r = await cmd.ExecuteReaderAsync(); var items = new List<HistoryEntry>();
        while (await r.ReadAsync()) items.Add(new(DateTimeOffset.Parse(r.GetString(0)), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        return items;
    }

    private static async Task AddHistory(SqliteConnection db, SqliteTransaction tx, Guid id, string? from, string to, string source, string? detail)
    {
        using var cmd = Command(db, "INSERT INTO quote_history(quote_id,at,from_status,to_status,source,detail) VALUES($id,$at,$from,$to,$source,$detail)", tx,
            ("$id", id.ToString()), ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$from", from), ("$to", to), ("$source", source), ("$detail", detail));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ChangeResult> ChangeStatus(Guid id, string next, string? error, string source)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        string current; bool pdf;
        using (var find = Command(db, "SELECT status,pdf_generated FROM quotes WHERE id=$id", tx, ("$id", id.ToString())))
        using (var r = await find.ExecuteReaderAsync())
        { if (!await r.ReadAsync()) return ChangeResult.NotFound; current = r.GetString(0); pdf = r.GetInt32(1) == 1; }
        if (!QuoteStatus.CanTransition(current, next, pdf)) return ChangeResult.Conflict;
        using (var update = Command(db, "UPDATE quotes SET status=$next,last_error=$error,updated=$now,next_dispatch=NULL,lease_owner=NULL,lease_until=NULL WHERE id=$id AND status=$current", tx,
            ("$id", id.ToString()), ("$current", current), ("$next", next), ("$error", error?[..Math.Min(error.Length, 300)]), ("$now", DateTimeOffset.UtcNow.ToString("O"))))
            if (await update.ExecuteNonQueryAsync() != 1) return ChangeResult.Conflict;
        await AddHistory(db, tx, id, current, next, source, error?[..Math.Min(error.Length, 300)]);
        tx.Commit(); return ChangeResult.Updated;
    }

    public Task<ChangeResult> Claim(Guid id) => ChangeStatus(id, QuoteStatus.Processing, null, "n8n");

    public async Task<bool> MarkPdf(Guid id)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        using var cmd = Command(db, "UPDATE quotes SET pdf_generated=1,updated=$now WHERE id=$id AND status='Em processamento' AND pdf_generated=0", tx,
            ("$id", id.ToString()), ("$now", DateTimeOffset.UtcNow.ToString("O")));
        var changed = await cmd.ExecuteNonQueryAsync() == 1;
        if (changed) await AddHistory(db, tx, id, QuoteStatus.Processing, QuoteStatus.Processing, "pdf", "Documento gerado");
        using var check = Command(db, "SELECT status,pdf_generated FROM quotes WHERE id=$id", tx, ("$id", id.ToString()));
        using var reader = await check.ExecuteReaderAsync();
        var valid = await reader.ReadAsync() && reader.GetString(0) == QuoteStatus.Processing && reader.GetInt32(1) == 1;
        reader.Close(); tx.Commit(); return valid;
    }

    public async Task<List<Guid>> LeaseDue(string owner, int limit)
    {
        var now = DateTimeOffset.UtcNow; var ids = new List<Guid>();
        using var db = Open(); using var tx = db.BeginTransaction();
        using (var select = Command(db, "SELECT id FROM quotes WHERE status='Recebida' AND next_dispatch <= $now AND (lease_until IS NULL OR lease_until < $now) AND attempts < 5 ORDER BY created LIMIT $limit", tx,
            ("$now", now.ToString("O")), ("$limit", limit)))
        using (var r = await select.ExecuteReaderAsync()) while (await r.ReadAsync()) ids.Add(Guid.Parse(r.GetString(0)));
        foreach (var id in ids)
            using (var update = Command(db, "UPDATE quotes SET lease_owner=$owner,lease_until=$until WHERE id=$id AND status='Recebida'", tx,
                ("$owner", owner), ("$until", now.AddMinutes(2).ToString("O")), ("$id", id.ToString()))) await update.ExecuteNonQueryAsync();
        tx.Commit(); return ids;
    }

    public async Task DispatchResult(Guid id, string owner, bool success, string? error)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        using var cmd = Command(db, success
            ? "UPDATE quotes SET attempts=attempts+1,next_dispatch=NULL,lease_owner=NULL,lease_until=NULL,updated=$now WHERE id=$id AND lease_owner=$owner AND status='Recebida'"
            : "UPDATE quotes SET attempts=attempts+1,next_dispatch=CASE WHEN attempts+1>=5 THEN NULL ELSE $next END,status=CASE WHEN attempts+1>=5 THEN 'Falha' ELSE status END,last_error=$error,lease_owner=NULL,lease_until=NULL,updated=$now WHERE id=$id AND lease_owner=$owner AND status='Recebida'", tx,
            ("$id", id.ToString()), ("$owner", owner), ("$now", DateTimeOffset.UtcNow.ToString("O")), ("$next", DateTimeOffset.UtcNow.AddMinutes(2).ToString("O")), ("$error", error?[..Math.Min(error.Length, 300)]));
        var changed = await cmd.ExecuteNonQueryAsync() == 1;
        if (changed && !success)
        {
            using var status = Command(db, "SELECT status FROM quotes WHERE id=$id", tx, ("$id", id.ToString()));
            var current = (string?)await status.ExecuteScalarAsync();
            await AddHistory(db, tx, id, QuoteStatus.Received, current ?? QuoteStatus.Received, "dispatcher", error);
        }
        tx.Commit();
    }

    public async Task ExpireStale()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-30).ToString("O"); var ids = new List<Guid>();
        using var db = Open(); using var tx = db.BeginTransaction();
        using (var select = Command(db, "SELECT id FROM quotes WHERE status='Em processamento' AND updated < $cutoff", tx, ("$cutoff", cutoff)))
        using (var r = await select.ExecuteReaderAsync()) while (await r.ReadAsync()) ids.Add(Guid.Parse(r.GetString(0)));
        foreach (var id in ids)
        {
            using var update = Command(db, "UPDATE quotes SET status='Falha',last_error='Tempo de processamento excedido',updated=$now WHERE id=$id AND status='Em processamento' AND updated < $cutoff", tx,
                ("$id", id.ToString()), ("$now", DateTimeOffset.UtcNow.ToString("O")), ("$cutoff", cutoff));
            if (await update.ExecuteNonQueryAsync() == 1) await AddHistory(db, tx, id, QuoteStatus.Processing, QuoteStatus.Failed, "dispatcher", "Tempo de processamento excedido");
        }
        tx.Commit();
    }

    public async Task<ChangeResult> RetrySafe(Guid id)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        using var find = Command(db, "SELECT status,pdf_generated FROM quotes WHERE id=$id", tx, ("$id", id.ToString()));
        using var r = await find.ExecuteReaderAsync(); if (!await r.ReadAsync()) return ChangeResult.NotFound;
        var allowed = r.GetString(0) == QuoteStatus.Failed && r.GetInt32(1) == 0; r.Close(); if (!allowed) return ChangeResult.Conflict;
        using var cmd = Command(db, "UPDATE quotes SET status='Recebida',attempts=0,next_dispatch=$now,last_error=NULL,updated=$now,lease_owner=NULL,lease_until=NULL WHERE id=$id AND status='Falha' AND pdf_generated=0", tx,
            ("$id", id.ToString()), ("$now", DateTimeOffset.UtcNow.ToString("O")));
        if (await cmd.ExecuteNonQueryAsync() != 1) return ChangeResult.Conflict;
        await AddHistory(db, tx, id, QuoteStatus.Failed, QuoteStatus.Received, "admin", "Reenvio seguro antes de gerar PDF");
        tx.Commit(); return ChangeResult.Updated;
    }
}
