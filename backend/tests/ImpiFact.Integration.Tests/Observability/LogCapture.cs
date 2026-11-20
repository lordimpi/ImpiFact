using System.Text;

namespace ImpiFact.Integration.Tests.Observability;

/// <summary>
/// Texto en memoria donde escribe la consola de los registros en las pruebas; se puede leer mientras otros hilos siguen
/// escribiendo (la API registra en segundo plano, por ejemplo al cerrar una solicitud).
/// </summary>
public sealed class LogCapture : TextWriter
{
    private readonly Lock gate = new();
    private readonly StringBuilder text = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (gate)
        {
            text.Append(value);
        }
    }

    public override void Write(string? value)
    {
        lock (gate)
        {
            text.Append(value);
        }
    }

    public override void Write(char[] buffer, int index, int count)
    {
        lock (gate)
        {
            text.Append(buffer, index, count);
        }
    }

    public override string ToString()
    {
        lock (gate)
        {
            return text.ToString();
        }
    }
}
