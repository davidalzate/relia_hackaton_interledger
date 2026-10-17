using System.Net;
using System.Text.Json;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 14: panel admin con filas y totales de la red en una llamada.</summary>
public class T14AdminTests
{
    private static JsonElement Row(JsonElement list, string name) =>
        list.At("items").EnumerateArray().Single(a => a.Str("name") == name);

    [Fact]
    public async Task Lista_los_corresponsales_sembrados_con_su_billetera_y_saldos()
    {
        var api = await new ReliaApiFactory().ResetAsync();

        var list = await (await (await api.AdminAsync()).GetAsync("/agents")).Json(HttpStatusCode.OK);

        Assert.Equal(4, list.At("items").GetArrayLength());
        var rosa = Row(list, "Tienda Doña Rosa");
        Assert.False(rosa.At("walletSimulated").GetBoolean());
        Assert.Equal(0, rosa.Long("quota"));
        var progreso = Row(list, "Miscelánea El Progreso");
        Assert.True(progreso.At("walletSimulated").GetBoolean());
        Assert.Equal(800_000, progreso.Long("quota"));
        Assert.Equal(12_500, progreso.Long("accruedCommission"));
        Assert.Equal(12_500 + 21_000 + 6_500, list.Long("totals.commissionPayable"));
    }

    [Fact]
    public async Task Los_montos_movidos_cuentan_solo_depositos_y_retiros_completados_con_el_monto_solicitado()
    {
        var s = await Scenario.ProgresoAsync();
        var deposit = (await (await s.DepositAsync(50_000)).Json(HttpStatusCode.Accepted)).Str("id")!;
        s.Rail.Complete(s.Rail.ForBackendPayment().Id);
        await s.PollAsync(deposit);
        var withdrawal = (await (await s.WithdrawAsync(100_000, round: true)).Json(HttpStatusCode.Accepted)).Str("id")!;
        s.Rail.Complete(s.Rail.ForBackendPayment().Id);
        await s.PollAsync(withdrawal);
        await s.WithdrawAsync(20_000); // pendiente: no cuenta

        var list = await (await (await s.Api.AdminAsync()).GetAsync("/agents")).Json(HttpStatusCode.OK);

        var row = Row(list, "Miscelánea El Progreso");
        Assert.Equal(2, row.Long("operations"));
        Assert.Equal(1, row.Long("deposits.count"));
        Assert.Equal(50_000, row.Long("deposits.amount"));
        Assert.Equal(100_000, row.Long("withdrawals.amount"));
        Assert.Equal(150_000, row.Long("totalMoved"));
        Assert.Equal(2, list.Long("totals.totalMoved.count"));
        Assert.Equal(150_000, list.Long("totals.totalMoved.amount"));
    }

    [Fact]
    public async Task Un_corresponsal_no_ve_el_panel_admin()
    {
        var api = await new ReliaApiFactory().ResetAsync();

        await (await (await api.RosaAsync()).GetAsync("/agents")).Problem(HttpStatusCode.Forbidden, "forbidden");
    }
}
