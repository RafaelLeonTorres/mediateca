using Mediateca.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Primer arranque: si la base no existe, la crea y aplica todas las migraciones; si ya existe,
    /// aplica solo las pendientes. Al terminar, EF Core corre la siembra (<see cref="SeedAsync"/>).
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Siembra de EF Core (UseAsyncSeeding). Corre después de cada MigrateAsync, así que solo agrega
    /// lo que falta: los roles Admin y Usuario.
    /// </summary>
    internal static async Task SeedAsync(DbContext context, bool storeManagementPerformed, CancellationToken cancellationToken)
    {
        var roles = context.Set<IdentityRole<Guid>>();
        foreach (var name in Roles.All)
        {
            var normalizedName = name.ToUpperInvariant();
            if (!await roles.AnyAsync(r => r.NormalizedName == normalizedName, cancellationToken))
            {
                roles.Add(new IdentityRole<Guid>(name)
                {
                    NormalizedName = normalizedName,
                    ConcurrencyStamp = Guid.NewGuid().ToString(),
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
