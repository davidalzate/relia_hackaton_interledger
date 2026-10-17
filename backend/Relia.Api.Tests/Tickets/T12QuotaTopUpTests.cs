using System.Net;
using Relia.Api.Rail;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 12: carga de cupo. Rosa: todo en ceros, celular 3100000001, billetera rosa-test.</summary>
public class T12QuotaTopUpTests
{
    private const string RosaPhone = "3100000001";

    [Fact]
    public async Task Bre_B_devuelve_un_QR_de_10_minutos_y_al_simular_el_pago_sube_el_cupo()
    {
        var s = await Scenario.RosaAsync();

        var operation = await (await s.TopUpAsync("breB", 100_000)).Json(HttpStatusCode.Accepted);

        Assert.Equal("quotaTopUp", operation.Str("type"));
        Assert.Equal("breB", operation.Str("topUpMethod"));
        Assert.StartsWith("breb://pay?ref=OP-", operation.Str("qrPayload"));
        Assert.Equal(ReliaApiFactory.Start.AddMinutes(10), operation.At("expiresAt").GetDateTimeOffset());
        Assert.Equal(0, operation.Long("fee.total"));
        Assert.Equal(100_000, operation.Long("netAmount"));
        Assert.Equal(100_000, operation.Long("chargedAmount"));
        Assert.Empty(s.Rail.Created);

        var paid = await (await s.Api.CreateClient().PostEmpty($"/demo/quota-top-ups/{operation.Str("id")}/breb-payment"))
            .Json(HttpStatusCode.OK);

        Assert.Equal("completed", paid.Str("status"));
        Assert.Equal(0, paid.Long("balances.quota.before"));
        Assert.Equal(100_000, paid.Long("balances.quota.after"));
        Assert.Equal(100_000, (await s.AgentAsync()).Long("balances.quota"));
        await (await s.Client.GetAsync($"/operations/{operation.Str("id")}/receipt"))
            .Problem(HttpStatusCode.Conflict, "receipt_not_available");

        var history = await (await s.Client.GetAsync(s.Url("history?kind=quotaTopUp"))).Json(HttpStatusCode.OK);
        Assert.Equal("completed", Assert.Single(history.At("items").EnumerateArray()).Str("status"));
    }

    [Fact]
    public async Task Simular_el_pago_de_algo_que_no_es_una_carga_Bre_B_pendiente_es_409()
    {
        var s = await Scenario.RosaAsync();
        var demo = s.Api.CreateClient();
        var id = (await (await s.TopUpAsync("breB", 100_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        await demo.PostEmpty($"/demo/quota-top-ups/{id}/breb-payment");

        Assert.Equal(HttpStatusCode.Conflict, (await demo.PostEmpty($"/demo/quota-top-ups/{id}/breb-payment")).StatusCode);
        await (await demo.PostEmpty($"/demo/quota-top-ups/{Guid.NewGuid()}/breb-payment"))
            .Problem(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Pasados_10_minutos_el_QR_vence()
    {
        var s = await Scenario.RosaAsync();
        var id = (await (await s.TopUpAsync("breB", 100_000)).Json(HttpStatusCode.Accepted)).Str("id")!;

        s.Api.Clock.Advance(TimeSpan.FromMinutes(10));
        var operation = await s.PollAsync(id);

        Assert.Equal("expired", operation.Str("status"));
        Assert.Equal("request_expired", operation.Str("failureCode"));
    }

    [Fact]
    public async Task Cancelar_una_carga_Bre_B_pendiente()
    {
        var s = await Scenario.RosaAsync();
        var id = (await (await s.TopUpAsync("breB", 100_000)).Json(HttpStatusCode.Accepted)).Str("id")!;

        var operation = await (await s.Client.PostEmpty($"/operations/{id}/cancel")).Json(HttpStatusCode.OK);

        Assert.Equal("cancelled", operation.Str("status"));
    }

    [Fact]
    public async Task Por_Wallet_le_cobra_a_la_billetera_del_corresponsal_y_aparece_en_Mi_billetera()
    {
        var s = await Scenario.RosaAsync();

        var operation = await (await s.TopUpAsync("wallet", 200_000)).Json(HttpStatusCode.Accepted);

        Assert.Equal("wallet", operation.Str("topUpMethod"));
        Assert.NotNull(operation.Str("approvalUrl"));
        Assert.Equal(ReliaApiFactory.Start.AddMinutes(5), operation.At("expiresAt").GetDateTimeOffset());
        var sent = Assert.Single(s.Rail.Created);
        Assert.Equal(RailDirection.Incoming, sent.Direction);
        Assert.Equal("$ilp.interledger-test.dev/rosa-test", sent.Counterparty);
        Assert.Equal(200_000, sent.ReceiveAmount);

        var requests = await (await s.Api.CreateClient().GetAsync($"/demo/payment-requests?phone={RosaPhone}"))
            .Json(HttpStatusCode.OK);
        Assert.Equal("quotaTopUp", Assert.Single(requests.At("items").EnumerateArray()).Str("type"));
        var inbox = await s.InboxAsync(RosaPhone);
        Assert.Contains(inbox.At("items").EnumerateArray(), m => m.Str("type") == "solicitud_pago");

        s.Rail.Complete(s.Rail.ForBackendPayment().Id);
        Assert.Equal("completed", (await s.PollAsync(operation.Str("id")!)).Str("status"));
        Assert.Equal(200_000, (await s.AgentAsync()).Long("balances.quota"));
    }

    [Fact]
    public async Task El_admin_no_carga_cupo()
    {
        var s = await Scenario.RosaAsync();
        var admin = await s.Api.AdminAsync();

        await (await admin.PostJson(s.Url("quota-top-ups"), new { method = "breB", requestedAmount = 1_000 }, Http.Key()))
            .Problem(HttpStatusCode.Forbidden, "forbidden");
    }
}
