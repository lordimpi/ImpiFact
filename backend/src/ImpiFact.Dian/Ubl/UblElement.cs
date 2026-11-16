namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Árbol mínimo de elementos para documentos UBL. Los nombres se escriben tal como se reciben
/// (prefijo:local); las declaraciones de espacios de nombres son atributos comunes de la raíz, así que la
/// salida conserva su orden exacto. Un elemento contiene texto o elementos hijos, nunca ambos.
/// </summary>
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

    /// <summary>Contenido de texto; null o vacío significa que el elemento no tiene nodo de texto.</summary>
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
