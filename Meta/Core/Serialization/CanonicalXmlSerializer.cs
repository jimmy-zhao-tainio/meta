using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Meta.Core.Serialization;

internal static class CanonicalXmlSerializer
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static byte[] SerializeToUtf8(XDocument document, bool indented)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = Utf8NoBom,
            Indent = indented,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = false,
        }))
        {
            document.Save(writer);
        }

        var contents = stream.ToArray();
        if (contents.Length == 0 || contents[^1] != (byte)'\n')
        {
            Array.Resize(ref contents, contents.Length + 1);
            contents[^1] = (byte)'\n';
        }

        return contents;
    }

    public static string SerializeToString(XDocument document, bool indented)
    {
        return Utf8NoBom.GetString(SerializeToUtf8(document, indented));
    }
}
