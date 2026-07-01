using Microsoft.EntityFrameworkCore;
using PymeCore.Data;
using PymeCore.Models;

namespace PymeCore.Services
{
    public class FacturaService
    {
        private readonly ApplicationDbContext _context;

        public FacturaService(ApplicationDbContext context)
        {
            _context = context;
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
            var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.Id == pedidoId);

            if (pedido is null)
                return (false, "Pedido no encontrado.", null);

            if (pedido.Estado != EstadoPedido.Completado)
                return (false, $"Solo se puede facturar un pedido en estado Completado (estado actual: {pedido.Estado}).", null);

            if (pedido.FacturaId is not null)
                return (false, "Este pedido ya tiene una factura generada.", null);

            const decimal porcentajeIva = 21m;
            var baseImponible = pedido.Total;
            var totalIva = Math.Round(baseImponible * porcentajeIva / 100m, 2);

            var factura = new Factura
            {
                Numero        = await GenerarNumeroAsync(),
                ClienteId     = pedido.ClienteId,
                PedidoId      = pedido.Id,
                FechaEmision  = DateTime.UtcNow,
                Estado        = EstadoFactura.Pendiente,
                BaseImponible = baseImponible,
                PorcentajeIVA = porcentajeIva,
                TotalIVA      = totalIva,
                Total         = baseImponible + totalIva
            };

            _context.Facturas.Add(factura);
            await _context.SaveChangesAsync();

            pedido.FacturaId = factura.Id;
            await _context.SaveChangesAsync();

            return (true, null, factura);
        }

        public async Task<(bool Ok, string? Error)> CambiarEstadoAsync(int facturaId, EstadoFactura nuevoEstado)
        {
            var factura = await _context.Facturas.FirstOrDefaultAsync(f => f.Id == facturaId);

            if (factura is null)
                return (false, "Factura no encontrada.");

            var transicionValida = (factura.Estado, nuevoEstado) switch
            {
                (EstadoFactura.Pendiente, EstadoFactura.Pagada)  => true,
                (EstadoFactura.Pendiente, EstadoFactura.Anulada) => true,
                _                                                  => false
            };

            if (!transicionValida)
                return (false, $"No se puede cambiar de {factura.Estado} a {nuevoEstado}.");

            factura.Estado = nuevoEstado;
            await _context.SaveChangesAsync();
            return (true, null);
        }
    }
}
