using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluxoPro.Api.Infrastructure;
using FluxoPro.Api.Models;
using FluxoPro.Api.Services;
using Microsoft.AspNetCore.Authentication;

namespace FluxoPro.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdmin(this WebApplication app)
    {
        app.MapPost("/api/admin/login", async (AdminLogin input, HttpContext context, IConfiguration config) =>
        {
            var expected = config["FLUXOPRO_ADMIN_PASSWORD"];
            if (string.IsNullOrWhiteSpace(expected) || expected.Length < 16)
                return Results.Problem("Defina FLUXOPRO_ADMIN_PASSWORD com pelo menos 16 caracteres.", statusCode: 503);
            var supplied = input.Password ?? "";
            if (supplied.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected)))
                return Results.Unauthorized();
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Administrador")], "Admin");
            await context.SignInAsync("Admin", new ClaimsPrincipal(identity));
            return Results.NoContent();
        }).RequireRateLimiting("login");

        var admin = app.MapGroup("/api/admin").RequireAuthorization().AddEndpointFilter(async (context, next) =>
        {
            var req = context.HttpContext.Request;
            if (req.Method is "POST" or "PUT" or "PATCH" or "DELETE")
                if (req.Headers["X-FluxoPro-Admin"] != "1") return Results.BadRequest(new { erro = "Cabeçalho administrativo ausente." });
            return await next(context);
        });
        admin.MapGet("/session", () => Results.Ok(new { authenticated = true }));
        admin.MapPost("/logout", async (HttpContext context) => { await context.SignOutAsync("Admin"); return Results.NoContent(); });
        admin.MapGet("/overview", async (QuoteRepository db) => Results.Ok(await db.Overview()));
        admin.MapGet("/quotes", async (string? search, string? status, int? page, QuoteRepository db) =>
        {
            if (search?.Length > 100 || !ValidStatus(status)) return Results.BadRequest(new { erro = "Filtro inválido." });
            return Results.Ok(await db.Search(search, status, page ?? 1, 20));
        });
        admin.MapGet("/quotes/{id:guid}", async (Guid id, QuoteRepository db) =>
        {
            var quote = await db.Get(id);
            return quote is null ? Results.NotFound() : Results.Ok(new { quote, history = await db.History(id) });
        });
        admin.MapGet("/quotes/{id:guid}/pdf", async (Guid id, QuoteRepository db, PdfService pdf) =>
        {
            var quote = await db.Get(id);
            return quote is null ? Results.NotFound() : Results.File(pdf.Create(quote), "application/pdf", $"orcamento-{id:N}.pdf");
        });
        admin.MapPost("/quotes/{id:guid}/retry", async (Guid id, QuoteRepository db) => InternalEndpoints.Outcome(await db.RetrySafe(id)));
        admin.MapGet("/services", async (QuoteRepository db) => Results.Ok(await db.Services()));
        admin.MapPut("/services/{name}", async (string name, PriceUpdate input, QuoteRepository db) =>
        {
            if (input.Preco is <= 0 or > 1000000) return Results.BadRequest(new { erro = "Preço deve ser maior que zero e até R$ 1.000.000." });
            return await db.UpdateService(name, input) ? Results.NoContent() : Results.NotFound();
        });
        admin.MapGet("/templates", async (QuoteRepository db) => Results.Ok(await db.Templates()));
        admin.MapPut("/templates/{key}", async (string key, TemplateUpdate input, QuoteRepository db) =>
        {
            if (input.Assunto?.Trim().Length is < 5 or > 150 || input.Corpo?.Trim().Length is < 10 or > 2000)
                return Results.BadRequest(new { erro = "Assunto ou corpo fora dos limites." });
            return await db.UpdateTemplate(key, input) ? Results.NoContent() : Results.NotFound();
        });
        admin.MapGet("/export.csv", async (string? search, string? status, QuoteRepository db) =>
        {
            if (search?.Length > 100 || !ValidStatus(status)) return Results.BadRequest(new { erro = "Filtro inválido." });
            var quotes = (await db.Search(search, status, 1, 5000)).Items;
            var csv = new StringBuilder("ID;Criado em;Nome;Email;Serviço;Quantidade;Valor estimado;Status\r\n");
            foreach (var q in quotes)
                csv.AppendLine(string.Join(';', Csv(q.Id.ToString()), Csv(q.CriadoEm.ToString("O")), Csv(q.Nome), Csv(q.Email), Csv(q.Servico), q.Quantidade, q.ValorEstimado.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Csv(q.Status)));
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", "solicitacoes-fluxopro.csv");
        });
    }

    private static bool ValidStatus(string? status) => string.IsNullOrWhiteSpace(status) || status is QuoteStatus.Received or QuoteStatus.Processing or QuoteStatus.Completed or QuoteStatus.Failed;
    private static string Csv(string value)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ');
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
