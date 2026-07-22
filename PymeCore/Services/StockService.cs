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
            // 1) Busca el producto al que pertenece el movimiento; si no existe, corta aquí devolviendo el error
            var producto = await _context.Productos.FindAsync(movimiento.ProductoId);
            if (producto is null) return "Producto no encontrado.";

            // 2) Traduce el tipo de movimiento (Entrada/Salida/Devolucion/Ajuste) a un número con signo:
            // positivo si suma stock, negativo si resta
            int delta = CalcularDelta(movimiento.Tipo, movimiento.Cantidad);

            // 3) Si no se permite negativo (por defecto no se permite) y aplicar el delta dejaría
            // el stock por debajo de 0, corta aquí sin guardar nada
            if (!permitirNegativo && producto.StockActual + delta < 0)
                return "El stock no puede quedar en negativo.";

            // 4) Si pasa la validación: se sella la fecha del movimiento en el momento de guardarlo...
            movimiento.Fecha = DateTime.UtcNow;
            // ...y se actualiza el stock del producto sumando/restando el delta calculado en el paso 2
            producto.StockActual += delta;

            // 5) Se guarda el movimiento como registro histórico y se persisten ambos cambios
            // (el nuevo StockActual del producto y el nuevo MovimientoStock) en la misma llamada a SaveChangesAsync
            _context.MovimientosStock.Add(movimiento);
            await _context.SaveChangesAsync();
            return null;
        }

        // Convierte el tipo de movimiento en el signo/valor que se le suma al stock actual del producto
        private static int CalcularDelta(TipoMovimiento tipo, int cantidad) => tipo switch
        {
            TipoMovimiento.Entrada    => cantidad,  // entra stock -> suma
            TipoMovimiento.Devolucion => cantidad,  // vuelve stock -> suma
            TipoMovimiento.Salida     => -cantidad, // sale stock -> resta (se invierte el signo)
            TipoMovimiento.Ajuste     => cantidad,  // cantidad ya viene con signo: positivo suma, negativo resta
            _ => 0                                  // tipo no reconocido -> no afecta al stock
        };
    }
}
