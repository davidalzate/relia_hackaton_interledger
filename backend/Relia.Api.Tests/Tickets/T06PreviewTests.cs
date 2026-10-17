using System.Net;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>
/// Ticket 06: desglose en vivo. Corresponsal sembrado "Miscelánea El Progreso": cupo 800.000, efectivo 350.000,
/// comisión 12.500. Tarifa 1.000 (500 Relia, 500 corresponsal).
/// </summary>
public class T06PreviewTests
{
    private static async Task<(HttpClient Client, string Url)> ProgresoAsync()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        var client = await api.LoginAsync("miscelanea@relia.dev");
        var id = (await (await client.GetAsync("/me")).Json(HttpStatusCode.OK)).Str("agent.id")!;
        return (client, $"/agents/{id}/operations/preview");
    }

    // tipo, M, redondo → neto, cobrado, cupo después, efectivo después
    [Theory]
    [InlineData("deposit", 50_000, false, 49_000, 50_000, 750_000, 400_000)]
    [InlineData("deposit", 50_000, true, 50_000, 51_000, 749_000, 401_000)]
    [InlineData("withdrawal", 100_000, false, 99_000, 100_000, 899_000, 251_000)]
    [InlineData("withdrawal", 100_000, true, 100_000, 101_000, 900_000, 250_000)]
    public async Task El_desglose_sigue_la_regla_del_valor_redondo(string type, long amount, bool round,
        long net, long charged, long quotaAfter, long cashAfter)
    {
        var (client, url) = await ProgresoAsync();

        var preview = await (await client.PostJson(url, new { type, requestedAmount = amount, roundAmount = round }))
            .Json(HttpStatusCode.OK);

        Assert.Equal(1_000, preview.Long("fee.total"));
        Assert.Equal(500, preview.Long("fee.relia"));
        Assert.Equal(500, preview.Long("fee.agent"));
        Assert.Equal(net, preview.Long("netAmount"));
        Assert.Equal(charged, preview.Long("chargedAmount"));
        Assert.Equal(quotaAfter, preview.Long("quotaAfter"));
        Assert.Equal(cashAfter, preview.Long("cashOnHandAfter"));
        Assert.Equal(13_000, preview.Long("accruedCommissionAfter"));
        Assert.Empty(preview.At("violations").EnumerateArray());
    }

    [Theory]
    [InlineData("deposit", 900_000, "insufficient_quota", 100_000)]
    [InlineData("withdrawal", 400_000, "insufficient_cash", 49_000)]
    public async Task Sin_saldo_responde_200_con_la_violacion_y_lo_que_falta(string type, long amount, string code,
        long shortfall)
    {
        var (client, url) = await ProgresoAsync();

        var preview = await (await client.PostJson(url, new { type, requestedAmount = amount, roundAmount = false }))
            .Json(HttpStatusCode.OK);

        var violation = Assert.Single(preview.At("violations").EnumerateArray());
        Assert.Equal(code, violation.Str("code"));
        Assert.Equal(shortfall, violation.Long("shortfall"));
    }

    [Fact]
    public async Task Un_monto_que_no_supera_la_tarifa_es_amount_too_low()
    {
        var (client, url) = await ProgresoAsync();

        var preview = await (await client.PostJson(url, new { type = "deposit", requestedAmount = 1_000, roundAmount = false }))
            .Json(HttpStatusCode.OK);

        Assert.Contains(preview.At("violations").EnumerateArray(), v => v.Str("code") == "amount_too_low");
    }

    [Fact]
    public async Task Un_monto_cero_es_validation_error()
    {
        var (client, url) = await ProgresoAsync();

        await (await client.PostJson(url, new { type = "deposit", requestedAmount = 0, roundAmount = false }))
            .Problem(HttpStatusCode.UnprocessableEntity, "validation_error");
    }
}
