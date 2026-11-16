namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Elemento XML mínimo en memoria para armar documentos UBL (UBL 2.1: el estándar XML de documentos
/// comerciales que usa la DIAN). Un elemento contiene texto o elementos hijos, nunca ambos.
/// </summary>
/// <remarks>
/// Los nombres se escriben tal como se reciben, con su prefijo (por ejemplo "cbc:ID"). Las declaraciones
/// de espacios de nombres (xmlns:cbc="...") se agregan como atributos normales del elemento raíz, así que
/// la salida las conserva en el orden exacto en que se agregaron.
/// </remarks>
public sealed class UblElement
{
    private readonly List<KeyValuePair<string, string>> _attributes = [];
    private readonly List<UblElement> _children = [];
    private string? _text;

    public UblElement(string name, string? text = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        _text = text;
    }

    public string Name { get; }

    /// <summary>
    /// Contenido de texto; null o vacío significa que el elemento no tiene texto (sin hijos se escribe
    /// vacío, como <c>&lt;x/&gt;</c>).
    /// </summary>
    public string? Text => _text;

    public IReadOnlyList<KeyValuePair<string, string>> Attributes => _attributes;

    public IReadOnlyList<UblElement> Children => _children;

    /// <summary>Agrega un atributo; los atributos se escriben en el orden en que se agregan.</summary>
    public UblElement Attr(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        _attributes.Add(new(name, value));
        return this;
    }

    /// <summary>Agrega un elemento hijo y devuelve este elemento.</summary>
    public UblElement Add(UblElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!string.IsNullOrEmpty(_text))
        {
            throw new InvalidOperationException($"<{Name}> already has text; mixed content is not supported.");
        }

        _children.Add(child);
        return this;
    }

    /// <summary>Agrega un elemento hijo con contenido de texto y devuelve este elemento.</summary>
    public UblElement Add(string name, string text) => Add(new UblElement(name, text));

    /// <summary>Agrega un nuevo elemento hijo y devuelve el hijo, para seguir construyendo dentro de él.</summary>
    public UblElement AddChild(string name)
    {
        var child = new UblElement(name);
        Add(child);
        return child;
    }

    /// <summary>Asigna el contenido de texto; el elemento no debe tener hijos.</summary>
    public UblElement SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_children.Count > 0)
        {
            throw new InvalidOperationException($"<{Name}> already has children; mixed content is not supported.");
        }

        _text = text;
        return this;
    }
}
