using System.Collections.Generic;
using System.IO;
using System.Linq;
using OCR.Business.Models;

namespace OCR.Business.DocxVbd;

/// <summary>
/// Ghép <see cref="QuyenSoDocxReader"/> (docx → trang sổ thô) với <see cref="DocxVbdEnvelopeMapper"/>
/// (trang sổ → envelope VietBD). Không giữ state — an toàn khi gọi song song.
/// </summary>
public sealed class DocxVbdConvertService : IDocxVbdConvertService
{
    public IReadOnlyList<VietBdGcnEnvelope> ConvertFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var pages = QuyenSoDocxReader.ReadFile(filePath);
        return pages.Select(page => DocxVbdEnvelopeMapper.Map(page, fileName)).ToList();
    }
}
