using System.Globalization;
using FluxoPro.Api.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FluxoPro.Api.Services;

public sealed class PdfService
{
    private const string Ink = "#102C25";
    private const string Muted = "#536B5D";
    private const string Rule = "#CBD6C8";
    private const string Tint = "#E6EFDF";
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public PdfService() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Create(Quote quote)
    {
        var unit = quote.Quantidade > 0 ? quote.ValorEstimado / quote.Quantidade : 0m;
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(42);
            page.DefaultTextStyle(style => style.FontSize(10).FontColor(Ink));
            page.Header().Column(header =>
            {
                header.Item().Row(row =>
                {
                    row.RelativeItem().Text("FluxoPro").Bold().FontSize(24).FontColor(Ink);
                    row.ConstantItem(170).AlignRight().Text("GESTÃO DE ORÇAMENTOS").Bold().FontSize(8).FontColor(Muted);
                });
                header.Item().PaddingTop(12).LineHorizontal(1).LineColor(Ink);
            });
            page.Content().PaddingVertical(26).Column(content =>
            {
                content.Spacing(17);
                content.Item().Text("DOCUMENTO DEMONSTRATIVO").Bold().FontSize(8).FontColor(Muted);
                content.Item().Text("Resumo da solicitação").Bold().FontSize(22).FontColor(Ink);
                content.Item().Row(row =>
                {
                    row.RelativeItem().Column(column =>
                    {
                        column.Item().Text("PROTOCOLO").Bold().FontSize(8).FontColor(Muted);
                        column.Item().Text(quote.Id.ToString()).FontSize(10);
                    });
                    row.ConstantItem(135).Column(column =>
                    {
                        column.Item().Text("DATA DO PEDIDO").Bold().FontSize(8).FontColor(Muted);
                        column.Item().Text(quote.CriadoEm.ToString("dd/MM/yyyy", Br)).FontSize(10);
                    });
                });
                content.Item().LineHorizontal(1).LineColor(Rule);
                content.Item().Column(column =>
                {
                    column.Item().Text("SOLICITANTE").Bold().FontSize(8).FontColor(Muted);
                    column.Item().Text(quote.Nome).Bold().FontSize(12);
                    column.Item().Text(quote.Email).FontSize(9).FontColor(Muted);
                });
                content.Item().PaddingTop(6).Text("Serviço solicitado").Bold().FontSize(13);
                content.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(4);
                        columns.ConstantColumn(48);
                        columns.ConstantColumn(95);
                        columns.ConstantColumn(95);
                    });
                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text("SERVIÇO");
                        header.Cell().Element(HeaderCell).AlignRight().Text("QTD.");
                        header.Cell().Element(HeaderCell).AlignRight().Text("UNITÁRIO");
                        header.Cell().Element(HeaderCell).AlignRight().Text("SUBTOTAL");
                    });
                    table.Cell().Element(BodyCell).Text(quote.Servico).Bold();
                    table.Cell().Element(BodyCell).AlignRight().Text(quote.Quantidade.ToString(Br));
                    table.Cell().Element(BodyCell).AlignRight().Text(unit.ToString("C", Br));
                    table.Cell().Element(BodyCell).AlignRight().Text(quote.ValorEstimado.ToString("C", Br));
                });
                content.Item().Background(Tint).Padding(15).Row(row =>
                {
                    row.RelativeItem().Text("TOTAL ESTIMADO").Bold().FontSize(9).FontColor(Ink);
                    row.ConstantItem(160).AlignRight().Text(quote.ValorEstimado.ToString("C", Br)).Bold().FontSize(17).FontColor(Ink);
                });
                content.Item().PaddingTop(8).Column(column =>
                {
                    column.Item().Text("Observações do pedido").Bold().FontSize(13);
                    column.Item().PaddingTop(7).Text(quote.Descricao).FontSize(10).LineHeight(1.35f);
                });
                content.Item().PaddingTop(10).Text("Valores ilustrativos calculados a partir do preço unitário e da quantidade informada. Não incluem condições comerciais, impostos ou contrato.")
                    .FontSize(9).FontColor(Muted).LineHeight(1.3f);
            });
            page.Footer().Column(footer =>
            {
                footer.Item().LineHorizontal(1).LineColor(Rule);
                footer.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Text("Orçamento demonstrativo, sem validade comercial").Bold().FontSize(8).FontColor(Muted);
                    row.ConstantItem(70).AlignRight().Text(text => { text.Span("Página "); text.CurrentPageNumber(); });
                });
            });
        })).GeneratePdf();
    }

    private static IContainer HeaderCell(IContainer cell) => cell.Background(Ink).PaddingVertical(9).PaddingHorizontal(7).DefaultTextStyle(style => style.FontSize(7).Bold().FontColor("#FFFFFF"));
    private static IContainer BodyCell(IContainer cell) => cell.BorderBottom(1).BorderColor(Rule).PaddingVertical(12).PaddingHorizontal(7).DefaultTextStyle(style => style.FontSize(9).FontColor(Ink));
}
