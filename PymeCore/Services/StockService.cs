using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        //
        // Resta atómica: la comprobación de "no negativo" y la resta ocurren en la MISMA
        // sentencia UPDATE (ExecuteUpdateAsync), no en un SELECT + cálculo en memoria + UPDATE
        // como antes. Así, si dos peticiones intentan descontar el mismo producto a la vez,
        // PostgreSQL bloquea la fila para la primera y obliga a la segunda a esperar; cuando
        // le toca su turno, vuelve a evaluar la condición contra el valor YA actualizado por
        // la primera, en vez de contra un valor leído de antemano que ya está desactualizado.
        public async Task<string?> RegistrarMovimientoAsync(MovimientoStock movimiento, bool permitirNegativo = false)
        {
            // 1) Traduce el tipo de movimiento (Entrada/Salida/Devolucion/Ajuste) a un número con signo:
            // positivo si suma stock, negativo si resta
            int delta = CalcularDelta(movimiento.Tipo, movimiento.Cantidad);

            // 2) Construye la consulta: siempre filtra por el producto; si no se permite negativo
            // (el caso normal), añade también la condición de que el resultado no baje de 0 —
            // esa condición se evalúa en la base de datos, en el mismo instante de la resta.
            var query = _context.Productos.Where(p => p.Id == movimiento.ProductoId);
            if (!permitirNegativo)
                query = query.Where(p => p.StockActual + delta >= 0);

            // 3) La resta (ExecuteUpdateAsync) y el guardado del MovimientoStock histórico
            // son dos operaciones separadas contra la BD. Si quien nos llama ya abrió una
            // transacción (ej. PedidoService al completar un pedido), no abrimos otra encima
            // (fallaría) — ya quedan protegidas por esa transacción externa. Si no, abrimos
            // la nuestra, para que restar el stock y registrar el movimiento sean todo o nada.
            await using var tx = _context.Database.CurrentTransaction is null
                ? await _context.Database.BeginTransactionAsync()
                : null;

            // 4) Ejecuta la resta directamente en la base de datos, sin cargar el producto en memoria.
            var filasAfectadas = await query.ExecuteUpdateAsync(s =>
                s.SetProperty(p => p.StockActual, p => p.StockActual + delta));

            // 5) Si no se actualizó ninguna fila, puede ser porque el producto no existe,
            // o porque la condición de stock no se cumplió (no había suficiente).
            if (filasAfectadas == 0)
            {
                var existe = await _context.Productos.AnyAsync(p => p.Id == movimiento.ProductoId);
                return existe ? "El stock no puede quedar en negativo." : "Producto no encontrado.";
            }

            // 6) Solo si la resta tuvo éxito, se registra el movimiento como histórico.
            movimiento.Fecha = DateTime.UtcNow;
            _context.MovimientosStock.Add(movimiento);
            await _context.SaveChangesAsync();

            if (tx is not null)
                await tx.CommitAsync();

            return null;
        }

        // Convierte el tipo de movimiento en el signo/valor que se le suma al stock actual del producto
        private static int CalcularDelta(TipoMovimiento tipo, int cantidad) => tipo switch
        {
            TipoMovimiento.Entrada => cantidad,  // entra stock -> suma
            TipoMovimiento.Devolucion => cantidad,  // vuelve stock -> suma
            TipoMovimiento.Salida => -cantidad, // sale stock -> resta (se invierte el signo)
            TipoMovimiento.Ajuste => cantidad,  // cantidad ya viene con signo: positivo suma, negativo resta
            _ => 0                                  // tipo no reconocido -> no afecta al stock
        };
    }
}
