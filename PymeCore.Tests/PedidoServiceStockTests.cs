using Microsoft.EntityFrameworkCore;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using Xunit;

namespace PymeCore.Tests
{
    // PedidoService.CambiarEstadoAsync es el UNICO otro punto de la aplicacion (aparte de
    // MovimientoStockController) que descuenta stock: al completar un pedido, resta la cantidad de
    // cada linea via StockService.RegistrarMovimientoAsync. Como esa llamada usa ExecuteUpdateAsync
    // (no soportado por el proveedor InMemory), estos tests usan SqliteContextoDePrueba.
    //
    // Se monta el grafo completo Cliente -> Presupuesto -> Pedido -> LineaPedido -> Producto
    // directamente contra el contexto (sin pasar por CrearDesdePresupuestoAsync), porque
    // PresupuestoOrigenId es una FK obligatoria de Pedido: hace falta un Presupuesto real para
    // que el INSERT no viole la restriccion de clave foranea.
    public class PedidoServiceStockTests
    {
        private static async Task<Cliente> CrearClienteAsync(ApplicationDbContext context)
        {
            var cliente = new Cliente { Nombre = "Cliente de Prueba", Nif = "12345678A", Email = "cliente@test.com", Telefono = "600000000" };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            return cliente;
        }

        private static async Task<Producto> CrearProductoAsync(ApplicationDbContext context, int stockActual, string sku)
        {
            var proveedor = new Proveedor { Nombre = "Proveedor de Prueba", Cif = Guid.NewGuid().ToString("N")[..9], Email = "proveedor@test.com" };
            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();

            var producto = new Producto
            {
                Nombre = "Producto de Prueba",
                Sku = sku,
                PrecioCoste = 10m,
                PrecioVenta = 20m,
                StockActual = stockActual,
                StockMinimo = 0,
                ProveedorId = proveedor.Id
            };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();
            return producto;
        }

        // Crea un Pedido en el estado indicado, con una linea por cada (producto, cantidad) recibido.
        private static async Task<Pedido> CrearPedidoAsync(ApplicationDbContext context, EstadoPedido estado, params (Producto Producto, int Cantidad)[] lineas)
        {
            var cliente = await CrearClienteAsync(context);
            var presupuesto = new Presupuesto { Numero = $"PRE-{Guid.NewGuid():N}"[..12], ClienteId = cliente.Id, Estado = EstadoPresupuesto.Aceptado };
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();

            var pedido = new Pedido
            {
                Numero = $"PED-{Guid.NewGuid():N}"[..12],
                ClienteId = cliente.Id,
                PresupuestoOrigenId = presupuesto.Id,
                Estado = estado
            };
            context.Pedidos.Add(pedido);
            await context.SaveChangesAsync();

            foreach (var (producto, cantidad) in lineas)
            {
                context.LineasPedido.Add(new LineaPedido
                {
                    PedidoId = pedido.Id,
                    ProductoId = producto.Id,
                    Cantidad = cantidad,
                    PrecioUnitario = producto.PrecioVenta,
                    Subtotal = producto.PrecioVenta * cantidad
                });
            }
            await context.SaveChangesAsync();

            return pedido;
        }

        // ---------- Caso normal: stock suficiente ----------

        [Fact]
        public async Task CambiarEstadoAsync_ACompletado_ConStockSuficiente_DescuentaElStockDeCadaLineaYRegistraElMovimiento()
        {
            using var db = SqliteContextoDePrueba.Create();
            var productoA = await CrearProductoAsync(db.Context, stockActual: 10, sku: "AAA-0001");
            var productoB = await CrearProductoAsync(db.Context, stockActual: 5, sku: "BBB-0001");
            var pedido = await CrearPedidoAsync(db.Context, EstadoPedido.EnPreparacion, (productoA, 4), (productoB, 5));
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (ok, error) = await service.CambiarEstadoAsync(pedido.Id, EstadoPedido.Completado);

            Assert.True(ok);
            Assert.Null(error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(6, (await lectura.Productos.FindAsync(productoA.Id))!.StockActual);
            Assert.Equal(0, (await lectura.Productos.FindAsync(productoB.Id))!.StockActual); // limite exacto: permitido
            Assert.Equal(EstadoPedido.Completado, (await lectura.Pedidos.FindAsync(pedido.Id))!.Estado);
            Assert.Equal(2, lectura.MovimientosStock.Count());
            Assert.All(lectura.MovimientosStock, m =>
            {
                Assert.Equal(TipoMovimiento.Salida, m.Tipo);
                Assert.Contains(pedido.Numero, m.Motivo);
            });
        }

        // ---------- Bloqueo previo: falta stock en una linea ----------

        // La comprobacion de "todas las lineas antes de tocar nada" corta ANTES de abrir la
        // transaccion: no debe quedar ningun movimiento registrado ni cambiar el stock de NINGUN
        // producto, ni siquiera el de la linea que si tenia stock suficiente.
        [Fact]
        public async Task CambiarEstadoAsync_ACompletado_ConStockInsuficienteEnUnaLinea_NoModificaNingunProductoNiCambiaElEstado()
        {
            using var db = SqliteContextoDePrueba.Create();
            var conStock = await CrearProductoAsync(db.Context, stockActual: 10, sku: "AAA-0001");
            var sinStock = await CrearProductoAsync(db.Context, stockActual: 2, sku: "BBB-0001");
            var pedido = await CrearPedidoAsync(db.Context, EstadoPedido.EnPreparacion, (conStock, 4), (sinStock, 5));
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (ok, error) = await service.CambiarEstadoAsync(pedido.Id, EstadoPedido.Completado);

            Assert.False(ok);
            Assert.Contains("stock insuficiente", error);
            Assert.Contains(sinStock.Nombre, error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(10, (await lectura.Productos.FindAsync(conStock.Id))!.StockActual); // intacto
            Assert.Equal(2, (await lectura.Productos.FindAsync(sinStock.Id))!.StockActual);  // intacto
            Assert.Equal(EstadoPedido.EnPreparacion, (await lectura.Pedidos.FindAsync(pedido.Id))!.Estado); // no cambia
            Assert.Empty(lectura.MovimientosStock);
        }

        [Fact]
        public async Task CambiarEstadoAsync_ACompletado_ConStockInsuficienteEnVariasLineas_ListaTodosLosProductosAfectadosEnElError()
        {
            using var db = SqliteContextoDePrueba.Create();
            var productoA = await CrearProductoAsync(db.Context, stockActual: 1, sku: "AAA-0001");
            var productoB = await CrearProductoAsync(db.Context, stockActual: 1, sku: "BBB-0001");
            var pedido = await CrearPedidoAsync(db.Context, EstadoPedido.EnPreparacion, (productoA, 5), (productoB, 5));
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (ok, error) = await service.CambiarEstadoAsync(pedido.Id, EstadoPedido.Completado);

            Assert.False(ok);
            Assert.Contains(productoA.Nombre, error);
            Assert.Contains(productoB.Nombre, error);
        }

        // ---------- Hueco real detectado: el chequeo previo no suma cantidades del MISMO producto ----------
        //
        // lineasSinStock compara, POR LINEA, l.Producto.StockActual (el valor cargado UNA vez para
        // las dos lineas, porque EF Core reutiliza la misma instancia rastreada) contra l.Cantidad.
        // Si el pedido tiene dos lineas del MISMO producto que, cada una por separado, caben en el
        // stock disponible pero JUNTAS lo superan, el chequeo previo no lo detecta (nunca sim SUMA
        // las cantidades de lineas del mismo producto) y el codigo intenta completar el pedido.
        // La integridad la salva la resta atomica de StockService (segunda linea falla) + el
        // rollback de la transaccion: el pedido NO queda a medias, pero el mensaje de error ya no es
        // el descriptivo de "stock insuficiente" sino el generico de "Error al registrar los
        // movimientos de stock.".
        [Fact]
        public async Task CambiarEstadoAsync_ConDosLineasDelMismoProductoQueJuntasSuperanElStock_ElChequeoPrevioNoLoDetectaPeroLaTransaccionRevierteTodo()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 10, sku: "AAA-0001");
            // Cada linea (6) cabe individualmente en el stock cargado (10), pero juntas piden 12.
            var pedido = await CrearPedidoAsync(db.Context, EstadoPedido.EnPreparacion, (producto, 6), (producto, 6));
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (ok, error) = await service.CambiarEstadoAsync(pedido.Id, EstadoPedido.Completado);

            Assert.False(ok);
            Assert.Equal("Error al registrar los movimientos de stock.", error);
            using var lectura = db.NuevoContexto();
            // El rollback deshace TAMBIEN el descuento de la primera linea, que si se habia aplicado
            // dentro de la transaccion antes de que la segunda fallara.
            Assert.Equal(10, (await lectura.Productos.FindAsync(producto.Id))!.StockActual);
            Assert.Equal(EstadoPedido.EnPreparacion, (await lectura.Pedidos.FindAsync(pedido.Id))!.Estado);
            Assert.Empty(lectura.MovimientosStock);
        }

        // ---------- Transiciones que no completan no tocan stock ----------

        [Fact]
        public async Task CambiarEstadoAsync_ACancelado_NoTocaElStock()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 10, sku: "AAA-0001");
            var pedido = await CrearPedidoAsync(db.Context, EstadoPedido.Pendiente, (producto, 4));
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (ok, error) = await service.CambiarEstadoAsync(pedido.Id, EstadoPedido.Cancelado);

            Assert.True(ok);
            Assert.Null(error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(10, (await lectura.Productos.FindAsync(producto.Id))!.StockActual);
            Assert.Empty(lectura.MovimientosStock);
        }

        // ---------- Transicion invalida hacia Completado ----------

        // Un pedido en Pendiente no puede saltar directamente a Completado (solo EnPreparacion ->
        // Completado). Igual que con el stock insuficiente, esto se corta antes de tocar el stock.
        [Fact]
        public async Task CambiarEstadoAsync_DePendienteACompletadoDirectamente_RechazaLaTransicionYNoTocaElStock()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 10, sku: "AAA-0001");
            var pedido = await CrearPedidoAsync(db.Context, EstadoPedido.Pendiente, (producto, 4));
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (ok, error) = await service.CambiarEstadoAsync(pedido.Id, EstadoPedido.Completado);

            Assert.False(ok);
            Assert.Equal("No se puede cambiar de Pendiente a Completado.", error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(10, (await lectura.Productos.FindAsync(producto.Id))!.StockActual);
            Assert.Empty(lectura.MovimientosStock);
        }
    }
}
