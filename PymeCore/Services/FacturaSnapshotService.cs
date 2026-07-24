using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PymeCore.Data;
using PymeCore.Dtos.Facturas;
using PymeCore.Models;

namespace PymeCore.Services
{
    public class FacturaSnapshotService
    {
        private readonly ApplicationDbContext _context;
        private readonly EmpresaOptions _empresa;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public FacturaSnapshotService(ApplicationDbContext context, IOptions<EmpresaOptions> empresaOptions)
        {
            _context = context;
            _empresa = empresaOptions.Value;
        }

        public void Crear(Factura factura, Pedido pedido)
        {
            var dto = new FacturaSnapshotDto
            {
                NumeroFactura = factura.Numero,
                FechaEmision = factura.FechaEmision,
                Empresa = new EmpresaSnapshotDto
                {
                    Nombre = _empresa.Nombre,
                    Cif = _empresa.Cif,
                    Direccion = _empresa.Direccion
                },
                Cliente = new ClienteSnapshotDto
                {
                    Nombre = pedido.Cliente!.Nombre,
                    Nif = pedido.Cliente.Nif,
                    Direccion = pedido.Cliente.Direccion
                },
                Lineas = pedido.Lineas.Select(l => new FacturaLineaSnapshotDto
                {
                    Descripcion = l.Descripcion ?? l.Producto?.Nombre ?? string.Empty,
                    Cantidad = l.Cantidad,
                    PrecioUnitario = l.PrecioUnitario,
                    Subtotal = l.Subtotal
                }).ToList(),
                BaseImponible = factura.BaseImponible,
                PorcentajeIVA = factura.PorcentajeIVA,
                TotalIVA = factura.TotalIVA,
                Total = factura.Total
            };

            var snapshot = new FacturaSnapshot
            {
                FacturaId = factura.Id,
                DatosJson = JsonSerializer.Serialize(dto, JsonOptions),
                FechaCreacion = DateTime.UtcNow,
                Version = 1
            };

            _context.FacturaSnapshots.Add(snapshot);
        }

        public async Task<FacturaSnapshotDto?> GetDtoByFacturaIdAsync(int facturaId)
        {
            var snapshot = await _context.FacturaSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.FacturaId == facturaId);

            return snapshot is null
                ? null
                : JsonSerializer.Deserialize<FacturaSnapshotDto>(snapshot.DatosJson, JsonOptions);
        }
    }
}
