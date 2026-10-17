using System.Net;
using Relia.Api.Services.Operations;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 13: el barrido de fondo avanza los pendientes aunque nadie los consulte.</summary>
public class T13SweepTests
{
    private static Task SweepAsync(Scenario s) => s.Api.Service<PaymentSweeper>().SweepAsync();

    private static async Task<string> StatusInHistoryAsync(Scenario s, string id)
    {
        var history = await (await s.Client.GetAsync(s.Url("history"))).Json(HttpStatusCode.OK);
        return history.At("items").EnumerateArray().Single(e => e.Str("operationId") == id).Str("status")!;
    }

    [Fact]
    public async Task Completa_un_pago_que_el_riel_ya_cerro_sin_que_el_frontend_consulte()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.WithdrawAsync(100_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        s.Rail.Complete(s.Rail.ForBackendPayment().Id);

        await SweepAsync(s);

        Assert.Equal("completed", await StatusInHistoryAsync(s, id));
        Assert.Equal(899_000, (await s.AgentAsync()).Long("balances.quota"));
    }

    [Fact]
    public async Task Vence_las_solicitudes_pasadas_de_plazo()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.WithdrawAsync(100_000)).Json(HttpStatusCode.Accepted)).Str("id")!;

        s.Api.Clock.Advance(TimeSpan.FromMinutes(5));
        await SweepAsync(s);

        Assert.Equal("expired", await StatusInHistoryAsync(s, id));
        Assert.Single(s.Rail.Cancelled);
    }

    [Fact]
    public async Task Si_el_riel_no_responde_la_operacion_sigue_pendiente_y_se_reintenta()
    {
        var s = await Scenario.ProgresoAsync();
        var id = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        s.Rail.Complete(s.Rail.ForBackendPayment().Id);

        s.Rail.Unavailable = true;
        await SweepAsync(s);
        Assert.Equal("pending", await StatusInHistoryAsync(s, id));

        s.Rail.Unavailable = false;
        await SweepAsync(s);
        Assert.Equal("completed", await StatusInHistoryAsync(s, id));
    }
}
