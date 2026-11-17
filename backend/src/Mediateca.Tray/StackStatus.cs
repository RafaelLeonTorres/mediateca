using System.Text.Json;

namespace Mediateca.Tray;

/// <summary>Estado general de la pila de Mediateca (los cuatro contenedores).</summary>
public enum StackState
{
    /// <summary>Docker no responde (cerrado o sin instalar).</summary>
    DockerUnavailable,

    /// <summary>No hay contenedores corriendo.</summary>
    Stopped,

    /// <summary>Los contenedores están arrancando (por ejemplo, con el healthcheck en "starting").</summary>
    Starting,

    /// <summary>web, api, worker y db están corriendo.</summary>
    Running,

    /// <summary>Corren algunos contenedores, pero no los cuatro.</summary>
    Partial,
}

/// <summary>Un servicio tal como lo reporta <c>docker compose ps --format json</c>.</summary>
/// <param name="Service">Nombre del servicio en el compose (web, api, worker, db).</param>
/// <param name="State">Estado del contenedor ("running", "exited", "created"…).</param>
/// <param name="Health">Estado del healthcheck ("healthy", "starting", "unhealthy") o cadena vacía.</param>
public sealed record ServiceInfo(string Service, string State, string Health);

/// <summary>Resultado de una revisión de estado.</summary>
/// <param name="State">Estado general.</param>
/// <param name="WebRunning">Si el servicio web está corriendo (habilita "Abrir Mediateca").</param>
public sealed record StackSnapshot(StackState State, bool WebRunning);

/// <summary>Funciones puras para interpretar la salida de <c>docker compose ps</c>.</summary>
public static class StackStatus
{
    /// <summary>Servicios que deben estar corriendo para considerar la pila completa.</summary>
    public static readonly string[] RequiredServices = { "web", "api", "worker", "db" };

    /// <summary>
    /// Interpreta la salida de <c>docker compose ps --format json</c>. Acepta los dos formatos:
    /// un objeto JSON por línea (Compose reciente) o un arreglo JSON (versiones anteriores).
    /// Las líneas que no son JSON válido se ignoran.
    /// </summary>
    /// <param name="output">Salida estándar del comando; puede ser nula o vacía.</param>
    /// <returns>Los servicios encontrados; lista vacía si no hay ninguno.</returns>
    public static IReadOnlyList<ServiceInfo> ParsePs(string? output)
    {
        var result = new List<ServiceInfo>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return result;
        }

        var text = output.Trim();
        if (text.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        AddIfService(result, element);
                    }
                }
            }
            catch (JsonException)
            {
                // Salida corrupta: se trata como si no hubiera servicios.
            }

            return result;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] != '{')
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                AddIfService(result, document.RootElement);
            }
            catch (JsonException)
            {
                // Línea que no es JSON (por ejemplo, un aviso): se ignora.
            }
        }

        return result;
    }

    /// <summary>Calcula el estado general a partir de los servicios reportados.</summary>
    /// <param name="dockerAvailable">Si <c>docker info</c> respondió bien.</param>
    /// <param name="services">Servicios devueltos por <see cref="ParsePs"/>.</param>
    /// <returns>El estado de la pila.</returns>
    public static StackState ComputeState(bool dockerAvailable, IReadOnlyList<ServiceInfo> services)
    {
        if (!dockerAvailable)
        {
            return StackState.DockerUnavailable;
        }

        if (services.Count == 0)
        {
            return StackState.Stopped;
        }

        var anyStarting = services.Any(s =>
            IsEqual(s.Health, "starting") || IsEqual(s.State, "restarting") || IsEqual(s.State, "created"));

        var allRequiredRunning = RequiredServices.All(name => IsServiceRunning(services, name));
        if (allRequiredRunning)
        {
            return anyStarting ? StackState.Starting : StackState.Running;
        }

        if (anyStarting)
        {
            return StackState.Starting;
        }

        return services.Any(s => IsEqual(s.State, "running")) ? StackState.Partial : StackState.Stopped;
    }

    /// <summary>Indica si el servicio indicado tiene un contenedor en estado "running".</summary>
    /// <param name="services">Servicios reportados.</param>
    /// <param name="serviceName">Nombre del servicio, sin distinguir mayúsculas.</param>
    /// <returns><c>true</c> si está corriendo.</returns>
    public static bool IsServiceRunning(IReadOnlyList<ServiceInfo> services, string serviceName)
    {
        return services.Any(s => IsEqual(s.Service, serviceName) && IsEqual(s.State, "running"));
    }

    /// <summary>Texto del tooltip del ícono (máximo 63 caracteres, el límite de NotifyIcon).</summary>
    /// <param name="state">Estado de la pila.</param>
    /// <returns>El texto en español.</returns>
    public static string Describe(StackState state)
    {
        return state switch
        {
            StackState.Running => "Mediateca: en ejecución",
            StackState.Stopped => "Mediateca: detenida",
            StackState.Starting => "Mediateca: iniciando…",
            StackState.Partial => "Mediateca: en ejecución parcial",
            _ => "Mediateca: Docker no está disponible",
        };
    }

    private static void AddIfService(List<ServiceInfo> target, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var service = GetString(element, "Service");
        if (service.Length == 0)
        {
            return;
        }

        target.Add(new ServiceInfo(service, GetString(element, "State"), GetString(element, "Health")));
    }

    private static string GetString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool IsEqual(string? left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
