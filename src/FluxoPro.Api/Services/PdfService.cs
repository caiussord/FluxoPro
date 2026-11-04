using System.Globalization;
using FluxoPro.Api.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FluxoPro.Api.Services;

public sealed class PdfService
{
    public PdfService() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Create(Quote q)
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4); page.Margin(42); page.DefaultTextStyle(style => style.FontSize(11).FontColor(Colors.Grey.Darken3));
            page.Header().Column(header =>
            {
                header.Item().Text("FluxoPro").Bold().FontSize(23).FontColor("#174F3F");
                header.Item().Text("Orçamento demonstrativo").FontSize(13).FontColor("#537064");
            });
            page.Content().PaddingVertical(26).Column(content =>
            {
                content.Spacing(14);
                content.Item().Text($"Protocolo  {q.Id:N}").FontSize(10).FontColor(Colors.Grey.Darken1);
                content.Item().Text($"Emitido em  {q.CriadoEm:dd/MM/yyyy}");
                content.Item().LineHorizontal(1).LineColor("#D7E5DC");
                content.Item().Text("Solicitante").Bold().FontSize(13);
                content.Item().Text(q.Nome);
                content.Item().Text(q.Email);
                content.Item().PaddingTop(8).Text("Detalhes do serviço").Bold().FontSize(13);
                content.Item().Text(q.Servico);
                content.Item().Text($"Quantidade: {q.Quantidade}");
                content.Item().Text(q.Descricao);
                content.Item().PaddingTop(12).Background("#EAF4EC").Padding(16)
                    .Text($"Total estimado  {q.ValorEstimado.ToString("C", culture)}").Bold().FontSize(17).FontColor("#174F3F");
            });
            page.Footer().AlignCenter().Text("Documento demonstrativo — sem validade comercial").FontSize(9).FontColor(Colors.Grey.Medium);
        })).GeneratePdf();
    }
}
