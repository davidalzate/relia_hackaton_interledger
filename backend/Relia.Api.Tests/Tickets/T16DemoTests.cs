using System.Net;
using System.Net.Http.Json;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 16: fallos forzados, "La solicitud vence ya" y reinicio de la demo.</summary>
public class T16DemoTests
{
    [Fact]
    public async Task El_proximo_pago_falla_sin_llamar_al_riel_y_el_interruptor_se_apaga_solo()
    {
        var s = await Scenario.ProgresoAsync();
        var demo = s.Api.CreateClient();
        Assert.False((await (await demo.GetAsync("/demo/faults")).Json(HttpStatusCode.OK)).At("failNextPayment").GetBoolean());

        var set = await (await demo.PutAsJsonAsync("/demo/faults", new { failNextPayment = true })).Json(HttpStatusCode.OK);
        Assert.True(set.At("failNextPayment").GetBoolean());

        var failed = await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted);
        Assert.Equal("failed", failed.Str("status"));
        Assert.Equal("payment_failed", failed.Str("failureCode"));
        Assert.Empty(s.Rail.Created);
        Assert.False((await (await demo.GetAsync("/demo/faults")).Json(HttpStatusCode.OK)).At("failNextPayment").GetBoolean());

        Assert.Equal("pending", (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("status"));
    }

    [Fact]
    public async Task El_fallo_forzado_tambien_anula_una_liquidacion()
    {
        var s = await Scenario.ProgresoAsync();
        await s.Api.CreateClient().PutAsJsonAsync("/demo/faults", new { failNextPayment = true });

        var settlement = await (await (await s.Api.AdminAsync()).PostJson($"/agents/{s.AgentId}/settlements", new { }, Http.Key()))
            .Json(HttpStatusCode.Accepted);

        Assert.Equal("voided", settlement.Str("status"));
        Assert.Equal(12_500, (await s.AgentAsync()).Long("balances.accruedCommission"));
    }

    [Fact]
    public async Task Vencer_ya_cancela_en_el_riel_y_deja_la_solicitud_expired()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.WithdrawAsync(100_000)).Json(HttpStatusCode.Accepted)).Str("id")!;

        var expired = await (await s.Api.CreateClient().PostEmpty($"/demo/operations/{id}/expire")).Json(HttpStatusCode.OK);

        Assert.Equal("expired", expired.Str("status"));
        Assert.Equal("request_expired", expired.Str("failureCode"));
        Assert.Single(s.Rail.Cancelled);
    }

    [Fact]
    public async Task Vencer_ya_solo_aplica_a_retiros_y_cargas_pendientes()
    {
        var s = await Scenario.ProgresoAsync();
        var demo = s.Api.CreateClient();
        var deposit = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        var approved = (await (await s.WithdrawAsync(10_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        s.Rail.Approve(s.Rail.ForBackendPayment().Id);

        Assert.Equal(HttpStatusCode.Conflict, (await demo.PostEmpty($"/demo/operations/{deposit}/expire")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await demo.PostEmpty($"/demo/operations/{approved}/expire")).StatusCode);
        await (await demo.PostEmpty($"/demo/operations/{Guid.NewGuid()}/expire")).Problem(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Vencer_ya_con_el_riel_caido_es_503()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.WithdrawAsync(10_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        s.Rail.Unavailable = true;

        await (await s.Api.CreateClient().PostEmpty($"/demo/operations/{id}/expire"))
            .Problem(HttpStatusCode.ServiceUnavailable, "rail_unavailable");
    }

    [Fact]
    public async Task Reiniciar_cancela_los_cobros_pendientes_vacia_todo_y_vuelve_a_sembrar()
    {
        var s = await Scenario.ProgresoAsync();
        await s.WithdrawAsync(10_000);
        await s.Client.PostJson(s.Url("cash-adjustments"), new { mode = "add", amount = 1_000 }, Http.Key());
        var demo = s.Api.CreateClient();
        await demo.PutAsJsonAsync("/demo/faults", new { failNextPayment = true });

        Assert.Equal(HttpStatusCode.NoContent, (await demo.PostEmpty("/demo/reset")).StatusCode);

        Assert.Single(s.Rail.Cancelled);
        Assert.False((await (await demo.GetAsync("/demo/faults")).Json(HttpStatusCode.OK)).At("failNextPayment").GetBoolean());
        var progreso = await Scenario.AsAsync("miscelanea@relia.dev", s.Api);
        Assert.Empty((await (await progreso.Client.GetAsync(progreso.Url("history"))).Json(HttpStatusCode.OK)).At("items").EnumerateArray());
        Assert.Equal(350_000, (await progreso.AgentAsync()).Long("balances.cashOnHand"));

        var rosa = await progreso.Api.RosaAsync();
        await (await rosa.GetAsync($"/keys/{Seed.LinkedPhone}")).Json(HttpStatusCode.OK);
        await (await rosa.GetAsync($"/keys/{Seed.UnlinkedPhone}")).Problem(HttpStatusCode.NotFound, "key_not_found");
    }

    [Fact]
    public async Task La_siembra_trae_historiales_de_ejemplo_y_a_Rosa_en_ceros()
    {
        var api = await new ReliaApiFactory().ResetAsync();

        var sanJose = await Scenario.AsAsync("drogueria@relia.dev", api);
        var history = await (await sanJose.Client.GetAsync(sanJose.Url("history"))).Json(HttpStatusCode.OK);
        Assert.NotEmpty(history.At("items").EnumerateArray());
        Assert.Equal(1_200_000, (await sanJose.AgentAsync()).Long("balances.quota"));

        var rosa = await Scenario.RosaAsync();
        Assert.Empty((await (await rosa.Client.GetAsync(rosa.Url("history"))).Json(HttpStatusCode.OK)).At("items").EnumerateArray());
    }
}
