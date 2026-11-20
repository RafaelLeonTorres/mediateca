using System.Net;
using Mediateca.Api.Auth;
using Mediateca.Api.Users;
using Mediateca.Core.Libraries;
using Mediateca.Infrastructure.Identity;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Administración de usuarios (PLAN.md, secciones 6 y 10): alta, edición, bibliotecas, restablecer la contraseña y
/// baja, con sus validaciones en español y las reglas del administrador.
/// </summary>
public sealed class UserAdminTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => TestUsers.EnsureAdminAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    private static Guid[] Sorted(IEnumerable<Guid> ids) => [.. ids.Order()];

    [Fact]
    public async Task Crea_un_usuario_con_rol_y_bibliotecas_y_aparece_en_la_lista()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var peliculas = await UserTestSupport.CreateEmptyLibraryAsync(factory, admin);
        var musica = await UserTestSupport.CreateEmptyLibraryAsync(factory, admin, LibraryType.Music);
        var userName = UserTestSupport.UniqueUserName("ana");

        // Con el nombre con espacios y una biblioteca repetida.
        var response = await admin.PostJsonAsync(
            "/api/admin/users",
            new CreateUserRequest(userName, "  Ana  ", TestUsers.Password, "usuario", [peliculas.Id, musica.Id, peliculas.Id]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.ReadJsonAsync<AdminUserResponse>())!;
        Assert.EndsWith($"/api/admin/users/{created.Id}", response.Headers.Location?.ToString());
        Assert.Equal(userName, created.UserName);
        Assert.Equal("Ana", created.DisplayName);
        Assert.Equal(Roles.Usuario, created.Role);
        Assert.Equal(Sorted([peliculas.Id, musica.Id]), Sorted(created.LibraryIds));
        Assert.True(created.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-5));

        // Está en la lista y se puede pedir por su id.
        var list = (await admin.GetJsonAsync<List<AdminUserResponse>>("/api/admin/users"))!;
        Assert.Contains(list, u => u.Id == created.Id && u.Role == Roles.Usuario);
        Assert.Contains(list, u => u.UserName == TestUsers.AdminUserName && u.Role == Roles.Admin);
        var single = (await admin.GetJsonAsync<AdminUserResponse>($"/api/admin/users/{created.Id}"))!;
        Assert.Equal(created.UserName, single.UserName);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/users/{Guid.NewGuid()}")).StatusCode);

        // Inicia sesión con la contraseña inicial, no es administrador y ve exactamente sus bibliotecas.
        using var ana = await TestUsers.LoginAsync(factory, userName);
        var me = (await ana.GetJsonAsync<UserResponse>("/api/auth/me"))!;
        Assert.False(me.IsAdmin);
        Assert.Equal("Ana", me.DisplayName);
        var visible = (await ana.GetJsonAsync<List<Mediateca.Api.Libraries.LibraryResponse>>("/api/libraries"))!;
        Assert.Equal(Sorted([peliculas.Id, musica.Id]), Sorted(visible.Select(l => l.Id)));
    }

    [Fact]
    public async Task Un_administrador_nuevo_ve_todo_y_sus_bibliotecas_no_se_guardan()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await UserTestSupport.CreateEmptyLibraryAsync(factory, admin);

        var created = await UserTestSupport.CreateUserAsync(
            admin, UserTestSupport.UniqueUserName("jefa"), Roles.Admin, [library.Id]);

        Assert.Equal(Roles.Admin, created.Role);
        Assert.Empty(created.LibraryIds);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        Assert.False(await db.LibraryAccess.AnyAsync(a => a.UserId == created.Id));

        // Ve todas las bibliotecas aunque no se le asignó ninguna.
        using var jefa = await TestUsers.LoginAsync(factory, created.UserName);
        var visible = (await jefa.GetJsonAsync<List<Mediateca.Api.Libraries.LibraryResponse>>("/api/libraries"))!;
        Assert.Contains(visible, l => l.Id == library.Id);
        Assert.Equal(HttpStatusCode.OK, (await jefa.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task El_alta_valida_los_datos_con_mensajes_en_espanol()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var existing = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("repetido"));

        var empty = await admin.PostJsonAsync("/api/admin/users", new CreateUserRequest("  ", "", "", "Usuario", null));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var emptyErrors = await UserTestSupport.ErrorsAsync(empty);
        Assert.Equal("Escribe un nombre de usuario.", Assert.Single(emptyErrors["userName"]));
        Assert.Equal("Escribe el nombre que se mostrará.", Assert.Single(emptyErrors["displayName"]));
        Assert.Equal("Escribe una contraseña inicial.", Assert.Single(emptyErrors["password"]));

        var shortPassword = await admin.PostJsonAsync(
            "/api/admin/users", new CreateUserRequest(UserTestSupport.UniqueUserName("corta"), "Corta", "1234", "Usuario", null));
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
        Assert.Contains("al menos 8 caracteres", Assert.Single((await UserTestSupport.ErrorsAsync(shortPassword))["password"]));

        var duplicate = await admin.PostJsonAsync(
            "/api/admin/users", new CreateUserRequest(existing.UserName, "Otra", TestUsers.Password, "Usuario", null));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("Ya existe el usuario", Assert.Single((await UserTestSupport.ErrorsAsync(duplicate))["userName"]));

        var invalidName = await admin.PostJsonAsync(
            "/api/admin/users", new CreateUserRequest("con espacios!", "Rara", TestUsers.Password, "Usuario", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalidName.StatusCode);
        Assert.Contains("no es válido", Assert.Single((await UserTestSupport.ErrorsAsync(invalidName))["userName"]));

        var badRole = await admin.PostJsonAsync(
            "/api/admin/users", new CreateUserRequest(UserTestSupport.UniqueUserName("rol"), "Rol", TestUsers.Password, "Dios", null));
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
        Assert.Equal("Elige el rol: Admin o Usuario.", Assert.Single((await UserTestSupport.ErrorsAsync(badRole))["role"]));

        var missingLibrary = await admin.PostJsonAsync(
            "/api/admin/users",
            new CreateUserRequest(UserTestSupport.UniqueUserName("bib"), "Bib", TestUsers.Password, "Usuario", [Guid.NewGuid()]));
        Assert.Equal(HttpStatusCode.BadRequest, missingLibrary.StatusCode);
        Assert.Contains("ya no existe", Assert.Single((await UserTestSupport.ErrorsAsync(missingLibrary))["libraryIds"]));

        var tooLong = await admin.PostJsonAsync(
            "/api/admin/users",
            new CreateUserRequest(new string('a', 51), new string('b', 101), TestUsers.Password, "Usuario", null));
        var tooLongErrors = await UserTestSupport.ErrorsAsync(tooLong);
        Assert.Contains("hasta 50", Assert.Single(tooLongErrors["userName"]));
        Assert.Contains("hasta 100", Assert.Single(tooLongErrors["displayName"]));

        // Ningún intento fallido dejó un usuario a medias.
        var list = (await admin.GetJsonAsync<List<AdminUserResponse>>("/api/admin/users"))!;
        Assert.DoesNotContain(list, u => u.DisplayName is "Corta" or "Otra" or "Rara" or "Rol" or "Bib");
    }

    [Fact]
    public async Task Edita_el_nombre_el_rol_y_las_bibliotecas()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var first = await UserTestSupport.CreateEmptyLibraryAsync(factory, admin);
        var second = await UserTestSupport.CreateEmptyLibraryAsync(factory, admin);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("beto"), libraryIds: [first.Id]);

        // Nombre y bibliotecas.
        var edited = await UserTestSupport.UpdateAsync(admin, user.Id, "  Beto Pérez ", "Usuario", [second.Id]);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var afterEdit = (await edited.ReadJsonAsync<AdminUserResponse>())!;
        Assert.Equal("Beto Pérez", afterEdit.DisplayName);
        Assert.Equal(user.UserName, afterEdit.UserName);
        Assert.Equal(new[] { second.Id }, afterEdit.LibraryIds.ToArray());

        // Sin lista de bibliotecas, se quedan como estaban.
        var keep = (await (await UserTestSupport.UpdateAsync(admin, user.Id, "Beto", "Usuario")).ReadJsonAsync<AdminUserResponse>())!;
        Assert.Equal(new[] { second.Id }, keep.LibraryIds.ToArray());

        // Con el rol Admin, sus permisos por biblioteca se borran: un administrador ve todo.
        var promoted = (await (await UserTestSupport.UpdateAsync(admin, user.Id, "Beto", "Admin", [first.Id]))
            .ReadJsonAsync<AdminUserResponse>())!;
        Assert.Equal(Roles.Admin, promoted.Role);
        Assert.Empty(promoted.LibraryIds);

        // De vuelta a Usuario, no se acuerda de las de antes: parte sin bibliotecas.
        var demoted = (await (await UserTestSupport.UpdateAsync(admin, user.Id, "Beto", "Usuario")).ReadJsonAsync<AdminUserResponse>())!;
        Assert.Equal(Roles.Usuario, demoted.Role);
        Assert.Empty(demoted.LibraryIds);

        // Validaciones y usuario que no existe.
        var invalid = await UserTestSupport.UpdateAsync(admin, user.Id, "", "Nadie", [Guid.NewGuid()]);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = await UserTestSupport.ErrorsAsync(invalid);
        Assert.True(errors.ContainsKey("displayName"));
        Assert.True(errors.ContainsKey("role"));
        Assert.True(errors.ContainsKey("libraryIds"));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await UserTestSupport.UpdateAsync(admin, Guid.NewGuid(), "Nadie", "Usuario")).StatusCode);
    }

    [Fact]
    public async Task Reemplaza_las_bibliotecas_de_un_usuario_y_no_acepta_las_de_un_administrador()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var first = await UserTestSupport.CreateEmptyLibraryAsync(factory, admin);
        var second = await UserTestSupport.CreateEmptyLibraryAsync(factory, admin);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("ines"), libraryIds: [first.Id]);

        var replaced = await admin.PutJsonAsync($"/api/admin/users/{user.Id}/libraries", new SetUserLibrariesRequest([second.Id]));
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(new[] { second.Id }, (await replaced.ReadJsonAsync<AdminUserResponse>())!.LibraryIds.ToArray());

        var cleared = await admin.PutJsonAsync($"/api/admin/users/{user.Id}/libraries", new SetUserLibrariesRequest([]));
        Assert.Empty((await cleared.ReadJsonAsync<AdminUserResponse>())!.LibraryIds);

        var missingList = await admin.PutJsonAsync($"/api/admin/users/{user.Id}/libraries", new SetUserLibrariesRequest(null));
        Assert.Equal(HttpStatusCode.BadRequest, missingList.StatusCode);

        var unknown = await admin.PutJsonAsync($"/api/admin/users/{user.Id}/libraries", new SetUserLibrariesRequest([Guid.NewGuid()]));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var adminId = await TestUsers.GetUserIdAsync(factory, TestUsers.AdminUserName);
        var forAdmin = await admin.PutJsonAsync($"/api/admin/users/{adminId}/libraries", new SetUserLibrariesRequest([first.Id]));
        Assert.Equal(HttpStatusCode.BadRequest, forAdmin.StatusCode);
        Assert.Contains("ve todas las bibliotecas", Assert.Single((await UserTestSupport.ErrorsAsync(forAdmin))["libraryIds"]));

        var nobody = await admin.PutJsonAsync($"/api/admin/users/{Guid.NewGuid()}/libraries", new SetUserLibrariesRequest([]));
        Assert.Equal(HttpStatusCode.NotFound, nobody.StatusCode);
    }

    [Fact]
    public async Task Restablece_la_contrasena_de_otro_usuario()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("luis"));

        var tooShort = await admin.PutJsonAsync($"/api/admin/users/{user.Id}/password", new ResetPasswordRequest("corta"));
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Contains("al menos 8 caracteres", Assert.Single((await UserTestSupport.ErrorsAsync(tooShort))["newPassword"]));

        var empty = await admin.PutJsonAsync($"/api/admin/users/{user.Id}/password", new ResetPasswordRequest(""));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var reset = await admin.PutJsonAsync($"/api/admin/users/{user.Id}/password", new ResetPasswordRequest("otra-clave-larga"));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await UserTestSupport.TryLoginAsync(factory, user.UserName, TestUsers.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UserTestSupport.TryLoginAsync(factory, user.UserName, "otra-clave-larga")).StatusCode);

        var nobody = await admin.PutJsonAsync($"/api/admin/users/{Guid.NewGuid()}/password", new ResetPasswordRequest("otra-clave-larga"));
        Assert.Equal(HttpStatusCode.NotFound, nobody.StatusCode);
    }

    [Fact]
    public async Task Restablecer_la_contrasena_desbloquea_a_un_usuario_bloqueado_por_intentos_fallidos()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("bloqueado"));

        // Cinco intentos fallidos bloquean la cuenta (Lockout:MaxFailedAccessAttempts).
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await UserTestSupport.TryLoginAsync(factory, user.UserName, "equivocada-1");
        }

        Assert.Equal(
            HttpStatusCode.Locked,
            (await UserTestSupport.TryLoginAsync(factory, user.UserName, TestUsers.Password)).StatusCode);

        var reset = await admin.PutJsonAsync($"/api/admin/users/{user.Id}/password", new ResetPasswordRequest("clave-nueva-larga"));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await UserTestSupport.TryLoginAsync(factory, user.UserName, "clave-nueva-larga")).StatusCode);
    }

    [Fact]
    public async Task Da_de_baja_a_un_usuario_con_su_progreso_y_sus_permisos()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await UserTestSupport.CreateEmptyLibraryAsync(factory, admin);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("baja"), libraryIds: [library.Id]);

        var deleted = await admin.DeleteAsync($"/api/admin/users/{user.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/users/{user.Id}")).StatusCode);
        Assert.DoesNotContain((await admin.GetJsonAsync<List<AdminUserResponse>>("/api/admin/users"))!, u => u.Id == user.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UserTestSupport.TryLoginAsync(factory, user.UserName, TestUsers.Password)).StatusCode);

        // Sus permisos se borraron con él; la biblioteca sigue ahí.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        Assert.False(await db.LibraryAccess.AnyAsync(a => a.UserId == user.Id));
        Assert.True(await db.Libraries.AnyAsync(l => l.Id == library.Id));

        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/admin/users/{user.Id}")).StatusCode);
    }

    [Fact]
    public async Task Un_administrador_no_puede_borrarse_ni_quitarse_el_rol_a_si_mismo()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var adminId = await TestUsers.GetUserIdAsync(factory, TestUsers.AdminUserName);
        // Con otro administrador de por medio, para que la que frene sea la regla de "a sí mismo" y no la del último.
        await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("respaldo"), Roles.Admin);

        var delete = await admin.DeleteAsync($"/api/admin/users/{adminId}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("No puedes borrar tu propia cuenta.", await UserTestSupport.TitleAsync(delete));

        var demote = await UserTestSupport.UpdateAsync(admin, adminId, "Administración", "Usuario");
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal("No puedes quitarte el rol de administrador.", await UserTestSupport.TitleAsync(demote));

        // Nada cambió: sigue siendo administrador y su sesión vale.
        var me = (await admin.GetJsonAsync<UserResponse>("/api/auth/me"))!;
        Assert.True(me.IsAdmin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/users")).StatusCode);

        // Cambiarse el nombre sí se puede (sin tocar el rol).
        var rename = await UserTestSupport.UpdateAsync(admin, adminId, "Administración", "Admin");
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
    }

    [Fact]
    public async Task Un_usuario_normal_recibe_403_en_la_administracion_de_usuarios_y_sin_sesion_401()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var user = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("curioso"));
        var other = Guid.NewGuid();
        using var normal = await TestUsers.LoginAsync(factory, user.UserName);
        using var anonymous = factory.CreateClient();
        await anonymous.UseXsrfTokenAsync();

        foreach (var (client, expected) in new[]
        {
            (normal, HttpStatusCode.Forbidden),
            (anonymous, HttpStatusCode.Unauthorized),
        })
        {
            Assert.Equal(expected, (await client.GetAsync("/api/admin/users")).StatusCode);
            Assert.Equal(expected, (await client.GetAsync($"/api/admin/users/{other}")).StatusCode);
            Assert.Equal(
                expected,
                (await client.PostJsonAsync("/api/admin/users", new CreateUserRequest("x", "X", "una-clave-larga", "Admin", null))).StatusCode);
            Assert.Equal(expected, (await UserTestSupport.UpdateAsync(client, other, "X", "Admin")).StatusCode);
            Assert.Equal(
                expected,
                (await client.PutJsonAsync($"/api/admin/users/{other}/libraries", new SetUserLibrariesRequest([]))).StatusCode);
            Assert.Equal(
                expected,
                (await client.PutJsonAsync($"/api/admin/users/{other}/password", new ResetPasswordRequest("una-clave-larga"))).StatusCode);
            Assert.Equal(expected, (await client.DeleteAsync($"/api/admin/users/{other}")).StatusCode);
        }

        // Y no pudo hacerse administrador a sí mismo.
        var mine = (await admin.GetJsonAsync<AdminUserResponse>($"/api/admin/users/{user.Id}"))!;
        Assert.Equal(Roles.Usuario, mine.Role);
    }
}
