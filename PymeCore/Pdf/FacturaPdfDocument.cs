using PymeCore.Dtos.Facturas;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PymeCore.Pdf
{
    public class FacturaPdfDocument : IDocument
    {
        private readonly FacturaSnapshotDto _snapshot;

        public FacturaPdfDocument(FacturaSnapshotDto snapshot)
        {
            _snapshot = snapshot;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text(_snapshot.Empresa.Nombre).FontSize(14).Bold();
                    column.Item().Text($"CIF: {_snapshot.Empresa.Cif}");
                    column.Item().Text(_snapshot.Empresa.Direccion);
                });

                row.RelativeItem().AlignRight().Column(column =>
                {
                    column.Item().Text($"Factura {_snapshot.NumeroFactura}").FontSize(14).Bold();
                    column.Item().Text($"Fecha: {_snapshot.FechaEmision:dd/MM/yyyy}");
                });
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.PaddingVertical(15).Column(column =>
            {
                column.Spacing(12);

                column.Item().Column(clienteColumn =>
                {
                    clienteColumn.Item().Text("Cliente").Bold();
                    clienteColumn.Item().Text(_snapshot.Cliente.Nombre);
                    clienteColumn.Item().Text($"NIF: {_snapshot.Cliente.Nif}");
                    if (!string.IsNullOrWhiteSpace(_snapshot.Cliente.Direccion))
                        clienteColumn.Item().Text(_snapshot.Cliente.Direccion);
                });

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("Descripción").Bold();
                        header.Cell().AlignRight().Text("Cantidad").Bold();
                        header.Cell().AlignRight().Text("Precio").Bold();
                        header.Cell().AlignRight().Text("Subtotal").Bold();
                    });

                    foreach (var linea in _snapshot.Lineas)
                    {
                        table.Cell().Text(linea.Descripcion);
                        table.Cell().AlignRight().Text(linea.Cantidad.ToString());
                        table.Cell().AlignRight().Text($"{linea.PrecioUnitario:N2} €");
                        table.Cell().AlignRight().Text($"{linea.Subtotal:N2} €");
                    }
                });

                column.Item().AlignRight().Column(totalesColumn =>
                {
                    totalesColumn.Item().Text($"Base imponible: {_snapshot.BaseImponible:N2} €");
                    totalesColumn.Item().Text($"IVA ({_snapshot.PorcentajeIVA:N0}%): {_snapshot.TotalIVA:N2} €");
                    totalesColumn.Item().Text($"Total: {_snapshot.Total:N2} €").FontSize(12).Bold();
                });
            });
        }
    }
}
