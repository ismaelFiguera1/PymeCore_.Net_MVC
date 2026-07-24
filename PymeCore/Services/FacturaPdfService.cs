using PymeCore.Dtos.Facturas;
using PymeCore.Pdf;
using QuestPDF.Fluent;

namespace PymeCore.Services
{
    public class FacturaPdfService
    {
        public byte[] Generar(FacturaSnapshotDto snapshot)
        {
            var documento = new FacturaPdfDocument(snapshot);
            return documento.GeneratePdf();
        }
    }
}
