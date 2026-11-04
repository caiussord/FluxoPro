using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluxoPro.Api.Infrastructure;
using FluxoPro.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;

namespace FluxoPro.Tests;

public class FluxoProTests
{
    private static WebApplicationFactory<Program> Factory(string path) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FLUXOPRO_DB_PATH"] = path,
            ["FLUXOPRO_INTERNAL_KEY"] = "internal-test-secret",
            ["FLUXOPRO_ADMIN_PASSWORD"] = "admin-test-password-123456",
            ["FLUXOPRO_N8N_WEBHOOK_URL"] = "",
            ["FLUXOPRO_N8N_WEBHOOK_KEY"] = ""
        })));

    private static CreateQuote Sample(string name = "Ana Teste") => new(name, "ana@example.com", null, "Landing page", "Página fictícia para validar o fluxo", 2, true);
    private static string TempDb() => Path.Combine(Path.GetTempPath(), "fluxopro-tests", Guid.NewGuid().ToString("N") + ".db");

    private static async Task<Guid> Create(HttpClient client, string key, CreateQuote? quote = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/solicitacoes");
        request.Headers.Add("Idempotency-Key", key);
        request.Content = JsonContent.Create(quote ?? Sample());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static HttpRequestMessage Internal(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Internal-Key", "internal-test-secret");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    [Fact]
    public async Task PublicValidationAndIdempotencyProtectTheSubmission()
    {
        using var factory = Factory(TempDb()); using var client = factory.CreateClient();
        using var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/solicitacoes");
        invalid.Headers.Add("Idempotency-Key", "invalid-001");
        invalid.Content = JsonContent.Create(Sample() with { Email = "invalid", Quantidade = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalid)).StatusCode);
        var id = await Create(client, "same-payload-001");
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/solicitacoes");
        replay.Headers.Add("Idempotency-Key", "same-payload-001"); replay.Content = JsonContent.Create(Sample());
        using var repeat = await client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        using var parsed = JsonDocument.Parse(await repeat.Content.ReadAsStringAsync());
        Assert.Equal(id, parsed.RootElement.GetProperty("id").GetGuid());
        using var changed = new HttpRequestMessage(HttpMethod.Post, "/api/solicitacoes");
        changed.Headers.Add("Idempotency-Key", "same-payload-001"); changed.Content = JsonContent.Create(Sample("Outra Pessoa"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(changed)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/solicitacoes/{id}")).StatusCode);
    }

    [Fact]
    public async Task StatusTransitionsRequirePdfAndLeaveAnAuditTrail()
    {
        using var factory = Factory(TempDb()); using var client = factory.CreateClient();
        var id = await Create(client, "status-001");
        using (var claim = Internal(HttpMethod.Post, $"/api/solicitacoes/{id}/iniciar")) Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(claim)).StatusCode);
        using (var duplicate = Internal(HttpMethod.Post, $"/api/solicitacoes/{id}/iniciar")) Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(duplicate)).StatusCode);
        using (var premature = Internal(HttpMethod.Patch, $"/api/solicitacoes/{id}/status", new StatusInput(QuoteStatus.Completed, null))) Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(premature)).StatusCode);
        using (var pdf = Internal(HttpMethod.Post, $"/api/solicitacoes/{id}/gerar-pdf"))
        using (var response = await client.SendAsync(pdf)) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType); }
        using (var complete = Internal(HttpMethod.Patch, $"/api/solicitacoes/{id}/status", new StatusInput(QuoteStatus.Completed, null))) Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(complete)).StatusCode);
        using (var reverse = Internal(HttpMethod.Patch, $"/api/solicitacoes/{id}/status", new StatusInput(QuoteStatus.Failed, null))) Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(reverse)).StatusCode);
        using var login = await client.PostAsJsonAsync("/api/admin/login", new AdminLogin("admin-test-password-123456"));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/admin/quotes/{id}");
        Assert.Equal(4, detail.GetProperty("history").GetArrayLength());
        Assert.Equal(QuoteStatus.Completed, detail.GetProperty("quote").GetProperty("status").GetString());
    }

    [Fact]
    public async Task AdminCanFilterAndEditFuturePricesAndTemplates()
    {
        using var factory = Factory(TempDb()); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/quotes")).StatusCode);
        using var login = await client.PostAsJsonAsync("/api/admin/login", new AdminLogin("admin-test-password-123456"));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var updatePrice = new HttpRequestMessage(HttpMethod.Put, "/api/admin/services/Landing%20page");
        updatePrice.Headers.Add("X-FluxoPro-Admin", "1"); updatePrice.Content = JsonContent.Create(new PriceUpdate(650m, true));
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(updatePrice)).StatusCode);
        var id = await Create(client, "priced-001");
        using (var get = Internal(HttpMethod.Get, $"/api/solicitacoes/{id}"))
        using (var response = await client.SendAsync(get))
        { var quote = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1300m, quote.GetProperty("valorEstimado").GetDecimal()); }
        using var updateTemplate = new HttpRequestMessage(HttpMethod.Put, "/api/admin/templates/cliente");
        updateTemplate.Headers.Add("X-FluxoPro-Admin", "1"); updateTemplate.Content = JsonContent.Create(new TemplateUpdate("Olá {{nome}} - {{id}}", "Seu serviço {{servico}} foi recebido."));
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(updateTemplate)).StatusCode);
        using (var messages = Internal(HttpMethod.Get, $"/api/solicitacoes/{id}/mensagens"))
        using (var response = await client.SendAsync(messages))
        { var text = await response.Content.ReadAsStringAsync(); Assert.Contains("Ana Teste", text); Assert.Contains("Landing page", text); }
        var page = await client.GetFromJsonAsync<JsonElement>("/api/admin/quotes?search=Ana&status=Recebida");
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        using var csv = await client.GetAsync("/api/admin/export.csv"); Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
    }

    [Fact]
    public async Task OnlyOneInstanceCanLeaseTheSamePendingQuote()
    {
        var path = TempDb(); var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FLUXOPRO_DB_PATH"] = path }).Build();
        var first = new QuoteRepository(config); var second = new QuoteRepository(config);
        await first.Initialize();
        var created = await first.Create(Sample(), "concurrent-001", "hash", 500m);
        Assert.NotNull(created.quote);
        var results = await Task.WhenAll(first.LeaseDue("worker-a", 10), second.LeaseDue("worker-b", 10));
        Assert.Single(results.SelectMany(ids => ids));
        Assert.Equal(created.quote.Id, results.SelectMany(ids => ids).Single());
    }

    [Fact]
    public async Task ExistingDatabaseIsMigratedWithoutLosingQuotes()
    {
        var path = TempDb(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var db = new SqliteConnection($"Data Source={path}"))
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TABLE quotes (id TEXT PRIMARY KEY, name TEXT NOT NULL, email TEXT NOT NULL, phone TEXT, service TEXT NOT NULL, description TEXT NOT NULL, quantity INTEGER NOT NULL, value TEXT NOT NULL, status TEXT NOT NULL, created TEXT NOT NULL, updated TEXT NOT NULL, idem_key TEXT NOT NULL UNIQUE, payload_hash TEXT NOT NULL, pdf_generated INTEGER NOT NULL DEFAULT 0, attempts INTEGER NOT NULL DEFAULT 0, next_dispatch TEXT, last_error TEXT)";
            cmd.ExecuteNonQuery();
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FLUXOPRO_DB_PATH"] = path }).Build();
        var repository = new QuoteRepository(config);
        await repository.Initialize();
        var created = await repository.Create(Sample(), "migrated-001", "hash", 500m);
        Assert.NotNull(created.quote);
        Assert.Single(await repository.History(created.quote.Id));
    }
}
