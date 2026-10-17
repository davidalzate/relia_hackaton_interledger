using System.Net;
using Microsoft.EntityFrameworkCore;
using Relia.Api.Persistence;
using Relia.Api.Rail;
using Relia.Api.Services.Support;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 09: depósito de punta a punta. "El Progreso": cupo 800.000, efectivo 350.000, comisión 12.500.</summary>
public class T09DepositTests
{
    [Fact]
    public async Task Crear_un_deposito_reserva_cupo_y_paga_el_neto_al_usuario_por_el_riel()
    {
        var s = await Scenario.ProgresoAsync();

        var response = await s.DepositAsync(50_000);
        var operation = await response.Json(HttpStatusCode.Accepted);

        Assert.Equal($"/operations/{operation.Str("id")}", response.Headers.Location?.ToString());
        Assert.Equal("deposit", operation.Str("type"));
        Assert.Equal("pending", operation.Str("status"));
        Assert.Matches("^OP-[0-9]{6}$", operation.Str("reference"));
        Assert.Equal(49_000, operation.Long("netAmount"));
        Assert.Equal(50_000, operation.Long("chargedAmount"));
        Assert.Equal(1_000, operation.Long("fee.total"));
        Assert.Equal("300 *** 0001", operation.Str("customer.phoneMasked"));
        Assert.Equal("$ilp...test.dev/je***", operation.Str("customer.walletAddressMasked"));

        var sent = Assert.Single(s.Rail.Created);
        Assert.Equal(RailDirection.Outgoing, sent.Direction);
        Assert.Equal("$ilp.interledger-test.dev/jeffdev", sent.Counterparty);
        Assert.Equal(49_000, sent.ReceiveAmount);

        var agent = await s.AgentAsync();
        Assert.Equal(800_000, agent.Long("balances.quota"));
        Assert.Equal(50_000, agent.Long("balances.quotaReserved"));
    }

    [Fact]
    public async Task Al_completarse_mueve_los_saldos_avisa_al_usuario_y_queda_en_el_historial()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;

        s.Rail.Complete(s.Rail.ForBackendPayment().Id);
        var operation = await s.PollAsync(id);

        Assert.Equal("completed", operation.Str("status"));
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, operation.At("completedAt").ValueKind);
        Assert.Equal(800_000, operation.Long("balances.quota.before"));
        Assert.Equal(750_000, operation.Long("balances.quota.after"));
        Assert.Equal(350_000, operation.Long("balances.cashOnHand.before"));
        Assert.Equal(400_000, operation.Long("balances.cashOnHand.after"));
        Assert.Equal(13_000, operation.Long("balances.accruedCommission.after"));

        var agent = await s.AgentAsync();
        Assert.Equal(750_000, agent.Long("balances.quota"));
        Assert.Equal(0, agent.Long("balances.quotaReserved"));

        var inbox = await s.InboxAsync(Seed.LinkedPhone);
        Assert.Contains(inbox.At("items").EnumerateArray(), m => m.Str("type") == "aviso_deposito");

        var history = await (await s.Client.GetAsync(s.Url("history?kind=deposit"))).Json(HttpStatusCode.OK);
        var entry = Assert.Single(history.At("items").EnumerateArray());
        Assert.Equal("completed", entry.Str("status"));
        Assert.Equal(50_000, entry.Long("amount"));
        Assert.Equal(id, entry.Str("operationId"));
        Assert.Equal(750_000, entry.Long("balances.quota.after"));
    }

    [Fact]
    public async Task Si_el_pago_falla_la_operacion_queda_failed_y_libera_la_reserva()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;

        s.Rail.Fail(s.Rail.ForBackendPayment().Id);
        var operation = await s.PollAsync(id);

        Assert.Equal("failed", operation.Str("status"));
        Assert.Equal("payment_failed", operation.Str("failureCode"));
        var agent = await s.AgentAsync();
        Assert.Equal(800_000, agent.Long("balances.quota"));
        Assert.Equal(0, agent.Long("balances.quotaReserved"));
    }

    [Fact]
    public async Task Sin_cupo_suficiente_responde_insufficient_quota_y_no_crea_nada()
    {
        var s = await Scenario.ProgresoAsync();

        var problem = await (await s.DepositAsync(900_000)).Problem(HttpStatusCode.UnprocessableEntity, "insufficient_quota");

        Assert.Equal(100_000, problem.Long("shortfall"));
        Assert.Empty(s.Rail.Created);
    }

    [Fact]
    public async Task Un_monto_que_no_supera_la_tarifa_es_validation_error()
    {
        var s = await Scenario.ProgresoAsync();

        await (await s.DepositAsync(1_000)).Problem(HttpStatusCode.UnprocessableEntity, "validation_error");
    }

    [Fact]
    public async Task Un_celular_sin_llave_responde_key_not_found()
    {
        var s = await Scenario.ProgresoAsync();

        await (await s.DepositAsync(50_000, Seed.UnlinkedPhone)).Problem(HttpStatusCode.NotFound, "key_not_found");
    }

    [Fact]
    public async Task Sin_confirmar_el_efectivo_no_se_crea()
    {
        var s = await Scenario.ProgresoAsync();

        await (await s.Client.PostJson(s.Url("deposits"),
                new { phone = Seed.LinkedPhone, requestedAmount = 50_000, roundAmount = false, cashReceivedConfirmed = false },
                Http.Key()))
            .Problem(HttpStatusCode.UnprocessableEntity, "validation_error");
    }

    [Fact]
    public async Task Si_el_riel_no_acepta_el_pago_responde_503_sin_crear_nada_y_se_reintenta_con_la_misma_clave()
    {
        var s = await Scenario.ProgresoAsync();
        var key = Http.Key();
        s.Rail.Unavailable = true;

        await (await s.DepositAsync(50_000, key: key)).Problem(HttpStatusCode.ServiceUnavailable, "rail_unavailable");
        var history = await (await s.Client.GetAsync(s.Url("history"))).Json(HttpStatusCode.OK);
        Assert.Empty(history.At("items").EnumerateArray());
        Assert.Equal(0, (await s.AgentAsync()).Long("balances.quotaReserved"));

        s.Rail.Unavailable = false;
        await (await s.DepositAsync(50_000, key: key)).Json(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Repetir_la_clave_devuelve_el_mismo_deposito_y_otro_cuerpo_es_conflicto()
    {
        var s = await Scenario.ProgresoAsync();
        var key = Http.Key();

        var first = await (await s.DepositAsync(50_000, key: key)).Json(HttpStatusCode.Accepted);
        var again = await (await s.DepositAsync(50_000, key: key)).Json(HttpStatusCode.Accepted);
        Assert.Equal(first.Str("id"), again.Str("id"));
        Assert.Single(s.Rail.Created);

        await (await s.DepositAsync(60_000, key: key)).Problem(HttpStatusCode.Conflict, "idempotency_conflict");
    }

    [Fact]
    public async Task Una_lectura_del_riel_con_version_vieja_se_descarta()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;

        s.Rail.Overwrite(s.Rail.ForBackendPayment().Id, RailPaymentStatus.Completed, version: 1);

        Assert.Equal("pending", (await s.PollAsync(id)).Str("status"));
    }

    [Fact]
    public async Task El_riel_se_consulta_como_maximo_una_vez_por_segundo_por_pago()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        var before = s.Rail.GetCalls;

        await s.Client.GetAsync($"/operations/{id}");
        await s.Client.GetAsync($"/operations/{id}");
        await s.Client.GetAsync($"/operations/{id}");

        Assert.Equal(before + 1, s.Rail.GetCalls);
    }

    [Fact]
    public async Task Otro_corresponsal_no_lee_la_operacion_pero_el_admin_si()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;

        var rosa = await s.Api.RosaAsync();
        await (await rosa.GetAsync($"/operations/{id}")).Problem(HttpStatusCode.Forbidden, "forbidden");

        var admin = await s.Api.AdminAsync();
        await (await admin.GetAsync($"/operations/{id}")).Json(HttpStatusCode.OK);
        await (await admin.GetAsync($"/operations/{Guid.NewGuid()}")).Problem(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task El_admin_no_crea_depositos()
    {
        var s = await Scenario.ProgresoAsync();
        var admin = await s.Api.AdminAsync();

        await (await admin.PostJson(s.Url("deposits"),
                new { phone = Seed.LinkedPhone, requestedAmount = 50_000, roundAmount = false, cashReceivedConfirmed = true },
                Http.Key()))
            .Problem(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task El_id_del_pago_ante_el_riel_sale_de_la_clave_para_que_un_reintento_recupere_el_mismo_pago()
    {
        var s = await Scenario.ProgresoAsync();
        var key = Http.Key();

        await (await s.DepositAsync(50_000, key: key)).Json(HttpStatusCode.Accepted);

        var sent = Assert.Single(s.Rail.Created);
        Assert.Equal(PaymentLog.PaymentId($"{s.AgentId}|operation", key), sent.IdempotencyKey);
    }

    [Fact]
    public async Task Cada_respuesta_del_riel_queda_en_la_bitacora_del_pago_aunque_no_cambie_el_estado()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        await s.PollAsync(id);
        await s.PollAsync(id);

        var db = s.Api.Service<ReliaDbContext>();
        var actions = await db.PaymentEvents.Where(e => e.Payment.OperationId == Guid.Parse(id))
            .OrderBy(e => e.Id).Select(e => e.Action).ToListAsync();
        Assert.Equal(["created", "polled", "polled"], actions);
    }
}
