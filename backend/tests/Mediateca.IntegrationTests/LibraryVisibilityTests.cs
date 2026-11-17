using System.Net;
using Mediateca.Api.Items;
using Mediateca.Api.Libraries;
using Mediateca.Core.Libraries;
using Mediateca.Core.Users;
using Mediateca.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Regla de visibilidad (PLAN.md, sección 6): el administrador ve todo; un usuario solo las bibliotecas
/// que tiene asignadas, y lo demás responde 404. La fase 3 agrega la administración de usuarios y las
/// pruebas completas con dos usuarios; esta es la comprobación mínima de las consultas de la fase 2.
/// </summary>
public sealed class LibraryVisibilityTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    private const string RegularUser = "invitado";

    public async Task InitializeAsync()
    {
        await TestUsers.EnsureAdminAsync(factory);
        await TestUsers.EnsureUserAsync(factory, RegularUser);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Un_usuario_sin_acceso_recibe_404_y_con_acceso_ve_la_biblioteca()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas", "Big Buck Bunny (2008)/Big Buck Bunny (2008).mp4");
        LibraryTestSupport.WriteFile(folder, "Big Buck Bunny (2008)/poster.png", [137, 80, 78, 71]);
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Privada"), LibraryType.Movies, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);
        var movie = Assert.Single((await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items);

        using var user = await TestUsers.LoginAsync(factory, RegularUser);

        // Sin acceso: la biblioteca y todo lo suyo responde 404, como si no existiera.
        Assert.DoesNotContain((await user.GetJsonAsync<List<LibraryResponse>>("/api/libraries"))!, l => l.Id == library.Id);
        foreach (var url in new[]
        {
            $"/api/libraries/{library.Id}/items",
            $"/api/libraries/{library.Id}/filters",
            $"/api/items/{movie.Id}",
            $"/api/items/{movie.Id}/children",
            $"/api/items/{movie.Id}/image",
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync(url)).StatusCode);
        }

        var recent = (await user.GetJsonAsync<List<ItemSummary>>("/api/me/recently-added?limit=50"))!;
        Assert.DoesNotContain(recent, i => i.Id == movie.Id);

        // Con acceso, lo ve.
        await GrantAccessAsync(RegularUser, library.Id);

        Assert.Contains((await user.GetJsonAsync<List<LibraryResponse>>("/api/libraries"))!, l => l.Id == library.Id && l.ItemCount == 1);
        Assert.Equal(1, (await user.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Total);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/items/{movie.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/items/{movie.Id}/image")).StatusCode);
        Assert.Contains((await user.GetJsonAsync<List<ItemSummary>>("/api/me/recently-added?limit=50"))!, i => i.Id == movie.Id);

        // La información del servidor sigue siendo solo para el administrador: el listado de usuarios no trae las carpetas.
        var json = await user.GetStringAsync("/api/libraries");
        Assert.DoesNotContain("paths", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Las_rutas_de_la_api_para_usuarios_piden_sesion()
    {
        using var anonymous = factory.CreateClient();

        foreach (var url in new[]
        {
            "/api/libraries",
            $"/api/libraries/{Guid.NewGuid()}/items",
            $"/api/items/{Guid.NewGuid()}",
            $"/api/items/{Guid.NewGuid()}/children",
            $"/api/items/{Guid.NewGuid()}/image",
            "/api/me/recently-added",
        })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        }
    }

    private async Task GrantAccessAsync(string userName, Guid libraryId)
    {
        var userId = await TestUsers.GetUserIdAsync(factory, userName);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        db.LibraryAccess.Add(new UserLibraryAccess { UserId = userId, LibraryId = libraryId });
        await db.SaveChangesAsync();
    }
}
