using Microsoft.EntityFrameworkCore;
using Npgsql;
using PymeCore.Data;
using PymeCore.Models;

namespace PymeCore.Services
{
    public class FacturaService
    {
        private readonly ApplicationDbContext _context;
        private readonly FacturaSnapshotService _snapshotService;

        public FacturaService(ApplicationDbContext context, FacturaSnapshotService snapshotService)
        {
            _context = context;
            _snapshotService = snapshotService;
        }

        public async Task<List<Factura>> GetAllAsync()
        {
            return await _context.Facturas
                .Include(f => f.Cliente)
                .OrderByDescending(f => f.FechaEmision)
                .ToListAsync();
        }

        public async Task<Factura?> GetByIdAsync(int id)
        {
            return await _context.Facturas
                .Include(f => f.Cliente)
                .Include(f => f.Pedido)
                .FirstOrDefaultAsync(f => f.Id == id);
        }

        public async Task<string> GenerarNumeroAsync()
        {
            var año = DateTime.UtcNow.Year;
            var prefix = $"FAC-{año}-";

            var numeros = await _context.Facturas
                .Where(f => f.Numero.StartsWith(prefix))
                .Select(f => f.Numero)
                .ToListAsync();

            var siguiente = numeros
                .Select(n => int.TryParse(n[prefix.Length..], out var num) ? num : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            return $"{prefix}{siguiente:D3}";
        }

        public async Task<(bool Ok, string? Error, Factura? Factura)> GenerarDesdePedidoAsync(int pedidoId)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.Cliente)
                .Include(p => p.Lineas)
                    .ThenInclude(l => l.Producto)
                .FirstOrDefaultAsync(p => p.Id == pedidoId);

            if (pedido is null)
                return (false, "Pedido no encontrado.", null);

            if (pedido.Estado != EstadoPedido.Completado)
                return (false, $"Solo se puede facturar un pedido en estado Completado (estado actual: {pedido.Estado}).", null);

            if (pedido.FacturaId is not null)
                return (false, "Este pedido ya tiene una factura generada.", null);

            if (!pedido.Lineas.Any())
                return (false, "No se puede facturar un pedido sin líneas.", null);

            const decimal porcentajeIva = 21m;
            var baseImponible = pedido.Total;
            var totalIva = Math.Round(baseImponible * porcentajeIva / 100m, 2);

            // Bucle de reintento: si otra petición se adelantó con el mismo número de factura
            // (choque detectado por el índice único IX_Facturas_Numero), se descarta la transacción,
            // se regenera el número y se vuelve a intentar, en vez de reventar con un error de BD.
            const int maxIntentos = 3;

            for (var intento = 1; ; intento++)
            {
                var factura = new Factura
                {
                    Numero = await GenerarNumeroAsync(),
                    ClienteId = pedido.ClienteId,
                    PedidoId = pedido.Id,
                    FechaEmision = DateTime.UtcNow,
                    Estado = EstadoFactura.Pendiente,
                    BaseImponible = baseImponible,
                    PorcentajeIVA = porcentajeIva,
                    TotalIVA = totalIva,
                    Total = baseImponible + totalIva
                };

                await using var transaction = await _context.Database.BeginTransactionAsync();

                _context.Facturas.Add(factura);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (EsNumeroDuplicado(ex) && intento < maxIntentos)
                {
                    _context.Entry(factura).State = EntityState.Detached;
                    continue;
                }

                pedido.FacturaId = factura.Id;

                var errorSnapshot = _snapshotService.Crear(factura, pedido);
                if (errorSnapshot is not null)
                    return (false, errorSnapshot, null);

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return (true, null, factura);
            }
        }

        private static bool EsNumeroDuplicado(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_Facturas_Numero" };

        public async Task<(bool Ok, string? Error)> CambiarEstadoAsync(int facturaId, EstadoFactura nuevoEstado)
        {
            var factura = await _context.Facturas.FirstOrDefaultAsync(f => f.Id == facturaId);

            if (factura is null)
                return (false, "Factura no encontrada.");

            var transicionValida = (factura.Estado, nuevoEstado) switch
            {
                (EstadoFactura.Pendiente, EstadoFactura.Pagada) => true,
                (EstadoFactura.Pendiente, EstadoFactura.Anulada) => true,
                _ => false
            };

            if (!transicionValida)
                return (false, $"No se puede cambiar de {factura.Estado} a {nuevoEstado}.");

            factura.Estado = nuevoEstado;
            await _context.SaveChangesAsync();
            return (true, null);
        }
    }
}
