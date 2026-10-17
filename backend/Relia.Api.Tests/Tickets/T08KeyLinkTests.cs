using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 08: vinculación pública de una llave con OTP.</summary>
public class T08KeyLinkTests
{
    private const string NewWallet = "$ilp.interledger-test.dev/nuevo-usuario";

    private static async Task<(ReliaApiFactory Api, HttpClient Client)> PublicAsync()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        return (api, api.CreateClient());
    }

    private static async Task<JsonElement> StartAsync(HttpClient client, string phone = Seed.UnlinkedPhone,
        string wallet = NewWallet) =>
        await (await client.PostJson("/key-links", new { phone, walletAddress = wallet })).Json(HttpStatusCode.Created);

    /// <summary>Lee el último OTP que llegó al teléfono simulado.</summary>
    private static async Task<string> OtpAsync(HttpClient client, string phone = Seed.UnlinkedPhone)
    {
        var inbox = await (await client.GetAsync($"/demo/messages?phone={phone}")).Json(HttpStatusCode.OK);
        var otp = inbox.At("items").EnumerateArray().First(m => m.Str("type") == "otp");
        return Regex.Match(otp.Str("content")!, @"\b\d{6}\b").Value;
    }

    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, JsonElement challenge, string code) =>
        client.PostJson($"/key-links/{challenge.Str("challengeId")}/verify", new { code });

    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";

    [Fact]
    public async Task Con_el_codigo_correcto_el_celular_queda_vinculado()
    {
        var (api, client) = await PublicAsync();

        var challenge = await StartAsync(client);
        Assert.Equal(5, challenge.Long("maxAttempts"));
        Assert.Equal(ReliaApiFactory.Start.AddMinutes(5), challenge.At("expiresAt").GetDateTimeOffset());

        var link = await (await VerifyAsync(client, challenge, await OtpAsync(client))).Json(HttpStatusCode.OK);
        Assert.Equal(Seed.UnlinkedPhone, link.Str("phone"));
        Assert.Equal(NewWallet, link.Str("walletAddress"));

        var rosa = await api.RosaAsync();
        var key = await (await rosa.GetAsync($"/keys/{Seed.UnlinkedPhone}")).Json(HttpStatusCode.OK);
        Assert.Equal("$ilp...test.dev/nu***", key.Str("walletAddressMasked"));
    }

    [Theory]
    [InlineData(Seed.LinkedPhone, NewWallet, HttpStatusCode.Conflict, "key_already_linked")]
    [InlineData("3100000001", NewWallet, HttpStatusCode.Conflict, "phone_reserved")]
    [InlineData(Seed.UnlinkedPhone, "$ilp.interledger-test.dev/rosa-test", HttpStatusCode.Conflict, "phone_reserved")]
    [InlineData(Seed.UnlinkedPhone, FakeRailClient.ReliaWallet, HttpStatusCode.Conflict, "phone_reserved")]
    [InlineData(Seed.UnlinkedPhone, "$ilp.interledger-test.dev/jeffdev", HttpStatusCode.Conflict, "wallet_already_linked")]
    [InlineData(Seed.UnlinkedPhone, "$ilp.interledger-test.dev/no-existe", HttpStatusCode.UnprocessableEntity, "wallet_not_found")]
    public async Task Iniciar_valida_celular_y_billetera_antes_de_enviar_el_codigo(string phone, string wallet,
        HttpStatusCode status, string code)
    {
        var (_, client) = await PublicAsync();

        await (await client.PostJson("/key-links", new { phone, walletAddress = wallet })).Problem(status, code);

        var inbox = await (await client.GetAsync($"/demo/messages?phone={phone}")).Json(HttpStatusCode.OK);
        Assert.DoesNotContain(inbox.At("items").EnumerateArray(), m => m.Str("type") == "otp");
    }

    [Fact]
    public async Task Si_el_riel_no_responde_al_iniciar_es_503()
    {
        var (api, client) = await PublicAsync();
        api.Rail.Unavailable = true;

        await (await client.PostJson("/key-links", new { phone = Seed.UnlinkedPhone, walletAddress = NewWallet }))
            .Problem(HttpStatusCode.ServiceUnavailable, "rail_unavailable");
    }

    [Fact]
    public async Task Un_codigo_incorrecto_descuenta_intentos_hasta_agotarlos()
    {
        var (_, client) = await PublicAsync();
        var challenge = await StartAsync(client);
        var wrong = Wrong(await OtpAsync(client));

        var first = await (await VerifyAsync(client, challenge, wrong)).Problem(HttpStatusCode.UnprocessableEntity, "invalid_otp");
        Assert.Equal(4, first.Long("attemptsRemaining"));

        for (var i = 0; i < 3; i++) await VerifyAsync(client, challenge, wrong);
        await (await VerifyAsync(client, challenge, wrong)).Problem(HttpStatusCode.UnprocessableEntity, "otp_attempts_exceeded");
        await (await VerifyAsync(client, challenge, await OtpAsync(client)))
            .Problem(HttpStatusCode.UnprocessableEntity, "otp_attempts_exceeded");
    }

    [Fact]
    public async Task Pasados_5_minutos_el_codigo_vence()
    {
        var (api, client) = await PublicAsync();
        var challenge = await StartAsync(client);
        var code = await OtpAsync(client);

        api.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        await (await VerifyAsync(client, challenge, code)).Problem(HttpStatusCode.UnprocessableEntity, "otp_expired");
    }

    [Fact]
    public async Task Reenviar_el_codigo_invalida_el_desafio_anterior()
    {
        var (_, client) = await PublicAsync();
        var first = await StartAsync(client);
        var firstCode = await OtpAsync(client);

        var second = await StartAsync(client);

        await (await VerifyAsync(client, first, firstCode)).Problem(HttpStatusCode.UnprocessableEntity, "otp_expired");
        await (await VerifyAsync(client, second, await OtpAsync(client))).Json(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Si_otro_celular_vinculo_la_billetera_mientras_tanto_verificar_responde_el_mismo_409()
    {
        var (_, client) = await PublicAsync();
        var mine = await StartAsync(client);
        var myCode = await OtpAsync(client);

        var other = await StartAsync(client, "3000000003");
        await (await VerifyAsync(client, other, await OtpAsync(client, "3000000003"))).Json(HttpStatusCode.OK);

        await (await VerifyAsync(client, mine, myCode)).Problem(HttpStatusCode.Conflict, "wallet_already_linked");
    }

    [Fact]
    public async Task Un_desafio_inexistente_es_404()
    {
        var (_, client) = await PublicAsync();

        await (await client.PostJson($"/key-links/{Guid.NewGuid()}/verify", new { code = "123456" }))
            .Problem(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Demasiados_codigos_seguidos_para_el_mismo_celular_responden_rate_limited()
    {
        var (_, client) = await PublicAsync();
        for (var i = 0; i < 5; i++) await StartAsync(client);

        var response = await client.PostJson("/key-links", new { phone = Seed.UnlinkedPhone, walletAddress = NewWallet });

        await response.Problem(HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }
}
