using FluxoPro.Api.Infrastructure;
using FluxoPro.Api.Models;
using FluxoPro.Api.Services;

namespace FluxoPro.Api.Endpoints;

public static class InternalEndpoints
{
    public static void MapInternal(this WebApplication app)
    {
        var routes = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
        {
            var request = context.HttpContext.Request;
            var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            return InternalAuth.Allowed(request, config) ? await next(context) : Results.Unauthorized();
        });
        routes.MapGet("/solicitacoes", async (QuoteRepository db) => Results.Ok((await db.Search(null, null, 1, 100)).Items));
        routes.MapGet("/solicitacoes/{id:guid}", async (Guid id, QuoteRepository db) => await db.Get(id) is { } quote ? Results.Ok(quote) : Results.NotFound());
        routes.MapPost("/solicitacoes/{id:guid}/iniciar", async (Guid id, QuoteRepository db) => Outcome(await db.Claim(id)));
        routes.MapPost("/solicitacoes/{id:guid}/gerar-pdf", async (Guid id, QuoteRepository db, PdfService pdf) =>
        {
            var quote = await db.Get(id);
            if (quote is null) return Results.NotFound();
            if (quote.Status != QuoteStatus.Processing) return Results.Conflict(new { erro = "A solicitação não está em processamento." });
            var bytes = pdf.Create(quote);
            if (!await db.MarkPdf(id)) return Results.Conflict(new { erro = "O processamento mudou durante a geração do PDF." });
            return Results.File(bytes, "application/pdf", $"orcamento-{id:N}.pdf");
        });
        routes.MapPatch("/solicitacoes/{id:guid}/status", async (Guid id, StatusInput input, QuoteRepository db) =>
        {
            if (input.Status is not (QuoteStatus.Completed or QuoteStatus.Failed)) return Results.BadRequest(new { erro = "Status inválido para o workflow." });
            return Outcome(await db.ChangeStatus(id, input.Status, input.Erro, "n8n"));
        });
        routes.MapGet("/solicitacoes/{id:guid}/mensagens", async (Guid id, QuoteRepository db) =>
        {
            var quote = await db.Get(id); if (quote is null) return Results.NotFound();
            var templates = await db.Templates();
            var rendered = templates.ToDictionary(t => t.Chave, t => new
            {
                assunto = Render(t.Assunto, quote), corpo = Render(t.Corpo, quote)
            });
            return Results.Ok(rendered);
        });
    }

    private static string Render(string text, Quote q) => text.Replace("{{id}}", q.Id.ToString()).Replace("{{nome}}", q.Nome).Replace("{{servico}}", q.Servico);
    internal static IResult Outcome(ChangeResult result) => result switch
    {
        ChangeResult.Updated => Results.NoContent(),
        ChangeResult.NotFound => Results.NotFound(),
        _ => Results.Conflict(new { erro = "Transição de status não permitida." })
    };
}
