using System.Net;
using Mediateca.Api.Auth;
using Mediateca.Api.Users;
using Mediateca.Infrastructure.Identity;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Mi cuenta (nombre visible y contraseña) y la vida de las sesiones: cambiar la contraseña o el rol, o borrar al
/// usuario, cierra sus sesiones abiertas; la sesión desde la que se cambia la propia contraseña sigue.
/// </summary>
public sealed class AccountAndSessionTests(MediatecaApiFactorySesionAlInstante factory) : IClassFixture<MediatecaApiFactorySesionAlInstante>, IAsyncLifetime
{
    public Task InitializeAsync() => TestUsers.EnsureAdminAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string current, string next) =>
        client.PutJsonAsync("/api/me/password", new ChangePasswordRequest(current, next));

    [Fact]
    public async Task Restablecer_la_contrasena_cierra_las_sesiones_abiertas_del_usuario()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("sesiones"));
        using var phone = await TestUsers.LoginAsync(factory, user.UserName);
        using var laptop = await TestUsers.LoginAsync(factory, user.UserName);
        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(phone));
        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(laptop));

        var reset = await admin.PutJsonAsync($"/api/admin/users/{user.Id}/password", new ResetPasswordRequest("clave-nueva-larga"));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await UserTestSupport.SessionStatusAsync(phone));
        Assert.Equal(HttpStatusCode.Unauthorized, await UserTestSupport.SessionStatusAsync(laptop));
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/auth/me")).StatusCode);

        // La sesión del administrador no se tocó.
        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(admin));
    }

    [Fact]
    public async Task Cambiar_el_rol_cierra_las_sesiones_y_el_rol_nuevo_rige_al_volver_a_entrar()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("ascenso"));
        using var before = await TestUsers.LoginAsync(factory, user.UserName);
        Assert.Equal(HttpStatusCode.Forbidden, (await before.GetAsync("/api/admin/users")).StatusCode);

        var promoted = await UserTestSupport.UpdateAsync(admin, user.Id, user.DisplayName, "Admin");
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await UserTestSupport.SessionStatusAsync(before));
        using var asAdmin = await TestUsers.LoginAsync(factory, user.UserName);
        Assert.Equal(HttpStatusCode.OK, (await asAdmin.GetAsync("/api/admin/users")).StatusCode);

        // Y al revés: quitarle el rol cierra la sesión de administrador y ya no entra a la administración.
        var demoted = await UserTestSupport.UpdateAsync(admin, user.Id, user.DisplayName, "Usuario");
        Assert.Equal(HttpStatusCode.OK, demoted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await asAdmin.GetAsync("/api/admin/users")).StatusCode);
        using var asUser = await TestUsers.LoginAsync(factory, user.UserName);
        Assert.Equal(HttpStatusCode.Forbidden, (await asUser.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Borrar_a_un_usuario_cierra_sus_sesiones()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("adios"));
        using var session = await TestUsers.LoginAsync(factory, user.UserName);
        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(session));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/users/{user.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await UserTestSupport.SessionStatusAsync(session));
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Cambiar_el_nombre_o_las_bibliotecas_no_cierra_la_sesion()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await UserTestSupport.CreateEmptyLibraryAsync(factory, admin);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("estable"));
        using var session = await TestUsers.LoginAsync(factory, user.UserName);

        var edited = await UserTestSupport.UpdateAsync(admin, user.Id, "Otro nombre", "Usuario", [library.Id]);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(session));
        Assert.Equal("Otro nombre", (await session.GetJsonAsync<UserResponse>("/api/auth/me"))!.DisplayName);
    }

    [Fact]
    public async Task Cambiar_la_propia_contrasena_conserva_la_sesion_actual_y_cierra_las_demas()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("propia"));
        using var current = await TestUsers.LoginAsync(factory, user.UserName);
        using var other = await TestUsers.LoginAsync(factory, user.UserName);

        var changed = await ChangePasswordAsync(current, TestUsers.Password, "clave-nueva-larga");

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(current));
        Assert.Equal(HttpStatusCode.Unauthorized, await UserTestSupport.SessionStatusAsync(other));
        Assert.Equal(HttpStatusCode.Unauthorized, (await UserTestSupport.TryLoginAsync(factory, user.UserName, TestUsers.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UserTestSupport.TryLoginAsync(factory, user.UserName, "clave-nueva-larga")).StatusCode);

        // La sesión que sigue puede seguir escribiendo (el token XSRF sigue siendo válido).
        var again = await ChangePasswordAsync(current, "clave-nueva-larga", "clave-mas-nueva");
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(current));
    }

    [Fact]
    public async Task Con_la_contrasena_actual_equivocada_no_se_cambia_nada_ni_se_cierra_la_sesion()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("equivocada"));
        using var current = await TestUsers.LoginAsync(factory, user.UserName);
        using var other = await TestUsers.LoginAsync(factory, user.UserName);

        var response = await ChangePasswordAsync(current, "no-es-la-clave", "clave-nueva-larga");

        // 400 y no 401: Angular cierra la sesión ante un 401, y esta sesión es buena.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await UserTestSupport.ErrorsAsync(response);
        Assert.Equal("La contraseña no es correcta.", Assert.Single(errors["currentPassword"]));
        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(current));
        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(other));
        Assert.Equal(HttpStatusCode.NoContent, (await UserTestSupport.TryLoginAsync(factory, user.UserName, TestUsers.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UserTestSupport.TryLoginAsync(factory, user.UserName, "clave-nueva-larga")).StatusCode);
    }

    [Fact]
    public async Task Cambiar_la_contrasena_valida_los_datos_en_espanol()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("validar"));
        using var session = await TestUsers.LoginAsync(factory, user.UserName);

        var empty = await ChangePasswordAsync(session, "", "");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var emptyErrors = await UserTestSupport.ErrorsAsync(empty);
        Assert.Equal("Escribe tu contraseña actual.", Assert.Single(emptyErrors["currentPassword"]));
        Assert.Equal("Escribe la contraseña nueva.", Assert.Single(emptyErrors["newPassword"]));

        var same = await ChangePasswordAsync(session, TestUsers.Password, TestUsers.Password);
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        Assert.Contains("distinta de la actual", Assert.Single((await UserTestSupport.ErrorsAsync(same))["newPassword"]));

        var tooShort = await ChangePasswordAsync(session, TestUsers.Password, "corta");
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Contains("al menos 8 caracteres", Assert.Single((await UserTestSupport.ErrorsAsync(tooShort))["newPassword"]));

        // Nada de lo anterior cambió la contraseña ni cerró la sesión.
        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(session));
        Assert.Equal(HttpStatusCode.NoContent, (await UserTestSupport.TryLoginAsync(factory, user.UserName, TestUsers.Password)).StatusCode);
    }

    [Fact]
    public async Task Mi_cuenta_muestra_y_cambia_el_nombre_visible()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var user = await UserTestSupport.CreateUserAsync(
            admin, UserTestSupport.UniqueUserName("cuenta"), displayName: "Nombre original");
        using var session = await TestUsers.LoginAsync(factory, user.UserName);

        var account = (await session.GetJsonAsync<UserResponse>("/api/me/account"))!;
        Assert.Equal(user.Id, account.Id);
        Assert.Equal(user.UserName, account.UserName);
        Assert.Equal("Nombre original", account.DisplayName);
        Assert.False(account.IsAdmin);

        var renamed = await session.PutJsonAsync("/api/me/account", new UpdateAccountRequest("  Nombre nuevo  "));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Nombre nuevo", (await renamed.ReadJsonAsync<UserResponse>())!.DisplayName);
        Assert.Equal("Nombre nuevo", (await session.GetJsonAsync<UserResponse>("/api/auth/me"))!.DisplayName);
        Assert.Equal("Nombre nuevo", (await admin.GetJsonAsync<AdminUserResponse>($"/api/admin/users/{user.Id}"))!.DisplayName);
        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(session));

        var empty = await session.PutJsonAsync("/api/me/account", new UpdateAccountRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("Escribe tu nombre.", Assert.Single((await UserTestSupport.ErrorsAsync(empty))["displayName"]));

        var tooLong = await session.PutJsonAsync("/api/me/account", new UpdateAccountRequest(new string('x', 101)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Mi_cuenta_pide_sesion()
    {
        using var anonymous = factory.CreateClient();
        await anonymous.UseXsrfTokenAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me/account")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PutJsonAsync("/api/me/account", new UpdateAccountRequest("X"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await ChangePasswordAsync(anonymous, "una-clave-larga", "otra-clave-larga")).StatusCode);
    }

    [Fact]
    public async Task Un_administrador_que_se_cambia_la_contrasena_desde_la_administracion_conserva_su_sesion()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var boss = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("jefe"), Roles.Admin);
        using var current = await TestUsers.LoginAsync(factory, boss.UserName);
        using var other = await TestUsers.LoginAsync(factory, boss.UserName);

        var reset = await current.PutJsonAsync($"/api/admin/users/{boss.Id}/password", new ResetPasswordRequest("clave-nueva-larga"));

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await UserTestSupport.SessionStatusAsync(current));
        Assert.Equal(HttpStatusCode.Unauthorized, await UserTestSupport.SessionStatusAsync(other));
    }
}
