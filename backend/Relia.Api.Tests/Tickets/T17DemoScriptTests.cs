using System.Net;
using System.Text.RegularExpressions;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>
/// Ticket 17: el guion de demo de FLUJOS.md §12 de punta a punta, con el riel falso aprobando los pagos.
/// Todas las respuestas pasan por el validador del contrato (ContractCheckingHandler).
/// </summary>
public class T17DemoScriptTests
{
    private const string RosaPhone = "3100000001";

    [Fact]
    public async Task Guion_de_demo_completo()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        var demo = api.CreateClient();

        // 1. Entrar como Rosa y ver la Caja.
        var s = await Scenario.AsAsync(Seed.RosaEmail, api);
        Assert.Equal(0, (await s.AgentAsync()).Long("balances.quota"));
        await (await s.Client.GetAsync(s.Url("history?limit=3"))).Json(HttpStatusCode.OK);

        // 2. Asignar efectivo $300.000.
        await (await s.Client.PostJson(s.Url("cash-adjustments"), new { mode = "add", amount = 300_000 }, Http.Key()))
            .Json(HttpStatusCode.Created);

        // 3. Cargar cupo por Bre-B y simular el pago.
        var topUp = (await (await s.TopUpAsync("breB", 500_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        await (await demo.PostEmpty($"/demo/quota-top-ups/{topUp}/breb-payment")).Json(HttpStatusCode.OK);
        Assert.Equal("completed", (await s.PollAsync(topUp)).Str("status"));

        // 4. Depositar $50.000 al 3000000001.
        await (await s.Client.PostJson(s.Url("operations/preview"),
            new { type = "deposit", requestedAmount = 50_000, roundAmount = false })).Json(HttpStatusCode.OK);
        await (await s.Client.GetAsync($"/keys/{Seed.LinkedPhone}")).Json(HttpStatusCode.OK);
        var deposit = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        s.Rail.Complete(s.Rail.ForBackendPayment().Id);
        Assert.Equal("completed", (await s.PollAsync(deposit)).Str("status"));
        await (await s.Client.GetAsync($"/operations/{deposit}/receipt")).Json(HttpStatusCode.OK);

        // 5. Retirar $100.000: el usuario aprueba desde "Mi billetera" y se entrega el efectivo.
        await (await s.Client.PostJson(s.Url("operations/preview"),
            new { type = "withdrawal", requestedAmount = 100_000, roundAmount = false })).Json(HttpStatusCode.OK);
        var withdrawal = (await (await s.WithdrawAsync(100_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        var requests = await (await demo.GetAsync($"/demo/payment-requests?phone={Seed.LinkedPhone}")).Json(HttpStatusCode.OK);
        Assert.Single(requests.At("items").EnumerateArray());
        var railId = s.Rail.ForBackendPayment().Id;
        s.Rail.Approve(railId);
        s.Rail.Complete(railId);
        Assert.Equal("completed", (await s.PollAsync(withdrawal)).Str("status"));
        await (await s.Client.PostEmpty($"/operations/{withdrawal}/cash-handover")).Json(HttpStatusCode.OK);

        // 6. Depositar a un celular sin llave: invitación, vinculación con OTP y depósito.
        await (await s.Client.GetAsync($"/keys/{Seed.UnlinkedPhone}")).Problem(HttpStatusCode.NotFound, "key_not_found");
        await (await s.Client.PostJson($"/keys/{Seed.UnlinkedPhone}/invitations", new { }, Http.Key())).Json(HttpStatusCode.Created);
        var challenge = await (await demo.PostJson("/key-links",
                new { phone = Seed.UnlinkedPhone, walletAddress = "$ilp.interledger-test.dev/nuevo-usuario" }))
            .Json(HttpStatusCode.Created);
        var inbox = await (await demo.GetAsync($"/demo/messages?phone={Seed.UnlinkedPhone}")).Json(HttpStatusCode.OK);
        var code = Regex.Match(inbox.At("items").EnumerateArray().First(m => m.Str("type") == "otp").Str("content")!, @"\b\d{6}\b").Value;
        await (await demo.PostJson($"/key-links/{challenge.Str("challengeId")}/verify", new { code })).Json(HttpStatusCode.OK);
        await (await s.Client.GetAsync($"/keys/{Seed.UnlinkedPhone}")).Json(HttpStatusCode.OK);
        var second = (await (await s.DepositAsync(20_000, Seed.UnlinkedPhone)).Json(HttpStatusCode.Accepted)).Str("id")!;
        s.Rail.Complete(s.Rail.ForBackendPayment().Id);
        Assert.Equal("completed", (await s.PollAsync(second)).Str("status"));

        // 7. Historial y comprobante.
        var history = await (await s.Client.GetAsync(s.Url("history"))).Json(HttpStatusCode.OK);
        Assert.Equal(5, history.At("items").GetArrayLength());
        await (await s.Client.GetAsync($"/operations/{second}/receipt")).Json(HttpStatusCode.OK);

        // 8. Admin: totales, detalle de Rosa y pago de comisión.
        var admin = await api.AdminAsync();
        var list = await (await admin.GetAsync("/agents")).Json(HttpStatusCode.OK);
        Assert.Equal(170_000, list.At("items").EnumerateArray().Single(a => a.Str("name") == "Tienda Doña Rosa").Long("totalMoved"));
        await (await admin.GetAsync("/settlements?from=2026-10-16")).Json(HttpStatusCode.OK);
        await (await admin.GetAsync($"/agents/{s.AgentId}")).Json(HttpStatusCode.OK);
        await (await admin.GetAsync($"/agents/{s.AgentId}/history")).Json(HttpStatusCode.OK);
        var settlement = await (await admin.PostJson($"/agents/{s.AgentId}/settlements", new { }, Http.Key()))
            .Json(HttpStatusCode.Accepted);
        Assert.Equal(1_500, settlement.Long("amount"));
        s.Rail.Complete(s.Rail.ForBackendPayment().Id);
        api.Clock.Advance(TimeSpan.FromSeconds(2));
        var paid = await (await admin.GetAsync($"/settlements/{settlement.Str("id")}")).Json(HttpStatusCode.OK);
        Assert.Equal("paid", paid.Str("status"));

        var rosa = await s.AgentAsync();
        Assert.Equal(500_000 - 50_000 + 99_000 - 20_000, rosa.Long("balances.quota"));
        Assert.Equal(300_000 + 50_000 - 99_000 + 20_000, rosa.Long("balances.cashOnHand"));
        Assert.Equal(0, rosa.Long("balances.accruedCommission"));
        Assert.Equal(1_500, rosa.Long("balances.settledCommissionTotal"));
        Assert.Contains((await s.InboxAsync(RosaPhone)).At("items").EnumerateArray(), m => m.Str("type") == "aviso_comision");
    }

    [Fact]
    public void Cada_codigo_de_error_del_contrato_tiene_al_menos_una_prueba()
    {
        var contract = File.ReadAllText(Find("contracts/relia-api.openapi.yaml"));
        var errorCodes = Regex.Match(contract, @"ErrorCode:\s+type: string\s+description:[^\n]*\n\s+enum:\n((?:\s+- \w+\n)+)")
            .Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().TrimStart('-').Trim())
            .ToList();
        Assert.NotEmpty(errorCodes);

        var tests = string.Concat(Directory.GetFiles(Find("backend/Relia.Api.Tests/Tickets"), "*.cs").Select(File.ReadAllText));
        var untested = errorCodes.Where(c => !tests.Contains($"\"{c}\"")).ToList();

        Assert.True(untested.Count == 0, $"Códigos sin prueba: {string.Join(", ", untested)}");
    }

    private static string Find(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relative);
    }
}
