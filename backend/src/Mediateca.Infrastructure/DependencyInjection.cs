using Mediateca.Infrastructure.Identity;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mediateca.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra la carpeta de datos, EF Core con PostgreSQL, Identity y las llaves de Data
    /// Protection. En las siguientes fases se agregan aquí el escáner y los proveedores de metadatos.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.Section))
            .PostConfigure<IHostEnvironment>((options, environment) =>
                options.DataPath = Path.GetFullPath(options.DataPath, environment.ContentRootPath));

        // La cadena de conexión se lee al crear el DbContext y no al registrarlo, así las pruebas
        // de integración pueden apuntar a su propio PostgreSQL.
        services.AddDbContext<MediatecaDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException("Falta la cadena de conexión ConnectionStrings:Default.");
            options.UseNpgsql(connectionString).UseAsyncSeeding(DatabaseInitializer.SeedAsync);
        });

        services.AddIdentityCore<AppUser>(options =>
            {
                // Contraseñas largas en lugar de reglas de composición, como recomienda el NIST.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.User.AllowedUserNameCharacters += "áéíóúüñÁÉÍÓÚÜÑ";
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MediatecaDbContext>()
            .AddErrorDescriber<SpanishIdentityErrorDescriber>()
            .AddSignInManager();

        // Llaves que cifran la cookie de sesión y los tokens XSRF. En la base de datos sobreviven
        // a que se recree el contenedor de la API.
        services.AddDataProtection()
            .SetApplicationName("Mediateca")
            .PersistKeysToDbContext<MediatecaDbContext>();

        return services;
    }
}
