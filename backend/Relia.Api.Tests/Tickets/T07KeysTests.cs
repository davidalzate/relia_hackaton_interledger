using System.Net;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 07: consulta de llave, invitación por WhatsApp y bandeja del teléfono simulado.</summary>
public class T07KeysTests
{
    [Fact]
    public async Task Un_celular_vinculado_devuelve_su_billetera_enmascarada()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        var rosa = await api.RosaAsync();

        var key = await (await rosa.GetAsync($"/keys/{Seed.LinkedPhone}")).Json(HttpStatusCode.OK);

        Assert.Equal(Seed.LinkedPhone, key.Str("phone"));
        Assert.Equal("$ilp...test.dev/je***", key.Str("walletAddressMasked"));
    }

    [Fact]
    public async Task Un_celular_sin_llave_responde_key_not_found()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        var rosa = await api.RosaAsync();

        await (await rosa.GetAsync($"/keys/{Seed.UnlinkedPhone}")).Problem(HttpStatusCode.NotFound, "key_not_found");
    }

    [Fact]
    public async Task La_invitacion_deja_el_whatsapp_en_la_bandeja_con_el_enlace_a_la_pagina_publica()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        var rosa = await api.RosaAsync();

        var invitation = await (await rosa.PostJson($"/keys/{Seed.UnlinkedPhone}/invitations", new { }, Http.Key()))
            .Json(HttpStatusCode.Created);

        Assert.Equal(Seed.UnlinkedPhone, invitation.Str("phone"));
        Assert.Contains("relia.com/selectkey?phone=3000000002", invitation.Str("message"));
        Assert.StartsWith("https://wa.me/573000000002?text=", invitation.Str("waLink"));

        var inbox = await (await api.CreateClient().GetAsync($"/demo/messages?phone={Seed.UnlinkedPhone}"))
            .Json(HttpStatusCode.OK);
        var message = Assert.Single(inbox.At("items").EnumerateArray());
        Assert.Equal("whatsapp", message.Str("channel"));
        Assert.Equal("invitacion_wallet", message.Str("type"));
        Assert.Contains("selectkey?phone=3000000002", message.Str("link"));
        Assert.False(message.At("read").GetBoolean());
        Assert.Equal(1, inbox.Long("unreadCount"));
    }

    [Fact]
    public async Task Invitar_un_celular_que_ya_tiene_llave_responde_key_already_linked()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        var rosa = await api.RosaAsync();

        await (await rosa.PostJson($"/keys/{Seed.LinkedPhone}/invitations", new { }, Http.Key()))
            .Problem(HttpStatusCode.Conflict, "key_already_linked");
    }

    [Fact]
    public async Task Abrir_el_telefono_marca_los_mensajes_como_leidos()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        var rosa = await api.RosaAsync();
        await rosa.PostJson($"/keys/{Seed.UnlinkedPhone}/invitations", new { }, Http.Key());
        var demo = api.CreateClient();

        var read = await demo.PostJson("/demo/messages/read", new { phone = Seed.UnlinkedPhone });
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        var inbox = await (await demo.GetAsync($"/demo/messages?phone={Seed.UnlinkedPhone}")).Json(HttpStatusCode.OK);
        Assert.Equal(0, inbox.Long("unreadCount"));
        Assert.True(inbox.At("items")[0].At("read").GetBoolean());
    }

    [Fact]
    public async Task Sin_DEMO_MODE_las_rutas_demo_responden_404()
    {
        var api = new ReliaApiFactory { DemoMode = false };

        await (await api.CreateClient().GetAsync("/demo/messages")).Problem(HttpStatusCode.NotFound, "not_found");
        await (await api.CreateClient().PostEmpty("/demo/reset")).Problem(HttpStatusCode.NotFound, "not_found");
    }
}
