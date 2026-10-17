using System.Net;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 05: ajustes de efectivo e idempotencia genérica.</summary>
public class T05CashAdjustmentTests
{
    private static async Task<(ReliaApiFactory Api, HttpClient Rosa, string Url, string RosaId)> RosaAsync()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        var rosa = await api.RosaAsync();
        var id = (await (await rosa.GetAsync("/me")).Json(HttpStatusCode.OK)).Str("agent.id")!;
        return (api, rosa, $"/agents/{id}/cash-adjustments", id);
    }

    [Fact]
    public async Task Sumar_efectivo_responde_el_antes_y_el_despues_y_queda_en_el_historial()
    {
        var (_, rosa, url, id) = await RosaAsync();

        var adjustment = await (await rosa.PostJson(url, new { mode = "add", amount = 300_000 }, Http.Key()))
            .Json(HttpStatusCode.Created);

        Assert.Equal("add", adjustment.Str("mode"));
        Assert.Equal(300_000, adjustment.Long("variation"));
        Assert.Equal("Asignación de efectivo", adjustment.Str("reason"));
        Assert.Equal(0, adjustment.Long("cashOnHand.before"));
        Assert.Equal(300_000, adjustment.Long("cashOnHand.after"));

        var agent = await (await rosa.GetAsync($"/agents/{id}")).Json(HttpStatusCode.OK);
        Assert.Equal(300_000, agent.Long("balances.cashOnHand"));
        Assert.Equal(0, agent.Long("balances.quota"));

        var history = await (await rosa.GetAsync($"/agents/{id}/history?kind=cashAdjustment")).Json(HttpStatusCode.OK);
        var entry = Assert.Single(history.At("items").EnumerateArray());
        Assert.Equal("cashAdjustment", entry.Str("kind"));
        Assert.Equal("completed", entry.Str("status"));
        Assert.Equal(300_000, entry.Long("amount"));
        Assert.Equal(300_000, entry.Long("balances.cashOnHand.after"));
    }

    [Fact]
    public async Task Fijar_el_total_por_debajo_registra_una_variacion_negativa_con_su_motivo()
    {
        var (_, rosa, url, _) = await RosaAsync();
        await rosa.PostJson(url, new { mode = "add", amount = 300_000 }, Http.Key());

        var adjustment = await (await rosa.PostJson(url,
            new { mode = "set", amount = 250_000, reason = "Conteo de cierre" }, Http.Key())).Json(HttpStatusCode.Created);

        Assert.Equal(-50_000, adjustment.Long("variation"));
        Assert.Equal("Conteo de cierre", adjustment.Str("reason"));
        Assert.Equal(300_000, adjustment.Long("cashOnHand.before"));
        Assert.Equal(250_000, adjustment.Long("cashOnHand.after"));
    }

    [Fact]
    public async Task Fijar_sin_motivo_usa_ajuste_de_efectivo()
    {
        var (_, rosa, url, _) = await RosaAsync();

        var adjustment = await (await rosa.PostJson(url, new { mode = "set", amount = 10_000 }, Http.Key()))
            .Json(HttpStatusCode.Created);

        Assert.Equal("Ajuste de efectivo", adjustment.Str("reason"));
    }

    [Fact]
    public async Task Un_ajuste_sin_variacion_responde_no_change()
    {
        var (_, rosa, url, _) = await RosaAsync();

        await (await rosa.PostJson(url, new { mode = "set", amount = 0 }, Http.Key()))
            .Problem(HttpStatusCode.UnprocessableEntity, "no_change");
        await (await rosa.PostJson(url, new { mode = "add", amount = 0 }, Http.Key()))
            .Problem(HttpStatusCode.UnprocessableEntity, "validation_error");
    }

    [Fact]
    public async Task Repetir_la_misma_clave_y_cuerpo_devuelve_el_mismo_ajuste_sin_sumar_dos_veces()
    {
        var (_, rosa, url, id) = await RosaAsync();
        var key = Http.Key();

        var first = await (await rosa.PostJson(url, new { mode = "add", amount = 100_000 }, key)).Json(HttpStatusCode.Created);
        var second = await (await rosa.PostJson(url, new { mode = "add", amount = 100_000 }, key)).Json(HttpStatusCode.Created);

        Assert.Equal(first.Str("id"), second.Str("id"));
        var agent = await (await rosa.GetAsync($"/agents/{id}")).Json(HttpStatusCode.OK);
        Assert.Equal(100_000, agent.Long("balances.cashOnHand"));
    }

    [Fact]
    public async Task La_misma_clave_con_otro_cuerpo_responde_idempotency_conflict()
    {
        var (_, rosa, url, _) = await RosaAsync();
        var key = Http.Key();
        await rosa.PostJson(url, new { mode = "add", amount = 100_000 }, key);

        await (await rosa.PostJson(url, new { mode = "add", amount = 200_000 }, key))
            .Problem(HttpStatusCode.Conflict, "idempotency_conflict");
    }

    [Fact]
    public async Task Tras_un_error_la_misma_clave_se_puede_reintentar()
    {
        var (_, rosa, url, _) = await RosaAsync();
        var key = Http.Key();
        await (await rosa.PostJson(url, new { mode = "set", amount = 0 }, key))
            .Problem(HttpStatusCode.UnprocessableEntity, "no_change");

        await (await rosa.PostJson(url, new { mode = "add", amount = 5_000 }, key)).Json(HttpStatusCode.Created);
    }

    [Fact]
    public async Task El_admin_no_ajusta_efectivo()
    {
        var (api, _, url, _) = await RosaAsync();
        var admin = await api.AdminAsync();

        await (await admin.PostJson(url, new { mode = "add", amount = 1_000 }, Http.Key()))
            .Problem(HttpStatusCode.Forbidden, "forbidden");
    }
}
