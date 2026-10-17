using System.Net;
using System.Text.Json;

namespace Relia.Api.Tests.Support;

/// <summary>Un corresponsal con sesión iniciada sobre una API recién reiniciada.</summary>
public class Scenario
{
    public required ReliaApiFactory Api { get; init; }
    public required HttpClient Client { get; init; }
    public required string AgentId { get; init; }

    public FakeRailClient Rail => Api.Rail;

    /// <summary>"Miscelánea El Progreso": cupo 800.000, efectivo 350.000, comisión 12.500, billetera simulada.</summary>
    public static Task<Scenario> ProgresoAsync() => AsAsync("miscelanea@relia.dev");

    /// <summary>Rosa: todo en ceros y billetera real.</summary>
    public static Task<Scenario> RosaAsync() => AsAsync(Seed.RosaEmail);

    public static async Task<Scenario> AsAsync(string email, ReliaApiFactory? api = null)
    {
        api ??= await new ReliaApiFactory().ResetAsync();
        var client = await api.LoginAsync(email);
        var id = (await (await client.GetAsync("/me")).Json(HttpStatusCode.OK)).Str("agent.id")!;
        return new Scenario { Api = api, Client = client, AgentId = id };
    }

    public string Url(string path) => $"/agents/{AgentId}/{path}";

    public Task<HttpResponseMessage> DepositAsync(long amount, string phone = Seed.LinkedPhone, bool round = false,
        string? key = null) =>
        Client.PostJson(Url("deposits"),
            new { phone, requestedAmount = amount, roundAmount = round, cashReceivedConfirmed = true }, key ?? Http.Key());

    public Task<HttpResponseMessage> WithdrawAsync(long amount, string phone = Seed.LinkedPhone, bool round = false,
        string? key = null) =>
        Client.PostJson(Url("withdrawals"), new { phone, requestedAmount = amount, roundAmount = round }, key ?? Http.Key());

    public Task<HttpResponseMessage> TopUpAsync(string method, long amount, string? key = null) =>
        Client.PostJson(Url("quota-top-ups"), new { method, requestedAmount = amount }, key ?? Http.Key());

    /// <summary>Consulta la operación como el frontend: avanza el reloj 2 s antes, como el polling.</summary>
    public async Task<JsonElement> PollAsync(string operationId)
    {
        Api.Clock.Advance(TimeSpan.FromSeconds(2));
        return await (await Client.GetAsync($"/operations/{operationId}")).Json(HttpStatusCode.OK);
    }

    public async Task<JsonElement> AgentAsync() => await (await Client.GetAsync($"/agents/{AgentId}")).Json(HttpStatusCode.OK);

    public async Task<JsonElement> InboxAsync(string phone) =>
        await (await Api.CreateClient().GetAsync($"/demo/messages?phone={phone}")).Json(HttpStatusCode.OK);
}
