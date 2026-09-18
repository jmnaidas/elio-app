using System.Diagnostics;

namespace Elio.Api;

public static class Observability
{
    // Stable source name for future OpenTelemetry subscription; no exporter required locally.
    public const string ServiceName = "Elio.Api";
    public static readonly ActivitySource ActivitySource = new(ServiceName);
}
