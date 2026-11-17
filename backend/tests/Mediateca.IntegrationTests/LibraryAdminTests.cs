using System.Net;
using Mediateca.Api.Libraries;
using Mediateca.Core.Libraries;
using Mediateca.Infrastructure.Libraries;

namespace Mediateca.IntegrationTests;

/// <summary>Alta, edición, baja y validación de bibliotecas, y quién puede usar la administración.</summary>
public sealed class LibraryAdminTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    private const string RegularUser = "usuario";

    public async Task InitializeAsync()
    {
        await TestUsers.EnsureAdminAsync(factory);
        await TestUsers.EnsureUserAsync(factory, RegularUser);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("GET", "/api/admin/libraries")]
    [InlineData("GET", "/api/admin/folders")]
    [InlineData("GET", "/api/admin/scans/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/admin/libraries/00000000-0000-0000-0000-000000000001/scan")]
    [InlineData("DELETE", "/api/admin/libraries/00000000-0000-0000-0000-000000000001")]
    public async Task Sin_sesion_la_administracion_responde_401(string method, string url)
    {
        using var client = factory.CreateClient();
        await client.UseXsrfTokenAsync();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/admin/libraries")]
    [InlineData("GET", "/api/admin/folders")]
    [InlineData("GET", "/api/admin/scans/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/admin/libraries/00000000-0000-0000-0000-000000000001/scan")]
    [InlineData("DELETE", "/api/admin/libraries/00000000-0000-0000-0000-000000000001")]
    public async Task Un_usuario_sin_permisos_de_administrador_recibe_403(string method, string url)
    {
        using var client = await TestUsers.LoginAsync(factory, RegularUser);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Un_usuario_sin_permisos_no_puede_crear_bibliotecas()
    {
        using var client = await TestUsers.LoginAsync(factory, RegularUser);
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas");

        var response = await client.PostJsonAsync(
            "/api/admin/libraries", new LibraryRequest(LibraryTestSupport.UniqueName("Intento"), LibraryType.Movies, [folder]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Crea_una_biblioteca_y_aparece_en_el_listado_con_su_tipo_como_texto()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas");
        var name = LibraryTestSupport.UniqueName("Películas");

        var response = await admin.PostJsonAsync("/api/admin/libraries", new LibraryRequest($"  {name}  ", LibraryType.Movies, [folder]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.ReadJsonAsync<AdminLibraryResponse>())!;
        Assert.Equal(name, created.Name);
        Assert.Equal(LibraryType.Movies, created.Type);
        Assert.Equal(new[] { folder }, created.Paths.ToArray());
        Assert.Equal(0, created.ItemCount);
        Assert.Null(created.LastScan);

        // El tipo viaja como texto, no como número.
        var json = await admin.GetStringAsync("/api/admin/libraries");
        Assert.Contains("\"type\":\"Movies\"", json);

        var list = (await admin.GetJsonAsync<List<AdminLibraryResponse>>("/api/admin/libraries"))!;
        Assert.Contains(list, l => l.Id == created.Id);
    }

    [Fact]
    public async Task Las_bibliotecas_incluyen_las_rutas_para_mostrar_con_los_discos_como_en_Windows()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var name = Guid.NewGuid().ToString("N");
        var disco = Path.Combine(factory.DrivesPath, "F", "Peliculas", name);
        Directory.CreateDirectory(disco);
        var normal = LibraryTestSupport.CreateFolder(factory, "Series");

        var created = await LibraryTestSupport.CreateLibraryAsync(
            admin, LibraryTestSupport.UniqueName("Con discos"), LibraryType.Movies, disco, normal);

        // Las rutas guardadas son las del contenedor; las de mostrar, las de Windows (en el mismo orden).
        Assert.Equal(new[] { disco, normal }, created.Paths.ToArray());
        Assert.Equal(new[] { $"F:\\Peliculas\\{name}", normal }, created.DisplayPaths.ToArray());

        var list = (await admin.GetJsonAsync<List<AdminLibraryResponse>>("/api/admin/libraries"))!;
        Assert.Equal(created.DisplayPaths, list.Single(l => l.Id == created.Id).DisplayPaths);
        Assert.Contains("\"displayPaths\"", await admin.GetStringAsync("/api/admin/libraries"));
    }

    [Fact]
    public async Task El_selector_de_carpetas_lista_los_discos_y_sube_de_un_disco_a_la_lista_inicial()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var unidad = Path.Combine(factory.DrivesPath, "F");
        var peliculas = Path.Combine(unidad, "Peliculas");
        Directory.CreateDirectory(peliculas);

        var roots = (await admin.GetJsonAsync<FolderListing>("/api/admin/folders"))!;
        var disco = Assert.Single(roots.Folders, f => f.Name == "Disco F:");
        Assert.Equal(unidad, disco.Path);
        Assert.Equal("F:\\", disco.DisplayPath);
        // La carpeta de los discos no aparece como una carpeta más.
        Assert.DoesNotContain(roots.Folders, f => f.Path == factory.DrivesPath);

        var dentro = (await admin.GetJsonAsync<FolderListing>($"/api/admin/folders?path={Uri.EscapeDataString(unidad)}"))!;
        Assert.Null(dentro.Parent);
        Assert.Equal("F:\\", dentro.DisplayPath);
        var carpeta = Assert.Single(dentro.Folders, f => f.Name == "Peliculas");
        Assert.Equal("F:\\Peliculas", carpeta.DisplayPath);

        var masAdentro = (await admin.GetJsonAsync<FolderListing>($"/api/admin/folders?path={Uri.EscapeDataString(peliculas)}"))!;
        Assert.Equal(unidad, masAdentro.Parent);
    }

    [Fact]
    public async Task Rechaza_una_carpeta_fuera_de_las_carpetas_de_medios()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var outside = Path.Combine(Path.GetTempPath(), $"mediateca-fuera-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var response = await admin.PostJsonAsync(
                "/api/admin/libraries",
                new LibraryRequest(LibraryTestSupport.UniqueName("Fuera"), LibraryType.Movies, [outside]));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("fuera de las carpetas de medios", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Rechaza_una_carpeta_hermana_que_solo_comparte_el_prefijo()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var sibling = factory.MediaPath + "2";
        Directory.CreateDirectory(sibling);
        try
        {
            var response = await admin.PostJsonAsync(
                "/api/admin/libraries",
                new LibraryRequest(LibraryTestSupport.UniqueName("Hermana"), LibraryType.Movies, [sibling]));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public async Task Rechaza_carpetas_que_no_existen_rutas_relativas_y_listas_vacias()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var missing = Path.Combine(factory.MediaPath, "NoExiste");

        foreach (var paths in new List<string>[] { [missing], ["Peliculas"], [] })
        {
            var response = await admin.PostJsonAsync(
                "/api/admin/libraries", new LibraryRequest(LibraryTestSupport.UniqueName("Mala"), LibraryType.Series, paths));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Rechaza_un_nombre_vacio_y_un_tipo_que_falta()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas");

        var withoutName = await admin.PostJsonAsync("/api/admin/libraries", new LibraryRequest("  ", LibraryType.Movies, [folder]));
        var withoutType = await admin.PostJsonAsync("/api/admin/libraries", new LibraryRequest(LibraryTestSupport.UniqueName("Sin tipo"), null, [folder]));

        Assert.Equal(HttpStatusCode.BadRequest, withoutName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withoutType.StatusCode);
    }

    [Fact]
    public async Task Un_nombre_repetido_responde_409_aunque_cambien_las_mayusculas()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas");
        var name = LibraryTestSupport.UniqueName("Repetida");
        await LibraryTestSupport.CreateLibraryAsync(admin, name, LibraryType.Movies, folder);

        var response = await admin.PostJsonAsync("/api/admin/libraries", new LibraryRequest(name.ToUpperInvariant(), LibraryType.Series, [folder]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Ya existe una biblioteca", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Edita_el_nombre_y_las_carpetas_pero_no_el_tipo()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var first = LibraryTestSupport.CreateFolder(factory, "Uno");
        var second = LibraryTestSupport.CreateFolder(factory, "Dos");
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Editable"), LibraryType.Series, first);

        var newName = LibraryTestSupport.UniqueName("Renombrada");
        var updated = await admin.PutJsonAsync($"/api/admin/libraries/{library.Id}", new LibraryRequest(newName, LibraryType.Series, [first, second]));

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = (await updated.ReadJsonAsync<AdminLibraryResponse>())!;
        Assert.Equal(newName, body.Name);
        Assert.Equal(new[] { first, second }, body.Paths.ToArray());

        var changeType = await admin.PutJsonAsync($"/api/admin/libraries/{library.Id}", new LibraryRequest(newName, LibraryType.Music, [first]));
        Assert.Equal(HttpStatusCode.BadRequest, changeType.StatusCode);

        // Sin mandar el tipo también se puede editar.
        var withoutType = await admin.PutJsonAsync($"/api/admin/libraries/{library.Id}", new LibraryRequest(newName, null, [second]));
        Assert.Equal(HttpStatusCode.OK, withoutType.StatusCode);

        var missing = await admin.PutJsonAsync($"/api/admin/libraries/{Guid.NewGuid()}", new LibraryRequest(newName, null, [first]));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Elimina_una_biblioteca_con_su_contenido()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas", "Big Buck Bunny (2008)/Big Buck Bunny (2008).mp4");
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Para borrar"), LibraryType.Movies, folder);
        var scan = await LibraryTestSupport.ScanAsync(admin, library.Id);
        Assert.Equal(1, scan.Added);

        var deleted = await admin.DeleteAsync($"/api/admin/libraries/{library.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var list = (await admin.GetJsonAsync<List<AdminLibraryResponse>>("/api/admin/libraries"))!;
        Assert.DoesNotContain(list, l => l.Id == library.Id);
        // Los archivos de la carpeta no se tocan: la API no escribe en las carpetas de medios.
        Assert.True(Directory.Exists(folder));

        var again = await admin.DeleteAsync($"/api/admin/libraries/{library.Id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task El_selector_de_carpetas_lista_las_carpetas_de_medios_y_sus_subcarpetas()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas");
        Directory.CreateDirectory(Path.Combine(folder, "Accion"));
        Directory.CreateDirectory(Path.Combine(folder, ".oculta"));

        var roots = (await admin.GetJsonAsync<FolderListing>("/api/admin/folders"))!;
        Assert.Null(roots.Path);
        Assert.Contains(roots.Folders, f => f.Path == Path.TrimEndingDirectorySeparator(Path.GetFullPath(factory.MediaPath)));

        var inside = (await admin.GetJsonAsync<FolderListing>($"/api/admin/folders?path={Uri.EscapeDataString(folder)}"))!;
        Assert.Equal(new[] { "Accion" }, inside.Folders.Select(f => f.Name).ToArray());
        Assert.Equal(Path.GetDirectoryName(folder), inside.Parent);

        var outside = await admin.GetAsync($"/api/admin/folders?path={Uri.EscapeDataString(Path.GetTempPath())}");
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
    }
}
