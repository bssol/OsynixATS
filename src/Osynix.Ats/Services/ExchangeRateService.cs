using System.Text.Json;

namespace Osynix.Ats.Services;

public sealed record ExchangeRateQuote(decimal PkrPerUsd, DateOnly SourceDate, DateTime SourceTimestampUtc, DateTime FetchedUtc);

public sealed class ExchangeRateService(HttpClient http)
{
    public async Task<ExchangeRateQuote> GetUsdToPkr(CancellationToken cancellationToken = default)
    {
        using var response=await http.GetAsync("https://api.exchangerate.fun/latest?base=USD",cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document=await JsonDocument.ParseAsync(stream,cancellationToken:cancellationToken);
        var root=document.RootElement;
        if(!root.TryGetProperty("rates",out var rates)||!rates.TryGetProperty("PKR",out var value)||!value.TryGetDecimal(out var rate)||rate is <100 or >1000)
            throw new InvalidOperationException("The exchange-rate service returned an invalid USD/PKR rate.");
        if(!root.TryGetProperty("timestamp",out var timestampValue)||!timestampValue.TryGetInt64(out var timestamp))
            throw new InvalidOperationException("The exchange-rate service did not include its update timestamp.");
        var sourceTimestamp=DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
        return new(rate,DateOnly.FromDateTime(sourceTimestamp),sourceTimestamp,DateTime.UtcNow);
    }
}
