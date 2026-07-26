using PymeCore.Dtos.Facturas;
using PymeCore.Models;
using PymeCore.Pdf;
using QuestPDF.Fluent;

namespace PymeCore.Services
{
    public class FacturaPdfService
    {
        public byte[] Generar(FacturaSnapshotDto snapshot, EstadoFactura estado)
        {
            var documento = new FacturaPdfDocument(snapshot, estado);
            return documento.GeneratePdf();
        }
    }
}
