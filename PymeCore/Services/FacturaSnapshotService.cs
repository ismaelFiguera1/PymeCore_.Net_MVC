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

        private const int VersionSoportada = 1;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public FacturaSnapshotService(ApplicationDbContext context, IOptions<EmpresaOptions> empresaOptions)
        {
            _context = context;
            _empresa = empresaOptions.Value;
        }

        // Devuelve null si el snapshot se creó correctamente, o un mensaje de error si no.
        // El snapshot es inmutable una vez creado, así que una factura generada con datos
        // de empresa en blanco quedaría así para siempre — por eso se corta aquí antes de
        // construirlo, en vez de generar una factura fiscalmente inválida en silencio.
        public string? Crear(Factura factura, Pedido pedido)
        {
            var errorEmpresa = ValidarDatosEmpresa();
            if (errorEmpresa is not null) return errorEmpresa;

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
                    Descripcion = string.IsNullOrWhiteSpace(l.Descripcion) ? (l.Producto?.Nombre ?? string.Empty) : l.Descripcion,
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
                Version = VersionSoportada
            };

            _context.FacturaSnapshots.Add(snapshot);
            return null;
        }

        private string? ValidarDatosEmpresa()
        {
            if (string.IsNullOrWhiteSpace(_empresa.Nombre))
                return "Faltan los datos fiscales de la empresa (nombre). Configura EmpresaOptions antes de facturar.";
            if (string.IsNullOrWhiteSpace(_empresa.Cif))
                return "Faltan los datos fiscales de la empresa (CIF). Configura EmpresaOptions antes de facturar.";
            if (string.IsNullOrWhiteSpace(_empresa.Direccion))
                return "Faltan los datos fiscales de la empresa (dirección). Configura EmpresaOptions antes de facturar.";
            return null;
        }

        // Devuelve (null, null) si la factura no tiene snapshot (caso "no encontrado" para
        // el llamador). Devuelve (null, mensaje) si el snapshot existe pero está corrupto,
        // vacío o es de una versión que no reconocemos — un error real, no un 404.
        public async Task<(FacturaSnapshotDto? Snapshot, string? Error)> GetDtoByFacturaIdAsync(int facturaId)
        {
            var snapshot = await _context.FacturaSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.FacturaId == facturaId);

            if (snapshot is null)
                return (null, null);

            if (snapshot.Version != VersionSoportada)
                return (null, $"El histórico de esta factura tiene una versión no soportada ({snapshot.Version}).");

            try
            {
                var dto = JsonSerializer.Deserialize<FacturaSnapshotDto>(snapshot.DatosJson, JsonOptions);
                return dto is null
                    ? (null, "El histórico de esta factura está vacío o corrupto.")
                    : (dto, null);
            }
            catch (JsonException)
            {
                return (null, "El histórico de esta factura está corrupto.");
            }
        }
    }
}
