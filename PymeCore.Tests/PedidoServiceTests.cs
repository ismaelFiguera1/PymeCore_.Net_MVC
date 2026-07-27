using Microsoft.EntityFrameworkCore;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using Xunit;

namespace PymeCore.Tests
{
    // Cubre PedidoService excepto el descuento de stock al completar un pedido (eso ya esta en
    // PedidoServiceStockTests.cs). CrearDesdePresupuestoAsync y CambiarEstadoAsync(Completado) abren
    // siempre una transaccion (Database.BeginTransactionAsync), asi que estos tests usan
    // SqliteContextoDePrueba en vez de DbContextFactory (InMemory no soporta transacciones).
    public class PedidoServiceTests
    {
        private static async Task<Cliente> CrearClienteAsync(ApplicationDbContext context, string nif = "12345678A")
        {
            var cliente = new Cliente { Nombre = "Cliente de Prueba", Nif = nif, Email = "cliente@test.com", Telefono = "600123456" };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            return cliente;
        }

        private static async Task<Producto> CrearProductoAsync(ApplicationDbContext context, int stockActual = 100, string sku = "TES-0001")
        {
            var proveedor = new Proveedor { Nombre = "Proveedor de Prueba", Cif = Guid.NewGuid().ToString("N")[..9], Email = "proveedor@test.com" };
            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();
            var producto = new Producto { Nombre = "Producto de Prueba", Sku = sku, PrecioCoste = 10m, PrecioVenta = 20m, StockActual = stockActual, ProveedorId = proveedor.Id };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();
            return producto;
        }

        private static async Task<Presupuesto> CrearPresupuestoAsync(ApplicationDbContext context, int clienteId, EstadoPresupuesto estado = EstadoPresupuesto.Aceptado, int? pedidoId = null, string? observaciones = null, params (Producto Producto, int Cantidad)[] lineas)
        {
            var presupuesto = new Presupuesto { Numero = $"PRES-{Guid.NewGuid():N}"[..15], ClienteId = clienteId, Estado = estado, Total = 0, PedidoId = pedidoId, Observaciones = observaciones };
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();

            decimal total = 0;
            foreach (var (producto, cantidad) in lineas)
            {
                var subtotal = producto.PrecioVenta * cantidad;
                total += subtotal;
                context.LineasPresupuesto.Add(new LineaPresupuesto { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = cantidad, PrecioUnitario = producto.PrecioVenta, Subtotal = subtotal });
            }
            presupuesto.Total = total;
            await context.SaveChangesAsync();
            return presupuesto;
        }

        private static async Task<Pedido> CrearPedidoAsync(ApplicationDbContext context, EstadoPedido estado, int clienteId, int presupuestoOrigenId, params (Producto Producto, int Cantidad)[] lineas)
        {
            var pedido = new Pedido { Numero = $"PED-{Guid.NewGuid():N}"[..12], ClienteId = clienteId, PresupuestoOrigenId = presupuestoOrigenId, Estado = estado };
            context.Pedidos.Add(pedido);
            await context.SaveChangesAsync();

            foreach (var (producto, cantidad) in lineas)
            {
                context.LineasPedido.Add(new LineaPedido { PedidoId = pedido.Id, ProductoId = producto.Id, Cantidad = cantidad, PrecioUnitario = producto.PrecioVenta, Subtotal = producto.PrecioVenta * cantidad });
            }
            await context.SaveChangesAsync();
            return pedido;
        }

        // ---------- Generacion del numero correlativo ----------

        [Fact]
        public async Task GenerarNumeroAsync_SinPedidosPrevios_EmpiezaEn001()
        {
            using var context = DbContextFactory.Create();
            var service = new PedidoService(context, new StockService(context));
            var año = DateTime.UtcNow.Year;

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"PED-{año}-001", numero);
        }

        [Fact]
        public async Task GenerarNumeroAsync_ConPedidosDelMismoAño_ContinuaElCorrelativo()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var año = DateTime.UtcNow.Year;
            db.Context.Pedidos.Add(new Pedido { Numero = $"PED-{año}-004", ClienteId = cliente.Id, PresupuestoOrigenId = presupuesto.Id });
            await db.Context.SaveChangesAsync();
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"PED-{año}-005", numero);
        }

        // Si algun Numero no tiene el formato esperado tras el prefijo (parte no numerica), se
        // ignora (cuenta como 0) en vez de reventar con FormatException. Mismo comportamiento que
        // PresupuestoService.GenerarNumeroAsync (parsean el sufijo de la misma forma).
        [Fact]
        public async Task GenerarNumeroAsync_IgnoraNumerosConFormatoInesperadoTrasElPrefijo()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var año = DateTime.UtcNow.Year;
            db.Context.Pedidos.Add(new Pedido { Numero = $"PED-{año}-ABC", ClienteId = cliente.Id, PresupuestoOrigenId = presupuesto.Id });
            await db.Context.SaveChangesAsync();
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"PED-{año}-001", numero);
        }

        [Fact]
        public async Task GenerarNumeroAsync_IgnoraPedidosDeAñosAnteriores()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            db.Context.Pedidos.Add(new Pedido { Numero = "PED-2020-099", ClienteId = cliente.Id, PresupuestoOrigenId = presupuesto.Id });
            await db.Context.SaveChangesAsync();
            var service = new PedidoService(db.Context, new StockService(db.Context));
            var año = DateTime.UtcNow.Year;

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"PED-{año}-001", numero);
        }

        // ---------- CrearDesdePresupuestoAsync: validaciones previas ----------

        [Fact]
        public async Task CrearDesdePresupuestoAsync_ConPresupuestoInexistente_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var service = new PedidoService(context, new StockService(context));

            var (ok, error, pedido) = await service.CrearDesdePresupuestoAsync(999);

            Assert.False(ok);
            Assert.Equal("Presupuesto no encontrado.", error);
            Assert.Null(pedido);
        }

        [Fact]
        public async Task CrearDesdePresupuestoAsync_ConPresupuestoNoAceptado_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var presupuesto = await CrearPresupuestoAsync(context, cliente.Id, estado: EstadoPresupuesto.Enviado);
            var service = new PedidoService(context, new StockService(context));

            var (ok, error, pedido) = await service.CrearDesdePresupuestoAsync(presupuesto.Id);

            Assert.False(ok);
            Assert.Equal("Solo se puede convertir un presupuesto en estado Aceptado (estado actual: Enviado).", error);
            Assert.Null(pedido);
        }

        [Fact]
        public async Task CrearDesdePresupuestoAsync_ConPresupuestoYaConvertido_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var presupuesto = await CrearPresupuestoAsync(context, cliente.Id, estado: EstadoPresupuesto.Aceptado, pedidoId: 1);
            var service = new PedidoService(context, new StockService(context));

            var (ok, error, pedido) = await service.CrearDesdePresupuestoAsync(presupuesto.Id);

            Assert.False(ok);
            Assert.Equal("Este presupuesto ya fue convertido en pedido.", error);
            Assert.Null(pedido);
        }

        // ---------- CrearDesdePresupuestoAsync: creacion correcta ----------

        [Fact]
        public async Task CrearDesdePresupuestoAsync_ConDatosValidos_CreaElPedidoConLasLineasCopiadas()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, observaciones: "Entregar por la mañana", lineas: (producto, 3));
            db.Context.LineasPresupuesto.Single().Descripcion = "Descripcion de la linea";
            await db.Context.SaveChangesAsync();
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (ok, error, pedido) = await service.CrearDesdePresupuestoAsync(presupuesto.Id);

            Assert.True(ok);
            Assert.Null(error);
            Assert.NotNull(pedido);
            Assert.Equal(EstadoPedido.Pendiente, pedido!.Estado);
            Assert.Equal(cliente.Id, pedido.ClienteId);
            Assert.Equal(presupuesto.Id, pedido.PresupuestoOrigenId);
            Assert.Equal(presupuesto.Total, pedido.Total);
            Assert.Equal("Entregar por la mañana", pedido.Observaciones); // se copian del presupuesto
            Assert.StartsWith("PED-", pedido.Numero);
            using var lectura = db.NuevoContexto();
            var lineaPedido = Assert.Single(lectura.LineasPedido);
            Assert.Equal(producto.Id, lineaPedido.ProductoId);
            Assert.Equal(3, lineaPedido.Cantidad);
            Assert.Equal(60m, lineaPedido.Subtotal); // 3 * 20
            Assert.Equal("Descripcion de la linea", lineaPedido.Descripcion); // tambien se copia
        }

        // No hay ninguna comprobacion de "al menos una linea" en CrearDesdePresupuestoAsync (a
        // diferencia de PresupuestoService.EnviarAsync, que si bloquea enviar sin lineas). En la
        // practica no deberia ser alcanzable desde la UI, porque un presupuesto solo llega a
        // Aceptado pasando antes por Enviado (que ya exige lineas), y AgregarLinea/EliminarLinea
        // solo se permiten en Borrador. Pero a nivel de servicio, si alguien llama a este metodo
        // sobre un presupuesto Aceptado sin lineas (por ejemplo, datos manipulados directamente en
        // BD), se crea un pedido vacio sin ningun aviso.
        [Fact]
        public async Task CrearDesdePresupuestoAsync_ConPresupuestoSinLineas_CreaUnPedidoVacioSinValidarlo()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id); // sin lineas
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (ok, error, pedido) = await service.CrearDesdePresupuestoAsync(presupuesto.Id);

            Assert.True(ok);
            Assert.Null(error);
            using var lectura = db.NuevoContexto();
            Assert.NotNull(await lectura.Pedidos.FindAsync(pedido!.Id));
            Assert.Empty(lectura.LineasPedido);
        }

        [Fact]
        public async Task CrearDesdePresupuestoAsync_ConDatosValidos_MarcaElPresupuestoComoConvertido()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, lineas: (producto, 1));
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (_, _, pedido) = await service.CrearDesdePresupuestoAsync(presupuesto.Id);

            using var lectura = db.NuevoContexto();
            var presupuestoActualizado = await lectura.Presupuestos.FindAsync(presupuesto.Id);
            Assert.Equal(pedido!.Id, presupuestoActualizado!.PedidoId);
        }

        // Restriccion real de EF/BD: builder.Entity<Pedido>().HasIndex(p => p.Numero).IsUnique().
        // Igual que con Presupuesto.Numero, el reintento automatico ante colision (EsNumeroDuplicado)
        // solo se activa con PostgresException; bajo SQLite el indice se aplica igualmente, pero el
        // reintento no puede reproducirse en un test sin concurrencia real.
        [Fact]
        public async Task IndiceUnicoDeNumero_RechazaDosPedidosConElMismoNumeroAlGuardarEnElContexto()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            db.Context.Pedidos.Add(new Pedido { Numero = "PED-2026-001", ClienteId = cliente.Id, PresupuestoOrigenId = presupuesto.Id });
            await db.Context.SaveChangesAsync();

            db.Context.Pedidos.Add(new Pedido { Numero = "PED-2026-001", ClienteId = cliente.Id, PresupuestoOrigenId = presupuesto.Id });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        }

        // ---------- CambiarEstadoAsync: transiciones (sin contar el descuento de stock) ----------

        [Fact]
        public async Task CambiarEstadoAsync_ConPedidoInexistente_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var service = new PedidoService(context, new StockService(context));

            var (ok, error) = await service.CambiarEstadoAsync(999, EstadoPedido.EnPreparacion);

            Assert.False(ok);
            Assert.Equal("Pedido no encontrado.", error);
        }

        [Fact]
        public async Task CambiarEstadoAsync_DePendienteAEnPreparacion_CambiaElEstado()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, EstadoPedido.Pendiente, cliente.Id, presupuesto.Id, (producto, 2));
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (ok, error) = await service.CambiarEstadoAsync(pedido.Id, EstadoPedido.EnPreparacion);

            Assert.True(ok);
            Assert.Null(error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(EstadoPedido.EnPreparacion, (await lectura.Pedidos.FindAsync(pedido.Id))!.Estado);
        }

        [Fact]
        public async Task CambiarEstadoAsync_DeEnPreparacionACancelado_CambiaElEstadoYNoTocaElStock()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context, stockActual: 10);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, EstadoPedido.EnPreparacion, cliente.Id, presupuesto.Id, (producto, 2));
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (ok, error) = await service.CambiarEstadoAsync(pedido.Id, EstadoPedido.Cancelado);

            Assert.True(ok);
            Assert.Null(error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(EstadoPedido.Cancelado, (await lectura.Pedidos.FindAsync(pedido.Id))!.Estado);
            Assert.Equal(10, (await lectura.Productos.FindAsync(producto.Id))!.StockActual);
            Assert.Empty(lectura.MovimientosStock);
        }

        // Transiciones no contempladas en el switch de PedidoService.CambiarEstadoAsync: cualquier
        // combinacion que no sea una de las 4 explicitas se rechaza, incluidas las que "suenan"
        // razonables como reabrir un pedido Cancelado.
        [Theory]
        [InlineData(EstadoPedido.Cancelado, EstadoPedido.Pendiente)]
        [InlineData(EstadoPedido.Cancelado, EstadoPedido.EnPreparacion)]
        [InlineData(EstadoPedido.Completado, EstadoPedido.Pendiente)]
        [InlineData(EstadoPedido.Pendiente, EstadoPedido.Pendiente)]
        public async Task CambiarEstadoAsync_ConTransicionNoContemplada_DevuelveErrorYNoCambiaElEstado(EstadoPedido estadoInicial, EstadoPedido nuevoEstado)
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, estadoInicial, cliente.Id, presupuesto.Id);
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var (ok, error) = await service.CambiarEstadoAsync(pedido.Id, nuevoEstado);

            Assert.False(ok);
            Assert.Equal($"No se puede cambiar de {estadoInicial} a {nuevoEstado}.", error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(estadoInicial, (await lectura.Pedidos.FindAsync(pedido.Id))!.Estado);
        }

        // ---------- Busqueda / listado ----------

        [Fact]
        public async Task GetAllAsync_DevuelveTodosOrdenadosPorFechaDescendente()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var presupuesto = await CrearPresupuestoAsync(context, cliente.Id);
            var antiguo = new Pedido { Numero = "PED-2026-001", ClienteId = cliente.Id, PresupuestoOrigenId = presupuesto.Id, Fecha = new DateTime(2026, 1, 1) };
            var reciente = new Pedido { Numero = "PED-2026-002", ClienteId = cliente.Id, PresupuestoOrigenId = presupuesto.Id, Fecha = new DateTime(2026, 6, 1) };
            context.Pedidos.AddRange(antiguo, reciente);
            await context.SaveChangesAsync();
            var service = new PedidoService(context, new StockService(context));

            var resultado = await service.GetAllAsync();

            Assert.Equal(2, resultado.Count);
            Assert.Equal("PED-2026-002", resultado[0].Numero);
            Assert.Equal("PED-2026-001", resultado[1].Numero);
            Assert.NotNull(resultado[0].Cliente); // Include(p => p.Cliente): la vista Index lo necesita para listar
        }

        [Fact]
        public async Task GetByIdAsync_ConIdInexistente_DevuelveNull()
        {
            using var context = DbContextFactory.Create();
            var service = new PedidoService(context, new StockService(context));

            var resultado = await service.GetByIdAsync(999);

            Assert.Null(resultado);
        }

        [Fact]
        public async Task GetByIdAsync_ConIdExistente_IncluyeClientePresupuestoOrigenYLineasConProducto()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, EstadoPedido.Pendiente, cliente.Id, presupuesto.Id, (producto, 2));
            var service = new PedidoService(db.Context, new StockService(db.Context));

            var resultado = await service.GetByIdAsync(pedido.Id);

            Assert.NotNull(resultado!.Cliente);
            Assert.NotNull(resultado.PresupuestoOrigen);
            var linea = Assert.Single(resultado.Lineas);
            Assert.NotNull(linea.Producto);
        }
    }
}
