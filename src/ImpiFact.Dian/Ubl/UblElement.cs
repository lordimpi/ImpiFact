namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Minimal element tree for UBL documents. Names are written as given (prefix:local); namespace
/// declarations are ordinary attributes of the root, so the output keeps their exact order.
/// An element holds either text or child elements, never both.
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

    /// <summary>Text content; null or empty means the element has no text node.</summary>
    public string? Text => _text;

    public IReadOnlyList<KeyValuePair<string, string>> Attributes => _attributes;

    public IReadOnlyList<UblElement> Children => _children;

    /// <summary>Adds an attribute; attributes are written in the order they are added.</summary>
    public UblElement Attr(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        _attributes.Add(new(name, value));
        return this;
    }

    /// <summary>Appends a child element and returns this element.</summary>
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

    /// <summary>Appends a child element with text content and returns this element.</summary>
    public UblElement Add(string name, string text) => Add(new UblElement(name, text));

    /// <summary>Appends a new child element and returns the child, to keep building inside it.</summary>
    public UblElement AddChild(string name)
    {
        var child = new UblElement(name);
        Add(child);
        return child;
    }

    /// <summary>Sets the text content; the element must have no children.</summary>
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
