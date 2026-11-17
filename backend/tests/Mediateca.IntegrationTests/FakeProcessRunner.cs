using Mediateca.Core.Subtitles;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Reemplaza a ffprobe y ffmpeg en las pruebas: guarda cada solicitud y responde lo que diga <see cref="Handler"/>.
/// Por omisión, "no está instalado", que es lo que pasa en la CI y en una PC sin FFmpeg.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<ProcessRequest> _requests = [];

    public Func<ProcessRequest, ProcessResult> Handler { get; set; } = _ => ProcessResult.NotInstalled;

    /// <summary>Todo lo que se pidió ejecutar, en orden.</summary>
    public IReadOnlyList<ProcessRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public IReadOnlyList<ProcessRequest> RequestsFor(string fileName) =>
        [.. Requests.Where(request => request.FileName == fileName)];

    public void Reset()
    {
        lock (_requests)
        {
            _requests.Clear();
        }

        Handler = _ => ProcessResult.NotInstalled;
    }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(request);
        }

        return Task.FromResult(Handler(request));
    }
}
