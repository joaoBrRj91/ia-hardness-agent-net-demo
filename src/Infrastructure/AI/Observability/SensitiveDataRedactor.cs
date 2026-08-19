using System.Text.RegularExpressions;

namespace Infrastructure.AI.Observability;

/// <summary>
/// Sanitiza dados sensíveis ANTES de qualquer conteúdo entrar no pipeline de
/// telemetria. Roda antes do exporter — telemetria não pode virar escopo de
/// compliance.
///
/// PONTO DE EXTENSÃO: os padrões abaixo são um exemplo funcional (número de
/// cartão validado por Luhn, CVV e segredos genéricos). Substitua ou estenda
/// conforme os dados sensíveis do seu domínio. A validação Luhn está aí para
/// evitar falso-positivo em IDs numéricos longos que não são cartão.
///
/// Esta é apenas a primeira camada: o OTel Collector aplica uma segunda
/// (processor attributes/redact) — ver deploy/otel/otel-collector-config.yaml.
/// </summary>
public sealed partial class SensitiveDataRedactor : ITelemetryRedactor
{
    [GeneratedRegex(@"\b(?:\d[ -]*?){13,19}\b")]
    private static partial Regex CandidatePanRegex();

    [GeneratedRegex(@"(?i)\b(?:cvv|cvc|cvv2|security[ _-]?code)\b\s*[:=]?\s*\d{3,4}")]
    private static partial Regex CvvRegex();

    [GeneratedRegex(@"(?i)\b(?:api[_-]?key|bearer|authorization)\b\s*[:=]?\s*[\w\-\.]{16,}")]
    private static partial Regex SecretRegex();

    public string Redact(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;

        var result = CandidatePanRegex().Replace(content, m =>
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            if (!IsLuhnValid(digits)) return m.Value;          // não é cartão
            return $"[PAN_REDACTED_last4:{digits[^4..]}]";     // últimos 4 dígitos preservados para correlação
        });

        result = CvvRegex().Replace(result, "[CVV_REDACTED]");
        result = SecretRegex().Replace(result, "[SECRET_REDACTED]");
        return result;
    }

    private static bool IsLuhnValid(string digits)
    {
        if (digits.Length is < 13 or > 19) return false;

        int sum = 0; bool alternate = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (alternate) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }
}
