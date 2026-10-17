using System.Net;
using Relia.Api.Tests.Support;

namespace Relia.Api.Tests.Tickets;

/// <summary>Ticket 02: login y perfil de sesión.</summary>
public class T02AuthTests
{
    [Fact]
    public async Task El_corresponsal_inicia_sesion_y_ve_su_perfil_con_saldos()
    {
        var api = await new ReliaApiFactory().ResetAsync();
        var client = api.CreateClient();

        var login = await (await client.PostJson("/auth/login", new { email = Seed.RosaEmail, password = Seed.Password }))
            .Json(HttpStatusCode.OK);

        Assert.Equal("agent", login.Str("role"));
        Assert.True(login.Long("expiresIn") > 0);
        var agentId = login.Str("agentId");
        Assert.NotNull(agentId);

        var rosa = await api.RosaAsync();
        var me = await (await rosa.GetAsync("/me")).Json(HttpStatusCode.OK);
        Assert.Equal("agent", me.Str("role"));
        Assert.Equal(Seed.RosaEmail, me.Str("email"));
        Assert.Equal(agentId, me.Str("agent.id"));
        Assert.Equal("Tienda Doña Rosa", me.Str("agent.name"));
        Assert.Equal(0, me.Long("agent.balances.quota"));
        Assert.Equal(0, me.Long("agent.balances.cashOnHand"));
    }

    [Fact]
    public async Task El_admin_inicia_sesion_sin_corresponsal()
    {
        var api = await new ReliaApiFactory().ResetAsync();

        var login = await (await api.CreateClient().PostJson("/auth/login",
            new { email = Seed.AdminEmail, password = Seed.Password })).Json(HttpStatusCode.OK);
        Assert.Equal("admin", login.Str("role"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, login.At("agentId").ValueKind);

        var me = await (await (await api.AdminAsync()).GetAsync("/me")).Json(HttpStatusCode.OK);
        Assert.Equal("admin", me.Str("role"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, me.At("agent").ValueKind);
    }

    [Theory]
    [InlineData(Seed.RosaEmail, "otra-clave")]
    [InlineData("nadie@relia.dev", Seed.Password)]
    public async Task Credenciales_malas_responden_invalid_credentials(string email, string password)
    {
        var api = await new ReliaApiFactory().ResetAsync();

        await (await api.CreateClient().PostJson("/auth/login", new { email, password }))
            .Problem(HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    [Fact]
    public async Task Un_cuerpo_invalido_responde_validation_error_con_los_campos_en_camelCase()
    {
        var api = new ReliaApiFactory();

        var problem = await (await api.CreateClient().PostJson("/auth/login", new { email = "no-es-correo" }))
            .Problem(HttpStatusCode.UnprocessableEntity, "validation_error");

        var fields = problem.At("errors").EnumerateArray().Select(e => e.Str("field")).ToList();
        Assert.Contains("email", fields);
        Assert.Contains("password", fields);
    }

    [Fact]
    public async Task Sin_token_las_rutas_protegidas_responden_unauthorized()
    {
        var api = new ReliaApiFactory();

        await (await api.CreateClient().GetAsync("/me")).Problem(HttpStatusCode.Unauthorized, "unauthorized");
    }
}
