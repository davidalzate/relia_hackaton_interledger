using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Relia.Api.Rail;

namespace Relia.Api.Tests.Rail;

public class HttpRailClientTests
{
    private readonly FakeProxyHandler _proxy = new();
    private readonly IRailClient _client;

    public HttpRailClientTests()
    {
        var http = new HttpClient(_proxy) { BaseAddress = new Uri("http://proxy.test/") };
        _client = new HttpRailClient(http,
            Options.Create(new RailProxyOptions { BaseUrl = "http://proxy.test/", ApiKey = "secreto" }));
    }

    // Pago de ejemplo del contrato del proxy: 49.000 COP con escala 2.
    private const string OutgoingPaymentJson = """
        {
          "id": "6f1c1f0e-0000-4000-8000-000000000001",
          "idempotencyKey": "6f1c1f0e-0000-4000-8000-0000000000aa",
          "direction": "outgoing",
          "status": "processing",
          "sender": "$ilp.interledger-test.dev/relia-test1",
          "receiver": "$ilp.interledger-test.dev/jeffdev",
          "receiveAmount": { "value": "4900000", "assetCode": "COP", "assetScale": 2 },
          "debitAmount": null,
          "receivedAmount": null,
          "approval": null,
          "expiresAt": null,
          "failureCode": null,
          "failureDetail": null,
          "rail": { "incomingPaymentUrl": null, "quoteUrl": null, "outgoingPaymentUrl": null },
          "createdAt": "2026-10-16T15:00:00Z",
          "updatedAt": "2026-10-16T15:00:00Z",
          "completedAt": null,
          "version": 1
        }
        """;

    [Fact]
    public async Task Crear_un_pago_saliente_envia_el_monto_en_unidades_minimas_con_la_clave_y_la_api_key()
    {
        _proxy.RespondWith(HttpStatusCode.Created, OutgoingPaymentJson);
        var key = Guid.Parse("6f1c1f0e-0000-4000-8000-0000000000aa");

        var payment = await _client.CreatePaymentAsync(new CreateRailPayment(
            key, RailDirection.Outgoing, "$ilp.interledger-test.dev/jeffdev", 49_000));

        var sent = Assert.Single(_proxy.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/payments", sent.PathAndQuery);
        Assert.Equal("secreto", sent.Headers["X-Api-Key"]);
        Assert.Equal(key.ToString(), sent.Headers["Idempotency-Key"]);
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal("outgoing", body.RootElement.GetProperty("direction").GetString());
        Assert.Equal("$ilp.interledger-test.dev/jeffdev", body.RootElement.GetProperty("counterparty").GetString());
        var amount = body.RootElement.GetProperty("receiveAmount");
        Assert.Equal("4900000", amount.GetProperty("value").GetString());
        Assert.Equal("COP", amount.GetProperty("assetCode").GetString());
        Assert.Equal(2, amount.GetProperty("assetScale").GetInt32());

        Assert.Equal(Guid.Parse("6f1c1f0e-0000-4000-8000-000000000001"), payment.Id);
        Assert.Equal(RailPaymentStatus.Processing, payment.Status);
        Assert.Equal(49_000, payment.ReceiveAmount);
        Assert.Null(payment.DebitAmount);
        Assert.Null(payment.ApprovalUrl);
        Assert.Equal(1, payment.Version);
    }

    [Fact]
    public async Task Consultar_un_cobro_pendiente_trae_la_url_de_aprobacion_y_los_montos_en_pesos()
    {
        _proxy.RespondWith(HttpStatusCode.OK, """
            {
              "id": "6f1c1f0e-0000-4000-8000-000000000002",
              "idempotencyKey": "6f1c1f0e-0000-4000-8000-0000000000bb",
              "direction": "incoming",
              "status": "pending_approval",
              "sender": "$ilp.interledger-test.dev/jeffdev",
              "receiver": "$ilp.interledger-test.dev/relia-test1",
              "receiveAmount": { "value": "10100000", "assetCode": "COP", "assetScale": 2 },
              "debitAmount": { "value": "10150000", "assetCode": "COP", "assetScale": 2 },
              "receivedAmount": null,
              "approval": { "method": "redirect", "url": "https://auth.interledger-test.dev/interact/abc" },
              "expiresAt": "2026-10-16T15:05:00Z",
              "failureCode": null,
              "failureDetail": null,
              "rail": {
                "incomingPaymentUrl": "https://ilp.interledger-test.dev/incoming-payments/1",
                "quoteUrl": "https://ilp.interledger-test.dev/quotes/1",
                "outgoingPaymentUrl": null
              },
              "createdAt": "2026-10-16T15:00:00Z",
              "updatedAt": "2026-10-16T15:00:02Z",
              "completedAt": null,
              "version": 3
            }
            """);
        var id = Guid.Parse("6f1c1f0e-0000-4000-8000-000000000002");

        var payment = await _client.GetPaymentAsync(id);

        var sent = Assert.Single(_proxy.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal($"/payments/{id}", sent.PathAndQuery);
        Assert.Equal("secreto", sent.Headers["X-Api-Key"]);
        Assert.NotNull(payment);
        Assert.Equal(RailDirection.Incoming, payment.Direction);
        Assert.Equal(RailPaymentStatus.PendingApproval, payment.Status);
        Assert.Equal(101_000, payment.ReceiveAmount);
        Assert.Equal(101_500, payment.DebitAmount);
        Assert.Equal("https://auth.interledger-test.dev/interact/abc", payment.ApprovalUrl);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 15, 5, 0, TimeSpan.Zero), payment.ExpiresAt);
        Assert.Equal("https://ilp.interledger-test.dev/incoming-payments/1", payment.IncomingPaymentUrl);
        Assert.Equal(3, payment.Version);
    }

    [Fact]
    public async Task Consultar_un_pago_que_el_proxy_no_conoce_devuelve_null()
    {
        _proxy.RespondWith(HttpStatusCode.NotFound, Problem(404, "not_found"), "application/problem+json");

        Assert.Null(await _client.GetPaymentAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Un_pago_fallido_trae_el_codigo_y_el_detalle_del_riel()
    {
        _proxy.RespondWith(HttpStatusCode.OK, OutgoingPaymentJson
            .Replace("\"processing\"", "\"failed\"")
            .Replace("\"failureCode\": null", "\"failureCode\": \"insufficient_funds\"")
            .Replace("\"failureDetail\": null", "\"failureDetail\": \"saldo insuficiente\""));

        var payment = await _client.GetPaymentAsync(Guid.NewGuid());

        Assert.Equal(RailPaymentStatus.Failed, payment!.Status);
        Assert.Equal("insufficient_funds", payment.FailureCode);
        Assert.Equal("saldo insuficiente", payment.FailureDetail);
    }

    [Fact]
    public async Task Cancelar_un_cobro_pendiente_llama_al_proxy_y_devuelve_el_pago_cancelado()
    {
        _proxy.RespondWith(HttpStatusCode.OK, OutgoingPaymentJson.Replace("\"processing\"", "\"cancelled\""));
        var id = Guid.NewGuid();

        var payment = await _client.CancelPaymentAsync(id);

        var sent = Assert.Single(_proxy.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"/payments/{id}/cancel", sent.PathAndQuery);
        Assert.Equal("secreto", sent.Headers["X-Api-Key"]);
        Assert.Equal(RailPaymentStatus.Cancelled, payment.Status);
    }

    [Fact]
    public async Task Cancelar_un_cobro_ya_aprobado_avisa_que_no_se_puede_cancelar()
    {
        _proxy.RespondWith(HttpStatusCode.Conflict, Problem(409, "payment_not_cancellable"), "application/problem+json");

        await Assert.ThrowsAsync<RailPaymentNotCancellableException>(() => _client.CancelPaymentAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Resolver_un_pointer_existente_devuelve_su_moneda_y_escala()
    {
        _proxy.RespondWith(HttpStatusCode.OK, """
            {
              "pointer": "$ilp.interledger-test.dev/jeffdev",
              "url": "https://ilp.interledger-test.dev/jeffdev",
              "assetCode": "COP",
              "assetScale": 2
            }
            """);

        var wallet = await _client.ResolveWalletAddressAsync("$ilp.interledger-test.dev/jeffdev");

        var sent = Assert.Single(_proxy.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("/wallet-addresses?pointer=%24ilp.interledger-test.dev%2Fjeffdev", sent.PathAndQuery);
        Assert.Equal("secreto", sent.Headers["X-Api-Key"]);
        Assert.Equal(new RailWalletAddress("$ilp.interledger-test.dev/jeffdev",
            "https://ilp.interledger-test.dev/jeffdev", "COP", 2), wallet);
    }

    [Fact]
    public async Task Resolver_un_pointer_que_no_existe_en_el_riel_devuelve_null()
    {
        _proxy.RespondWith(HttpStatusCode.NotFound, Problem(404, "wallet_not_found"), "application/problem+json");

        Assert.Null(await _client.ResolveWalletAddressAsync("$ilp.interledger-test.dev/nadie"));
    }

    [Theory]
    [InlineData(422, "same_wallet")]
    [InlineData(422, "unsupported_asset")]
    [InlineData(409, "idempotency_conflict")]
    public async Task Un_rechazo_del_proxy_conserva_su_codigo_para_que_el_servicio_decida(int status, string code)
    {
        _proxy.RespondWith((HttpStatusCode)status, Problem(status, code), "application/problem+json");

        var error = await Assert.ThrowsAsync<RailRejectedException>(() => _client.CreatePaymentAsync(AnyOutgoing()));

        Assert.Equal(status, error.Status);
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task Un_cobro_incoming_envia_el_plazo_de_aprobacion()
    {
        _proxy.RespondWith(HttpStatusCode.Created, OutgoingPaymentJson);
        var expiresAt = new DateTimeOffset(2026, 10, 16, 15, 5, 0, TimeSpan.Zero);

        await _client.CreatePaymentAsync(new CreateRailPayment(
            Guid.NewGuid(), RailDirection.Incoming, "$ilp.interledger-test.dev/jeffdev", 101_000, expiresAt,
            "Retiro OP-000123 · Tienda Doña Rosa"));

        using var body = JsonDocument.Parse(_proxy.Requests.Single().Body!);
        Assert.Equal("incoming", body.RootElement.GetProperty("direction").GetString());
        Assert.Equal(expiresAt, body.RootElement.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal("Retiro OP-000123 · Tienda Doña Rosa", body.RootElement.GetProperty("description").GetString());
    }

    private static CreateRailPayment AnyOutgoing() =>
        new(Guid.NewGuid(), RailDirection.Outgoing, "$ilp.interledger-test.dev/jeffdev", 49_000);

    private static string Problem(int status, string code) =>
        $$"""{ "type": "about:blank", "title": "x", "status": {{status}}, "code": "{{code}}" }""";

    [Theory]
    [InlineData("relia_grant_unavailable")]
    [InlineData("rail_unavailable")]
    public async Task Un_503_del_proxy_es_riel_no_disponible_y_llega_al_frontend_como_503(string code)
    {
        _proxy.RespondWith(HttpStatusCode.ServiceUnavailable, Problem(503, code), "application/problem+json");

        var error = await Assert.ThrowsAsync<RailUnavailableException>(() => _client.CreatePaymentAsync(AnyOutgoing()));

        Assert.Equal(503, error.Status);
        Assert.Equal("rail_unavailable", error.Code);
    }

    public static TheoryData<Exception> NetworkFailures() => new()
    {
        new HttpRequestException("Connection refused"),
        new TaskCanceledException("timeout", new TimeoutException()),
    };

    [Theory]
    [MemberData(nameof(NetworkFailures))]
    public async Task Si_el_proxy_no_responde_es_riel_no_disponible(Exception failure)
    {
        _proxy.Throw(failure);

        await Assert.ThrowsAsync<RailUnavailableException>(() => _client.CreatePaymentAsync(AnyOutgoing()));
    }

    [Fact]
    public async Task Cancelar_desde_quien_llama_no_se_confunde_con_riel_no_disponible()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _proxy.Throw(new TaskCanceledException("cancelado", null, cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _client.CreatePaymentAsync(AnyOutgoing(), cts.Token));
    }
}
