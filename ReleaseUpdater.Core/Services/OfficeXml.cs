using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace ReleaseUpdater.Core.Services;

/// <summary>Reads an XML part of an Office file safely: no DTD (no entity tricks) and a limit for its size.</summary>
internal static class OfficeXml
{
    private const long MaxPartBytes = 50L * 1024 * 1024;

    /// <exception cref="FormatException">The part is too large or no valid XML.</exception>
    public static XDocument Load(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxPartBytes)
        {
            throw new FormatException("The file is too large.");
        }

        try
        {
            using Stream stream = entry.Open();
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            return XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new FormatException("The file is no valid Office document.", ex);
        }
    }
}
