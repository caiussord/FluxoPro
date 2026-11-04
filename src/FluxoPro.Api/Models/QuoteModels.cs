namespace FluxoPro.Api.Models;

public static class QuoteStatus
{
    public const string Received = "Recebida";
    public const string Processing = "Em processamento";
    public const string Completed = "Concluída";
    public const string Failed = "Falha";

    public static bool CanTransition(string current, string next, bool pdfGenerated) => (current, next) switch
    {
        (Received, Processing) => true,
        (Received, Failed) => true,
        (Processing, Completed) => pdfGenerated,
        (Processing, Failed) => true,
        _ => false
    };
}

public sealed record CreateQuote(string Nome, string Email, string? Telefone, string Servico, string Descricao, int Quantidade, bool Consentimento);
public sealed record StatusInput(string Status, string? Erro);
public sealed record Quote(Guid Id, string Nome, string Email, string? Telefone, string Servico, string Descricao, int Quantidade, decimal ValorEstimado, string Status, DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm, bool PdfGerado, string? UltimoErro);
public sealed record ServiceItem(string Nome, decimal Preco, bool Ativo);
public sealed record HistoryEntry(DateTimeOffset Em, string? De, string Para, string Origem, string? Detalhe);
public sealed record QuotePage(IReadOnlyList<Quote> Items, int Total, int Page, int PageSize);
public enum ChangeResult { Updated, NotFound, Conflict }
public sealed record AdminLogin(string Password);
public sealed record PriceUpdate(decimal Preco, bool Ativo);
public sealed record TemplateUpdate(string Assunto, string Corpo);
public sealed record EmailTemplate(string Chave, string Assunto, string Corpo);
