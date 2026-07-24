using Microsoft.EntityFrameworkCore;
using Npgsql;
using PymeCore.Data;
using PymeCore.Models;

namespace PymeCore.Services
{
    public class PedidoService
    {
        private readonly ApplicationDbContext _context;
        private readonly StockService _stockService;

        public PedidoService(ApplicationDbContext context, StockService stockService)
        {
            _context = context;
            _stockService = stockService;
        }

        public async Task<List<Pedido>> GetAllAsync()
        {
            return await _context.Pedidos
                .Include(p => p.Cliente)
                .OrderByDescending(p => p.Fecha)
                .ToListAsync();
        }

        public async Task<Pedido?> GetByIdAsync(int id)
        {
            return await _context.Pedidos
                .Include(p => p.Cliente)
                .Include(p => p.PresupuestoOrigen)
                .Include(p => p.Lineas)
                    .ThenInclude(l => l.Producto)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<string> GenerarNumeroAsync()
        {
            var año = DateTime.UtcNow.Year;
            var prefix = $"PED-{año}-";

            var numeros = await _context.Pedidos
                .Where(p => p.Numero.StartsWith(prefix))
                .Select(p => p.Numero)
                .ToListAsync();

            var siguiente = numeros
                .Select(n => int.TryParse(n[prefix.Length..], out var num) ? num : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            return $"{prefix}{siguiente:D3}";
        }

        // Genera el número y lo guarda; si otra petición se adelantó con el mismo número
        // (choque detectado por el índice único IX_Pedidos_Numero), regenera y reintenta
        // en vez de dejar reventar la petición con un error de base de datos.
        private async Task GuardarConNumeroUnicoAsync(Pedido pedido)
        {
            const int maxIntentos = 3;

            for (var intento = 1; ; intento++)
            {
                pedido.Numero = await GenerarNumeroAsync();
                _context.Pedidos.Add(pedido);

                try
                {
                    await _context.SaveChangesAsync();
                    return;
                }
                catch (DbUpdateException ex) when (EsNumeroDuplicado(ex) && intento < maxIntentos)
                {
                    _context.Entry(pedido).State = EntityState.Detached;
                }
            }
        }

        private static bool EsNumeroDuplicado(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_Pedidos_Numero" };

        public async Task<(bool Ok, string? Error, Pedido? Pedido)> CrearDesdePresupuestoAsync(int presupuestoId)
        {
            var presupuesto = await _context.Presupuestos
                .Include(p => p.Lineas)
                .FirstOrDefaultAsync(p => p.Id == presupuestoId);

            if (presupuesto is null)
                return (false, "Presupuesto no encontrado.", null);

            if (presupuesto.Estado != EstadoPresupuesto.Aceptado)
                return (false, $"Solo se puede convertir un presupuesto en estado Aceptado (estado actual: {presupuesto.Estado}).", null);

            if (presupuesto.PedidoId is not null)
                return (false, "Este presupuesto ya fue convertido en pedido.", null);

            var pedido = new Pedido
            {
                ClienteId           = presupuesto.ClienteId,
                PresupuestoOrigenId = presupuesto.Id,
                Fecha               = DateTime.UtcNow,
                Estado              = EstadoPedido.Pendiente,
                Observaciones       = presupuesto.Observaciones,
                Total               = presupuesto.Total
            };

            await GuardarConNumeroUnicoAsync(pedido);

            foreach (var linea in presupuesto.Lineas)
            {
                _context.LineasPedido.Add(new LineaPedido
                {
                    PedidoId       = pedido.Id,
                    ProductoId     = linea.ProductoId,
                    Descripcion    = linea.Descripcion,
                    Cantidad       = linea.Cantidad,
                    PrecioUnitario = linea.PrecioUnitario,
                    Subtotal       = linea.Subtotal
                });
            }

            presupuesto.PedidoId = pedido.Id;
            await _context.SaveChangesAsync();

            return (true, null, pedido);
        }

        public async Task<(bool Ok, string? Error)> CambiarEstadoAsync(int pedidoId, EstadoPedido nuevoEstado)
        {
            var pedido = await _context.Pedidos
                .FirstOrDefaultAsync(p => p.Id == pedidoId);

            if (pedido is null)
                return (false, "Pedido no encontrado.");


            var transicionValida = (pedido.Estado, nuevoEstado) switch
            {
                (EstadoPedido.Pendiente,     EstadoPedido.EnPreparacion) => true,
                (EstadoPedido.Pendiente,     EstadoPedido.Cancelado)     => true,
                (EstadoPedido.EnPreparacion, EstadoPedido.Completado)    => true,
                (EstadoPedido.EnPreparacion, EstadoPedido.Cancelado)     => true,
                _                                                         => false
            };

            if (!transicionValida)
                return (false, $"No se puede cambiar de {pedido.Estado} a {nuevoEstado}.");


            if (nuevoEstado == EstadoPedido.Completado)
            {

                await _context.Entry(pedido).Collection(p => p.Lineas).Query()
                    .Include(l => l.Producto)
                    .LoadAsync();

                // Validación bloqueante: se revisan TODAS las líneas antes de mover una
                // sola unidad de stock. Si falta stock en cualquiera, se corta acá y no
                // se llega a abrir la transacción.
                var lineasSinStock = pedido.Lineas
                    .Where(l => (l.Producto?.StockActual ?? 0) < l.Cantidad)
                    .Select(l => $"{l.Producto?.Nombre}: stock insuficiente ({l.Producto?.StockActual} disponible, {l.Cantidad} necesario).")
                    .ToList();

                if (lineasSinStock.Count > 0)
                    return (false, "No se puede completar el pedido: " + string.Join(" | ", lineasSinStock));

                // Transacción: si el descuento de stock de una línea falla a mitad de
                // camino, hay que deshacer TODO (las líneas ya descontadas + el cambio
                // de estado), no dejar el pedido a medio completar.
                await using var tx = await _context.Database.BeginTransactionAsync();
                try
                {
                    foreach (var linea in pedido.Lineas)
                    {
                        // Por cada línea del pedido se registra una Salida de stock.
                        // RegistrarMovimientoAsync devuelve string? como error en vez de
                        // lanzar excepción, por eso se convierte a excepción acá para
                        // que dispare el catch/rollback de la transacción.
                        var errorStock = await _stockService.RegistrarMovimientoAsync(new MovimientoStock
                        {
                            ProductoId = linea.ProductoId,
                            Tipo       = TipoMovimiento.Salida,
                            Cantidad   = linea.Cantidad,
                            Motivo     = $"Venta - {pedido.Numero}"
                        });

                        if (errorStock is not null)
                            throw new InvalidOperationException(errorStock);
                    }

                    pedido.Estado = EstadoPedido.Completado;
                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch
                {
                    // Rollback deshace los movimientos de stock ya guardados en este
                    // foreach; el pedido queda igual que antes de intentar completarlo.
                    await tx.RollbackAsync();
                    return (false, "Error al registrar los movimientos de stock.");
                }

                return (true, null);
            }

            // Camino simple: transiciones que no son "Completado" (ej. Cancelado)
            // no tocan stock, solo actualizan el Estado.
            pedido.Estado = nuevoEstado;
            await _context.SaveChangesAsync();
            return (true, null);
        }
    }
}
