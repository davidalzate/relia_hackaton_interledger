using System.Net;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 10: recibos congelados de depósitos y retiros.</summary>
public class T10ReceiptTests
{
    private static async Task<(Scenario S, string OperationId)> CompletedDepositAsync()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        s.Rail.Complete(s.Rail.ForBackendPayment().Id);
        await s.PollAsync(id);
        return (s, id);
    }

    [Fact]
    public async Task El_recibo_trae_el_contenido_congelado_con_datos_enmascarados()
    {
        var (s, id) = await CompletedDepositAsync();

        var receipt = await (await s.Client.GetAsync($"/operations/{id}/receipt")).Json(HttpStatusCode.OK);

        Assert.Matches("^[0-9A-F]{12}$", receipt.Str("code"));
        Assert.Matches("^[0-9a-f]{64}$", receipt.Str("contentHash"));
        Assert.Equal(id, receipt.Str("operationId"));
        Assert.False(receipt.At("isCopy").GetBoolean());
        Assert.Equal("deposit", receipt.Str("data.type"));
        Assert.Equal("COP", receipt.Str("data.currency"));
        Assert.Equal("Miscelánea El Progreso", receipt.Str("data.merchant.name"));
        Assert.Equal("300 *** 0001", receipt.Str("data.customer.phoneMasked"));
        Assert.Equal("$ilp...test.dev/je***", receipt.Str("data.customer.walletAddressMasked"));
        Assert.Equal(50_000, receipt.Long("data.amounts.requestedAmount"));
        Assert.Equal(1_000, receipt.Long("data.amounts.fee"));
        Assert.Equal(49_000, receipt.Long("data.amounts.netAmount"));
        Assert.Equal("openPayments", receipt.Str("data.payment.rail"));
    }

    [Fact]
    public async Task Desde_la_segunda_emision_es_copia_con_el_mismo_contenido()
    {
        var (s, id) = await CompletedDepositAsync();

        var first = await (await s.Client.GetAsync($"/operations/{id}/receipt")).Json(HttpStatusCode.OK);
        var printed = await (await s.Client.GetAsync($"/operations/{id}/receipt?channel=print")).Json(HttpStatusCode.OK);

        Assert.True(printed.At("isCopy").GetBoolean());
        Assert.Equal(first.Str("code"), printed.Str("code"));
        Assert.Equal(first.Str("contentHash"), printed.Str("contentHash"));
    }

    [Fact]
    public async Task Una_operacion_no_completada_no_tiene_recibo()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;

        await (await s.Client.GetAsync($"/operations/{id}/receipt")).Problem(HttpStatusCode.Conflict, "operation_not_completed");
        await (await s.Client.PostJson($"/operations/{id}/receipt/send", new { channel = "sms" }))
            .Problem(HttpStatusCode.Conflict, "operation_not_completed");
    }

    [Fact]
    public async Task Enviar_el_recibo_lo_deja_en_el_telefono_del_usuario()
    {
        var (s, id) = await CompletedDepositAsync();
        await s.Client.GetAsync($"/operations/{id}/receipt");

        var delivery = await (await s.Client.PostJson($"/operations/{id}/receipt/send", new { channel = "sms" }))
            .Json(HttpStatusCode.Accepted);

        Assert.Equal("sms", delivery.Str("channel"));
        Assert.Equal("300 *** 0001", delivery.Str("sentTo"));
        Assert.True(delivery.At("isCopy").GetBoolean());
        var inbox = await s.InboxAsync(Seed.LinkedPhone);
        Assert.Contains(inbox.At("items").EnumerateArray(), m => m.Str("type") == "comprobante");
    }

    [Fact]
    public async Task El_admin_lee_recibos_y_otro_corresponsal_no()
    {
        var (s, id) = await CompletedDepositAsync();

        await (await (await s.Api.AdminAsync()).GetAsync($"/operations/{id}/receipt")).Json(HttpStatusCode.OK);
        await (await (await s.Api.RosaAsync()).GetAsync($"/operations/{id}/receipt"))
            .Problem(HttpStatusCode.Forbidden, "forbidden");
    }
}
