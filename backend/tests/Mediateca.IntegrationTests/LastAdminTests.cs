using System.Net;
using Mediateca.Api.Users;
using Mediateca.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.IntegrationTests;

/// <summary>
/// El sistema nunca se queda sin administradores, ni siquiera cuando dos administradores se quitan el cargo uno al
/// otro a la vez (el candado de PostgreSQL de la administración de usuarios).
/// </summary>
public sealed class LastAdminTests(MediatecaApiFactorySesionAlInstante factory) : IClassFixture<MediatecaApiFactorySesionAlInstante>, IAsyncLifetime
{
    public Task InitializeAsync() => TestUsers.EnsureAdminAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> CountAdminsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return (await users.GetUsersInRoleAsync(Roles.Admin)).Count;
    }

    /// <summary>Uno de los dos pedidos funcionó; el otro se rechazó por la regla (409) o porque su sesión ya no valía.</summary>
    private static void AssertOneWonAndOneWasRejected(HttpResponseMessage[] responses, HttpStatusCode success)
    {
        Assert.Single(responses, r => r.StatusCode == success);
        Assert.All(
            responses.Where(r => r.StatusCode != success),
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden }));
    }

    [Fact]
    public async Task Dos_administradores_que_se_borran_o_se_degradan_a_la_vez_no_dejan_al_sistema_sin_ninguno()
    {
        // Fase 1: dos administradores se borran uno al otro.
        using var first = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var firstId = await TestUsers.GetUserIdAsync(factory, TestUsers.AdminUserName);
        var second = await UserTestSupport.CreateUserAsync(first, UserTestSupport.UniqueUserName("segundo"), Roles.Admin);
        using var secondClient = await TestUsers.LoginAsync(factory, second.UserName);
        Assert.Equal(2, await CountAdminsAsync());

        var deletions = await Task.WhenAll(
            first.DeleteAsync($"/api/admin/users/{second.Id}"),
            secondClient.DeleteAsync($"/api/admin/users/{firstId}"));

        AssertOneWonAndOneWasRejected(deletions, HttpStatusCode.NoContent);
        Assert.Equal(1, await CountAdminsAsync());

        // Fase 2: con el administrador que quedó y uno nuevo, se quitan el rol uno al otro.
        var (survivor, survivorId) = deletions[0].StatusCode == HttpStatusCode.NoContent
            ? (first, firstId)
            : (secondClient, second.Id);
        var third = await UserTestSupport.CreateUserAsync(survivor, UserTestSupport.UniqueUserName("tercero"), Roles.Admin);
        using var thirdClient = await TestUsers.LoginAsync(factory, third.UserName);
        Assert.Equal(2, await CountAdminsAsync());

        var demotions = await Task.WhenAll(
            UserTestSupport.UpdateAsync(survivor, third.Id, third.DisplayName, "Usuario"),
            UserTestSupport.UpdateAsync(thirdClient, survivorId, "Quien quedó", "Usuario"));

        AssertOneWonAndOneWasRejected(demotions, HttpStatusCode.OK);
        Assert.Equal(1, await CountAdminsAsync());
    }
}
