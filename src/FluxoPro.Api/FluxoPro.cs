using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

static class FluxoPro
{
    public static async Task Start(string[] args)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var b = WebApplication.CreateBuilder(args);
        b.Services.AddSingleton<Database>();
        b.Services.AddHttpClient();
        b.Services.AddHostedService<Dispatcher>();
        b.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("public", limiter => { limiter.PermitLimit = 10; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; }));
        var app = b.Build();
        app.UseDefaultFiles(); app.UseStaticFiles(); app.UseRateLimiter();
        var db = app.Services.GetRequiredService<Database>();
        await db.Init();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/api/servicos", (IConfiguration c) => Results.Ok(Prices(c)));
        app.MapPost("/api/solicitacoes", async (CreateQuote input, HttpRequest req, IConfiguration c, Database data) =>
        {
            var errors = Validate(input, c);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var key = req.Headers["Idempotency-Key"].ToString();
            if (!Regex.IsMatch(key, "^[a-zA-Z0-9_-]{8,128}$")) return Results.BadRequest(new { erro = "Envie Idempotency-Key de 8 a 128 caracteres." });
            var clean = input with { Nome = input.Nome.Trim(), Email = input.Email.Trim().ToLowerInvariant(), Telefone = input.Telefone?.Trim(), Servico = input.Servico.Trim(), Descricao = input.Descricao.Trim() };
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(clean))));
            var (quote, conflict, existing) = await data.Create(clean, key, hash, Prices(c)[clean.Servico]);
            if (conflict) return Results.Conflict(new { erro = "Chave de idempotência já usada com outros dados." });
            return Results.Json(new { quote!.Id, quote.Status, quote.CriadoEm, quote.ValorEstimado }, statusCode: existing ? 200 : 201);
        }).RequireRateLimiting("public");
        app.MapGet("/api/solicitacoes", async (HttpRequest r, IConfiguration c, Database d) => Auth(r, c) ? Results.Ok(await d.List()) : Results.Unauthorized());
        app.MapGet("/api/solicitacoes/{id:guid}", async (Guid id, HttpRequest r, IConfiguration c, Database d) => Auth(r, c) ? await d.Get(id) is { } q ? Results.Ok(q) : Results.NotFound() : Results.Unauthorized());
        app.MapPost("/api/solicitacoes/{id:guid}/iniciar", async (Guid id, HttpRequest r, IConfiguration c, Database d) => Auth(r, c) ? await d.Claim(id) ? Results.NoContent() : Results.Conflict(new { erro = "Solicitação já iniciada ou inexistente." }) : Results.Unauthorized());
        app.MapPost("/api/solicitacoes/{id:guid}/gerar-pdf", async (Guid id, HttpRequest r, IConfiguration c, Database d) =>
        {
            if (!Auth(r, c)) return Results.Unauthorized();
            var q = await d.Get(id); if (q is null) return Results.NotFound();
            var pdf = MakePdf(q); await d.MarkPdf(id);
            return Results.File(pdf, "application/pdf", $"orcamento-{id:N}.pdf");
        });
        app.MapPatch("/api/solicitacoes/{id:guid}/status", async (Guid id, StatusInput input, HttpRequest r, IConfiguration c, Database d) =>
        {
            if (!Auth(r, c)) return Results.Unauthorized();
            if (input.Status is not ("Em processamento" or "Concluída" or "Falha")) return Results.BadRequest(new { erro = "Status inválido." });
            if (input.Status == "Concluída" && (await d.Get(id))?.PdfGerado != true) return Results.BadRequest(new { erro = "Gere o PDF antes de concluir." });
            return await d.Status(id, input.Status, input.Erro) ? Results.NoContent() : Results.NotFound();
        });
        app.Run();
    }
    private static bool Auth(HttpRequest r, IConfiguration c)
    {
        var expected = c["FLUXOPRO_INTERNAL_KEY"];
        var actual = r.Headers["X-Internal-Key"].ToString();
        return !string.IsNullOrEmpty(expected) && expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
    }
    private static Dictionary<string, decimal> Prices(IConfiguration c) => new() { ["Site institucional"] = c.GetValue("Prices:Site", 800m), ["Landing page"] = c.GetValue("Prices:LandingPage", 500m), ["Automação simples"] = c.GetValue("Prices:Automation", 350m) };
    private static Dictionary<string, string[]> Validate(CreateQuote q, IConfiguration c)
    {
        var e = new Dictionary<string, string[]>();
        if (q.Nome?.Trim().Length is < 2 or > 100) e["nome"] = ["Nome deve ter de 2 a 100 caracteres."];
        if (q.Email is null || q.Email.Length > 254 || !Regex.IsMatch(q.Email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) e["email"] = ["E-mail inválido."];
        if (q.Telefone?.Length > 30) e["telefone"] = ["Telefone muito longo."];
        if (q.Servico is null || !Prices(c).ContainsKey(q.Servico.Trim())) e["servico"] = ["Selecione um serviço."];
        if (q.Descricao?.Trim().Length is < 10 or > 2000) e["descricao"] = ["Descrição deve ter de 10 a 2000 caracteres."];
        if (q.Quantidade is < 1 or > 20) e["quantidade"] = ["Quantidade deve ser de 1 a 20."];
        if (!q.Consentimento) e["consentimento"] = ["Aceite o aviso de privacidade."];
        return e;
    }
    private static byte[] MakePdf(Quote q) => Document.Create(doc => doc.Page(p =>
    {
        p.Size(PageSizes.A4); p.Margin(40); p.DefaultTextStyle(x => x.FontSize(11));
        p.Header().Text("FluxoPro | Orçamento demonstrativo").Bold().FontSize(20).FontColor(Colors.Blue.Darken2);
        p.Content().PaddingVertical(24).Column(col =>
        {
            col.Spacing(12);
            col.Item().Text($"Identificador: {q.Id:N}"); col.Item().Text($"Data: {q.CriadoEm:dd/MM/yyyy}");
            col.Item().Text($"Solicitante: {q.Nome} ({q.Email})"); col.Item().Text($"Serviço: {q.Servico}");
            col.Item().Text($"Quantidade: {q.Quantidade}"); col.Item().Text($"Descrição: {q.Descricao}");
            col.Item().Text($"Total estimado: {q.ValorEstimado.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))}").Bold().FontSize(17);
        });
        p.Footer().AlignCenter().Text("Documento demonstrativo — sem validade comercial");
    })).GeneratePdf();
}

record CreateQuote(string Nome, string Email, string? Telefone, string Servico, string Descricao, int Quantidade, bool Consentimento);
record StatusInput(string Status, string? Erro);
record Quote(Guid Id, string Nome, string Email, string? Telefone, string Servico, string Descricao, int Quantidade, decimal ValorEstimado, string Status, DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm, bool PdfGerado, string? UltimoErro);
