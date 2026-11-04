using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluxoPro.Api.Infrastructure;
using FluxoPro.Api.Models;
using FluxoPro.Api.Services;

namespace FluxoPro.Api.Endpoints;

public static class PublicEndpoints
{
    public static void MapPublic(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/api/servicos", async (QuoteRepository db) => Results.Ok(await db.ActivePrices()));
        app.MapPost("/api/solicitacoes", async (CreateQuote input, HttpRequest request, QuoteRepository db) =>
        {
            var prices = await db.ActivePrices();
            var errors = QuoteValidation.Check(input, prices);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var key = request.Headers["Idempotency-Key"].ToString();
            if (!Regex.IsMatch(key, "^[a-zA-Z0-9_-]{8,128}$"))
                return Results.BadRequest(new { erro = "Envie Idempotency-Key de 8 a 128 caracteres." });
            var clean = input with { Nome = input.Nome.Trim(), Email = input.Email.Trim().ToLowerInvariant(), Telefone = input.Telefone?.Trim(), Servico = input.Servico.Trim(), Descricao = input.Descricao.Trim() };
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(clean))));
            var (quote, conflict, existing) = await db.Create(clean, key, hash, prices[clean.Servico]);
            if (conflict) return Results.Conflict(new { erro = "Chave de idempotência já usada com outros dados." });
            return Results.Json(new { quote!.Id, quote.Status, quote.CriadoEm, quote.ValorEstimado }, statusCode: existing ? 200 : 201);
        }).RequireRateLimiting("public");
    }
}
