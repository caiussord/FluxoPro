using System.Text.RegularExpressions;
using FluxoPro.Api.Models;

namespace FluxoPro.Api.Services;

public static class QuoteValidation
{
    public static Dictionary<string, string[]> Check(CreateQuote q, IReadOnlyDictionary<string, decimal> prices)
    {
        var errors = new Dictionary<string, string[]>();
        if (q.Nome?.Trim().Length is < 2 or > 100) errors["nome"] = ["Nome deve ter de 2 a 100 caracteres."];
        if (q.Email is null || q.Email.Length > 254 || !Regex.IsMatch(q.Email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) errors["email"] = ["E-mail inválido."];
        if (q.Telefone?.Length > 30) errors["telefone"] = ["Telefone muito longo."];
        if (q.Servico is null || !prices.ContainsKey(q.Servico.Trim())) errors["servico"] = ["Selecione um serviço."];
        if (q.Descricao?.Trim().Length is < 10 or > 2000) errors["descricao"] = ["Descrição deve ter de 10 a 2000 caracteres."];
        if (q.Quantidade is < 1 or > 20) errors["quantidade"] = ["Quantidade deve ser de 1 a 20."];
        if (!q.Consentimento) errors["consentimento"] = ["Aceite o aviso de privacidade."];
        return errors;
    }
}
