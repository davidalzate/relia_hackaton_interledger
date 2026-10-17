using System.Net;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 11: retiro de punta a punta. "El Progreso": cupo 800.000, efectivo 350.000, comisión 12.500.</summary>
public class T11WithdrawalTests
{
    private static async Task<(Scenario S, string Id)> PendingAsync(long amount = 100_000)
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.WithdrawAsync(amount)).Json(HttpStatusCode.Accepted)).Str("id")!;
        return (s, id);
    }

    [Fact]
    public async Task Crear_un_retiro_reserva_efectivo_y_le_cobra_al_usuario_con_aprobacion()
    {
        var s = await Scenario.ProgresoAsync();

        var operation = await (await s.WithdrawAsync(100_000)).Json(HttpStatusCode.Accepted);

        Assert.Equal("withdrawal", operation.Str("type"));
        Assert.Equal("pending", operation.Str("status"));
        Assert.Equal(99_000, operation.Long("netAmount"));
        Assert.Equal(100_000, operation.Long("chargedAmount"));
        Assert.Equal(ReliaApiFactory.Start.AddMinutes(5), operation.At("expiresAt").GetDateTimeOffset());
        var approvalUrl = operation.Str("approvalUrl");
        Assert.StartsWith("https://auth.interledger-test.dev/interact/", approvalUrl);

        var sent = Assert.Single(s.Rail.Created);
        Assert.Equal(Relia.Api.Rail.RailDirection.Incoming, sent.Direction);
        Assert.Equal("$ilp.interledger-test.dev/jeffdev", sent.Counterparty);
        Assert.Equal(100_000, sent.ReceiveAmount);

        Assert.Equal(99_000, (await s.AgentAsync()).Long("balances.cashReserved"));

        var inbox = await s.InboxAsync(Seed.LinkedPhone);
        var sms = Assert.Single(inbox.At("items").EnumerateArray(), m => m.Str("type") == "solicitud_pago");
        Assert.Equal(approvalUrl, sms.Str("link"));

        var requests = await (await s.Api.CreateClient().GetAsync($"/demo/payment-requests?phone={Seed.LinkedPhone}"))
            .Json(HttpStatusCode.OK);
        var request = Assert.Single(requests.At("items").EnumerateArray());
        Assert.Equal("withdrawal", request.Str("type"));
        Assert.Equal("Miscelánea El Progreso", request.Str("agentName"));
        Assert.Equal(100_000, request.Long("chargedAmount"));
        Assert.Equal(approvalUrl, request.Str("approvalUrl"));
    }

    [Fact]
    public async Task Aprobado_y_completado_mueve_cupo_efectivo_y_comision()
    {
        var (s, id) = await PendingAsync();
        var railId = s.Rail.ForBackendPayment().Id;

        s.Rail.Approve(railId);
        Assert.Equal("pending", (await s.PollAsync(id)).Str("status"));
        s.Rail.Complete(railId);
        var operation = await s.PollAsync(id);

        Assert.Equal("completed", operation.Str("status"));
        Assert.Equal(899_000, operation.Long("balances.quota.after"));
        Assert.Equal(251_000, operation.Long("balances.cashOnHand.after"));
        Assert.Equal(13_000, operation.Long("balances.accruedCommission.after"));
        Assert.Equal(0, (await s.AgentAsync()).Long("balances.cashReserved"));
        await (await s.Client.GetAsync($"/operations/{id}/receipt")).Json(HttpStatusCode.OK);
    }

    public static TheoryData<string, string, string> RailOutcomes() => new()
    {
        { "reject", "failed", "customer_rejected" },
        { "expire", "expired", "request_expired" },
        { "cancel", "cancelled", "request_cancelled" },
        { "fail", "failed", "payment_failed" },
    };

    [Theory]
    [MemberData(nameof(RailOutcomes))]
    public async Task El_estado_del_riel_se_traduce_al_de_la_operacion(string outcome, string status, string failureCode)
    {
        var (s, id) = await PendingAsync();
        var railId = s.Rail.ForBackendPayment().Id;

        switch (outcome)
        {
            case "reject": s.Rail.Reject(railId); break;
            case "expire": s.Rail.Expire(railId); break;
            case "cancel": await s.Rail.CancelPaymentAsync(railId); break;
            default: s.Rail.Approve(railId); s.Rail.Fail(railId); break;
        }
        var operation = await s.PollAsync(id);

        Assert.Equal(status, operation.Str("status"));
        Assert.Equal(failureCode, operation.Str("failureCode"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, operation.At("approvalUrl").ValueKind);
        Assert.Equal(0, (await s.AgentAsync()).Long("balances.cashReserved"));
    }

    [Fact]
    public async Task Sin_efectivo_suficiente_responde_insufficient_cash()
    {
        var s = await Scenario.ProgresoAsync();

        var problem = await (await s.WithdrawAsync(400_000)).Problem(HttpStatusCode.UnprocessableEntity, "insufficient_cash");

        Assert.Equal(49_000, problem.Long("shortfall"));
    }

    [Fact]
    public async Task Cancelar_antes_de_aprobar_cancela_en_el_riel()
    {
        var (s, id) = await PendingAsync();

        var operation = await (await s.Client.PostEmpty($"/operations/{id}/cancel")).Json(HttpStatusCode.OK);

        Assert.Equal("cancelled", operation.Str("status"));
        Assert.Equal("request_cancelled", operation.Str("failureCode"));
        Assert.Single(s.Rail.Cancelled);
    }

    [Fact]
    public async Task Despues_de_aprobar_ya_no_se_puede_cancelar()
    {
        var (s, id) = await PendingAsync();
        s.Rail.Approve(s.Rail.ForBackendPayment().Id);

        await (await s.Client.PostEmpty($"/operations/{id}/cancel")).Problem(HttpStatusCode.Conflict, "operation_not_cancellable");
        Assert.Equal("pending", (await s.PollAsync(id)).Str("status"));
    }

    [Fact]
    public async Task Un_deposito_no_se_cancela()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;

        await (await s.Client.PostEmpty($"/operations/{id}/cancel")).Problem(HttpStatusCode.Conflict, "operation_not_cancellable");
    }

    [Fact]
    public async Task A_los_5_minutos_el_backend_la_vence_y_cancela_en_el_riel()
    {
        var (s, id) = await PendingAsync();

        s.Api.Clock.Advance(TimeSpan.FromMinutes(5));
        var operation = await s.PollAsync(id);

        Assert.Equal("expired", operation.Str("status"));
        Assert.Equal("request_expired", operation.Str("failureCode"));
        Assert.Single(s.Rail.Cancelled);
    }

    [Fact]
    public async Task Si_el_usuario_ya_aprobo_el_vencimiento_no_la_cierra()
    {
        var (s, id) = await PendingAsync();
        s.Rail.Approve(s.Rail.ForBackendPayment().Id);

        s.Api.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal("pending", (await s.PollAsync(id)).Str("status"));
    }

    [Fact]
    public async Task Confirmar_la_entrega_queda_en_auditoria_sin_tocar_saldos_y_es_idempotente()
    {
        var (s, id) = await PendingAsync();
        await (await s.Client.PostEmpty($"/operations/{id}/cash-handover"))
            .Problem(HttpStatusCode.Conflict, "operation_not_completed");
        s.Rail.Complete(s.Rail.ForBackendPayment().Id);
        await s.PollAsync(id);
        var cashBefore = (await s.AgentAsync()).Long("balances.cashOnHand");

        var first = await (await s.Client.PostEmpty($"/operations/{id}/cash-handover")).Json(HttpStatusCode.OK);
        s.Api.Clock.Advance(TimeSpan.FromMinutes(1));
        var again = await (await s.Client.PostEmpty($"/operations/{id}/cash-handover")).Json(HttpStatusCode.OK);

        Assert.Equal("completed", first.Str("status"));
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, first.At("cashHandedOverAt").ValueKind);
        Assert.Equal(first.Str("cashHandedOverAt"), again.Str("cashHandedOverAt"));
        Assert.Equal(cashBefore, (await s.AgentAsync()).Long("balances.cashOnHand"));
    }

    [Fact]
    public async Task El_admin_no_cancela_ni_confirma_entregas()
    {
        var (s, id) = await PendingAsync();
        var admin = await s.Api.AdminAsync();

        await (await admin.PostEmpty($"/operations/{id}/cancel")).Problem(HttpStatusCode.Forbidden, "forbidden");
        await (await admin.PostEmpty($"/operations/{id}/cash-handover")).Problem(HttpStatusCode.Forbidden, "forbidden");
        await (await admin.PostJson(s.Url("withdrawals"),
                new { phone = Seed.LinkedPhone, requestedAmount = 10_000, roundAmount = false }, Http.Key()))
            .Problem(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Fijar_el_efectivo_por_debajo_de_lo_reservado_responde_insufficient_cash()
    {
        var (s, _) = await PendingAsync();

        var problem = await (await s.Client.PostJson(s.Url("cash-adjustments"), new { mode = "set", amount = 50_000 }, Http.Key()))
            .Problem(HttpStatusCode.UnprocessableEntity, "insufficient_cash");

        Assert.Equal(49_000, problem.Long("shortfall"));
    }
}
