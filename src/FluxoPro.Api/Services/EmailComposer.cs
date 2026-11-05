using System.Globalization;
using System.Net;
using FluxoPro.Api.Models;

namespace FluxoPro.Api.Services;

public static class EmailComposer
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public static string Text(Quote quote, string message, bool team)
    {
        var title = team ? "NOVO PEDIDO PARA A EQUIPE" : "SOLICITAÇÃO REGISTRADA";
        return $"""
            FLUXOPRO | {title}

            {message}

            Protocolo: {quote.Id}
            Serviço: {quote.Servico}
            Quantidade: {quote.Quantidade}
            Valor estimado: {quote.ValorEstimado.ToString("C", Br)}
            Data: {quote.CriadoEm.ToString("dd/MM/yyyy", Br)}

            {(team ? "Consulte o painel administrativo para acompanhar o histórico." : "O PDF demonstrativo segue anexo para sua referência.")}

            Orçamento demonstrativo, sem validade comercial.
            """;
    }

    public static string Html(Quote quote, string message, bool team)
    {
        var title = team ? "Novo pedido registrado." : "Recebemos sua solicitação.";
        var intro = team ? "A equipe já pode acompanhar o pedido no painel." : "Seu pedido foi registrado. Guarde o protocolo para referência.";
        var note = team ? "Consulte o painel administrativo para ver os detalhes e o histórico." : "O PDF demonstrativo segue anexo a esta mensagem.";
        var safeMessage = Escape(message).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "<br>");
        var protocol = Escape(quote.Id.ToString());
        var service = Escape(quote.Servico);
        var quantity = Escape(quote.Quantidade.ToString(Br));
        var price = Escape(quote.ValorEstimado.ToString("C", Br));
        var date = Escape(quote.CriadoEm.ToString("dd/MM/yyyy", Br));

        return $$"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f2f1e9;color:#102c25;font-family:Arial,Helvetica,sans-serif;">
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f2f1e9;"><tr><td align="center" style="padding:32px 12px;">
                <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="max-width:600px;background:#ffffff;border:1px solid #d5ddd2;">
                  <tr><td style="background:#102c25;padding:28px 34px;">
                    <div style="font-size:23px;font-weight:700;letter-spacing:-1px;color:#ffffff;">Fluxo<span style="color:#c8ef78;">Pro</span></div>
                    <div style="margin-top:7px;font-size:10px;letter-spacing:2px;font-weight:700;color:#c8ef78;">GESTÃO DE ORÇAMENTOS · DEMONSTRAÇÃO</div>
                  </td></tr>
                  <tr><td style="padding:38px 34px 32px;">
                    <div style="font-size:11px;letter-spacing:2px;font-weight:700;color:#557163;">{{(team ? "AVISO À EQUIPE" : "CONFIRMAÇÃO DO PEDIDO")}}</div>
                    <h1 style="margin:12px 0 10px;font-size:28px;line-height:1.2;letter-spacing:-1px;color:#102c25;">{{title}}</h1>
                    <p style="margin:0 0 28px;font-size:15px;line-height:1.6;color:#476257;">{{intro}}</p>
                    <div style="padding:20px 22px;background:#f5f7f1;border-left:3px solid #7aaa55;font-size:15px;line-height:1.65;color:#18382e;">{{safeMessage}}</div>
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="margin-top:30px;border-collapse:collapse;">
                      <tr><td colspan="2" style="padding:0 0 10px;font-size:11px;letter-spacing:1.7px;font-weight:700;color:#557163;">RESUMO DO PEDIDO</td></tr>
                      <tr><td style="padding:12px 8px 12px 0;border-top:1px solid #d5ddd2;color:#557163;font-size:13px;">Protocolo</td><td style="padding:12px 0;border-top:1px solid #d5ddd2;text-align:right;font-size:12px;font-weight:700;word-break:break-all;">{{protocol}}</td></tr>
                      <tr><td style="padding:12px 8px 12px 0;border-top:1px solid #d5ddd2;color:#557163;font-size:13px;">Serviço</td><td style="padding:12px 0;border-top:1px solid #d5ddd2;text-align:right;font-size:13px;font-weight:700;">{{service}}</td></tr>
                      <tr><td style="padding:12px 8px 12px 0;border-top:1px solid #d5ddd2;color:#557163;font-size:13px;">Quantidade</td><td style="padding:12px 0;border-top:1px solid #d5ddd2;text-align:right;font-size:13px;font-weight:700;">{{quantity}}</td></tr>
                      <tr><td style="padding:12px 8px 12px 0;border-top:1px solid #d5ddd2;color:#557163;font-size:13px;">Data do pedido</td><td style="padding:12px 0;border-top:1px solid #d5ddd2;text-align:right;font-size:13px;font-weight:700;">{{date}}</td></tr>
                      <tr><td style="padding:17px 8px 17px 0;background:#e7efdf;border-top:2px solid #102c25;font-size:13px;font-weight:700;">VALOR ILUSTRATIVO</td><td style="padding:17px 12px 17px 0;background:#e7efdf;border-top:2px solid #102c25;text-align:right;font-size:20px;font-weight:700;">{{price}}</td></tr>
                    </table>
                    <p style="margin:26px 0 0;font-size:13px;line-height:1.6;color:#476257;">{{note}}</p>
                  </td></tr>
                  <tr><td style="padding:22px 34px;background:#f5f7f1;border-top:1px solid #d5ddd2;font-size:12px;line-height:1.6;color:#557163;">
                    <strong style="color:#102c25;">FluxoPro</strong> · Projeto autoral demonstrativo<br>
                    Orçamento demonstrativo, sem validade comercial.
                  </td></tr>
                </table>
              </td></tr></table>
            </body></html>
            """;
    }

    private static string Escape(string text) => WebUtility.HtmlEncode(text);
}
