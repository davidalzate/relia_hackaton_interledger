using System.Net;
using System.Text.Json;
using Relia.Api.Rail;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 15: liquidaciones ("Pago de comisión").</summary>
public class T15SettlementTests
{
    private const string RosaPhone = "3100000001";

    /// <summary>Rosa con $500 de comisión: carga 100.000 por Bre-B y deposita 50.000 (completado).</summary>
    private static async Task<(Scenario Rosa, HttpClient Admin)> RosaWithCommissionAsync()
    {
        var s = await Scenario.RosaAsync();
        var topUp = (await (await s.TopUpAsync("breB", 100_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        await s.Api.CreateClient().PostEmpty($"/demo/quota-top-ups/{topUp}/breb-payment");
        var deposit = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        s.Rail.Complete(s.Rail.ForBackendPayment().Id);
        await s.PollAsync(deposit);
        return (s, await s.Api.AdminAsync());
    }

    private static async Task<JsonElement> SettleAsync(HttpClient admin, string agentId, HttpStatusCode expected = HttpStatusCode.Accepted) =>
        await (await admin.PostJson($"/agents/{agentId}/settlements", new { }, Http.Key())).Json(expected);

    private static async Task<JsonElement> PollSettlementAsync(Scenario s, HttpClient client, string id)
    {
        s.Api.Clock.Advance(TimeSpan.FromSeconds(2));
        return await (await client.GetAsync($"/settlements/{id}")).Json(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Paga_toda_la_comision_a_la_billetera_del_corresponsal_por_el_riel()
    {
        var (s, admin) = await RosaWithCommissionAsync();

        var response = await admin.PostJson($"/agents/{s.AgentId}/settlements", new { }, Http.Key());
        var settlement = await response.Json(HttpStatusCode.Accepted);

        Assert.Equal($"/settlements/{settlement.Str("id")}", response.Headers.Location?.ToString());
        Assert.Matches("^LQ-[0-9]{6}$", settlement.Str("reference"));
        Assert.Equal("pending", settlement.Str("status"));
        Assert.Equal(500, settlement.Long("amount"));
        Assert.Equal("$ilp.interledger-test.dev/rosa-test", settlement.Str("walletAddress"));
        Assert.Equal("2026-10-16", settlement.Str("periodFrom"));
        Assert.Equal("2026-10-16", settlement.Str("periodTo"));
        Assert.Equal("Admin Relia", settlement.Str("createdByAdmin.name"));
        var sent = s.Rail.Created[^1];
        Assert.Equal(RailDirection.Outgoing, sent.Direction);
        Assert.Equal("$ilp.interledger-test.dev/rosa-test", sent.Counterparty);
        Assert.Equal(500, sent.ReceiveAmount);

        s.Rail.Complete(s.Rail.ForBackendPayment().Id);
        var paid = await PollSettlementAsync(s, admin, settlement.Str("id")!);

        Assert.Equal("paid", paid.Str("status"));
        Assert.NotEqual(JsonValueKind.Null, paid.At("paidAt").ValueKind);
        var agent = await s.AgentAsync();
        Assert.Equal(0, agent.Long("balances.accruedCommission"));
        Assert.Equal(500, agent.Long("balances.settledCommissionTotal"));
        Assert.Contains((await s.InboxAsync(RosaPhone)).At("items").EnumerateArray(), m => m.Str("type") == "aviso_comision");

        var history = await (await s.Client.GetAsync(s.Url("history?kind=settlement"))).Json(HttpStatusCode.OK);
        var entry = Assert.Single(history.At("items").EnumerateArray());
        Assert.Equal("completed", entry.Str("status"));
        Assert.Equal(settlement.Str("id"), entry.Str("settlementId"));

        var today = await (await admin.GetAsync("/settlements?from=2026-10-16")).Json(HttpStatusCode.OK);
        Assert.Single(today.At("items").EnumerateArray());
        var list = await (await admin.GetAsync("/agents")).Json(HttpStatusCode.OK);
        Assert.Equal(500, list.Long("totals.commissionPaidToday"));
    }

    [Fact]
    public async Task Una_billetera_simulada_se_liquida_sin_riel()
    {
        var progreso = await Scenario.ProgresoAsync();
        var admin = await progreso.Api.AdminAsync();

        var settlement = await SettleAsync(admin, progreso.AgentId);
        var read = await (await admin.GetAsync($"/settlements/{settlement.Str("id")}")).Json(HttpStatusCode.OK);

        Assert.Equal("paid", read.Str("status"));
        Assert.Equal(12_500, read.Long("amount"));
        Assert.Empty(progreso.Rail.Created);
    }

    [Fact]
    public async Task Solo_una_liquidacion_pendiente_por_corresponsal()
    {
        var (s, admin) = await RosaWithCommissionAsync();
        await SettleAsync(admin, s.AgentId);

        var problem = await (await admin.PostJson($"/agents/{s.AgentId}/settlements", new { }, Http.Key()))
            .Problem(HttpStatusCode.Conflict, "settlement_in_progress");
        Assert.NotNull(problem.Str("title"));
    }

    [Fact]
    public async Task Sin_comision_responde_no_commission()
    {
        var s = await Scenario.RosaAsync();
        var admin = await s.Api.AdminAsync();

        await (await admin.PostJson($"/agents/{s.AgentId}/settlements", new { }, Http.Key()))
            .Problem(HttpStatusCode.UnprocessableEntity, "no_commission");
    }

    [Fact]
    public async Task Si_el_riel_no_acepta_el_pago_es_503_y_no_se_crea_nada()
    {
        var (s, admin) = await RosaWithCommissionAsync();
        s.Rail.Unavailable = true;

        await (await admin.PostJson($"/agents/{s.AgentId}/settlements", new { }, Http.Key()))
            .Problem(HttpStatusCode.ServiceUnavailable, "rail_unavailable");

        var list = await (await admin.GetAsync($"/settlements?agentId={s.AgentId}")).Json(HttpStatusCode.OK);
        Assert.Empty(list.At("items").EnumerateArray());
    }

    [Fact]
    public async Task Si_el_pago_falla_queda_anulada_con_el_motivo_y_la_comision_intacta()
    {
        var (s, admin) = await RosaWithCommissionAsync();
        var id = (await SettleAsync(admin, s.AgentId)).Str("id")!;

        s.Rail.Fail(s.Rail.ForBackendPayment().Id);
        var voided = await PollSettlementAsync(s, admin, id);

        Assert.Equal("voided", voided.Str("status"));
        Assert.Contains("insufficient_funds", voided.Str("voidReason"));
        Assert.Equal(500, (await s.AgentAsync()).Long("balances.accruedCommission"));
        var history = await (await s.Client.GetAsync(s.Url("history?kind=settlement"))).Json(HttpStatusCode.OK);
        Assert.Equal("cancelled", Assert.Single(history.At("items").EnumerateArray()).Str("status"));
    }

    [Fact]
    public async Task El_corresponsal_no_liquida_y_solo_ve_las_suyas()
    {
        var (s, admin) = await RosaWithCommissionAsync();
        var progreso = await Scenario.AsAsync("miscelanea@relia.dev", s.Api);
        var rosaSettlement = (await SettleAsync(admin, s.AgentId)).Str("id")!;
        await SettleAsync(admin, progreso.AgentId);

        await (await s.Client.PostJson($"/agents/{s.AgentId}/settlements", new { }, Http.Key()))
            .Problem(HttpStatusCode.Forbidden, "forbidden");

        var mine = await (await s.Client.GetAsync($"/settlements?agentId={progreso.AgentId}")).Json(HttpStatusCode.OK);
        Assert.Equal(rosaSettlement, Assert.Single(mine.At("items").EnumerateArray()).Str("id"));
        await (await progreso.Client.GetAsync($"/settlements/{rosaSettlement}")).Problem(HttpStatusCode.Forbidden, "forbidden");
    }
}
