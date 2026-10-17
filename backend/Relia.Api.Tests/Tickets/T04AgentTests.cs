using System.Net;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 04: saldos e historial del corresponsal.</summary>
public class T04AgentTests
{
    private static async Task<(ReliaApiFactory Api, HttpClient Rosa, string RosaId)> RosaAsync()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        var rosa = await api.RosaAsync();
        var me = await (await rosa.GetAsync("/me")).Json(HttpStatusCode.OK);
        return (api, rosa, me.Str("agent.id")!);
    }

    private static async Task<string> OtherAgentIdAsync(ReliaApiFactory api)
    {
        var list = await (await (await api.AdminAsync()).GetAsync("/agents")).Json(HttpStatusCode.OK);
        return list.At("items").EnumerateArray().First(a => a.Str("name") != "Tienda Doña Rosa").Str("id")!;
    }

    [Fact]
    public async Task El_corresponsal_ve_sus_saldos_con_reservas_en_cero()
    {
        var (_, rosa, rosaId) = await RosaAsync();

        var agent = await (await rosa.GetAsync($"/agents/{rosaId}")).Json(HttpStatusCode.OK);

        Assert.Equal("Tienda Doña Rosa", agent.Str("name"));
        Assert.Equal("Rosa Gómez", agent.Str("managerName"));
        Assert.Equal("$ilp.interledger-test.dev/rosa-test", agent.Str("walletAddress"));
        foreach (var field in new[] { "quota", "quotaReserved", "cashOnHand", "cashReserved", "accruedCommission", "settledCommissionTotal" })
            Assert.Equal(0, agent.Long($"balances.{field}"));
    }

    [Fact]
    public async Task El_corresponsal_no_puede_leer_otro_corresponsal()
    {
        var (api, rosa, _) = await RosaAsync();
        var otherId = await OtherAgentIdAsync(api);

        await (await rosa.GetAsync($"/agents/{otherId}")).Problem(HttpStatusCode.Forbidden, "forbidden");
        await (await rosa.GetAsync($"/agents/{otherId}/history")).Problem(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task El_admin_lee_cualquier_corresponsal_y_un_id_inexistente_es_404()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        var admin = await api.AdminAsync();
        var otherId = await OtherAgentIdAsync(api);

        var other = await (await admin.GetAsync($"/agents/{otherId}")).Json(HttpStatusCode.OK);
        Assert.True(other.Long("balances.quota") > 0);

        await (await admin.GetAsync($"/agents/{Guid.NewGuid()}")).Problem(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Un_historial_vacio_devuelve_una_pagina_sin_cursor()
    {
        var (_, rosa, rosaId) = await RosaAsync();

        var page = await (await rosa.GetAsync($"/agents/{rosaId}/history?limit=3")).Json(HttpStatusCode.OK);

        Assert.Empty(page.At("items").EnumerateArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, page.At("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Un_limite_fuera_de_rango_es_validation_error()
    {
        var (_, rosa, rosaId) = await RosaAsync();

        await (await rosa.GetAsync($"/agents/{rosaId}/history?limit=0"))
            .Problem(HttpStatusCode.UnprocessableEntity, "validation_error");
    }
}
