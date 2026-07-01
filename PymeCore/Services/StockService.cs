using Microsoft.EntityFrameworkCore;
using PymeCore.Data;
using PymeCore.Models;

namespace PymeCore.Services
{
    public class StockService
    {
        private readonly ApplicationDbContext _context;

        public StockService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<MovimientoStock>> GetHistorialPorProductoAsync(int productoId)
        {
            return await _context.MovimientosStock
                .Where(m => m.ProductoId == productoId)
                .OrderByDescending(m => m.Fecha)
                .ToListAsync();
        }

        // Devuelve null si el movimiento es válido, o un mensaje de error si no lo es.
        public async Task<string?> RegistrarMovimientoAsync(MovimientoStock movimiento, bool permitirNegativo = false)
        {
            var producto = await _context.Productos.FindAsync(movimiento.ProductoId);
            if (producto is null) return "Producto no encontrado.";

            int delta = CalcularDelta(movimiento.Tipo, movimiento.Cantidad);

            if (!permitirNegativo && producto.StockActual + delta < 0)
                return "El stock no puede quedar en negativo.";

            movimiento.Fecha = DateTime.UtcNow;
            producto.StockActual += delta;

            _context.MovimientosStock.Add(movimiento);
            await _context.SaveChangesAsync();
            return null;
        }

        private static int CalcularDelta(TipoMovimiento tipo, int cantidad) => tipo switch
        {
            TipoMovimiento.Entrada    => cantidad,
            TipoMovimiento.Devolucion => cantidad,
            TipoMovimiento.Salida     => -cantidad,
            TipoMovimiento.Ajuste     => cantidad, // positivo suma, negativo resta
            _ => 0
        };
    }
}
