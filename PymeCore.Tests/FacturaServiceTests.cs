using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using Xunit;

namespace PymeCore.Tests
{
    // GenerarDesdePedidoAsync abre siempre una transaccion (Database.BeginTransactionAsync), no
    // soportada por el proveedor InMemory de EF Core. Por eso, igual que en PedidoServiceTests, los
    // tests que pasan por ahi usan SqliteContextoDePrueba; el resto (lecturas, GenerarNumeroAsync,
    // CambiarEstadoAsync que no abre transaccion) usan DbContextFactory (InMemory).
    public class FacturaServiceTests
    {
        private static IOptions<EmpresaOptions> EmpresaValida() => Options.Create(new EmpresaOptions
        {
            Nombre = "PymeCore S.L.",
            Cif = "J16793500",
            Direccion = "C. Caleruega 31, Burgos"
        });

        private static FacturaService CrearService(ApplicationDbContext context, IOptions<EmpresaOptions>? empresa = null) =>
            new(context, new FacturaSnapshotService(context, empresa ?? EmpresaValida()));

        private static async Task<Cliente> CrearClienteAsync(ApplicationDbContext context, string nif = "12345678A")
        {
            var cliente = new Cliente { Nombre = "Cliente de Prueba", Nif = nif, Email = "cliente@test.com", Telefono = "600123456", Direccion = "Calle Falsa 123" };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            return cliente;
        }

        private static async Task<Producto> CrearProductoAsync(ApplicationDbContext context, string sku = "TES-0001")
        {
            var proveedor = new Proveedor { Nombre = "Proveedor de Prueba", Cif = Guid.NewGuid().ToString("N")[..9], Email = "proveedor@test.com" };
            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();
            var producto = new Producto { Nombre = "Producto de Prueba", Sku = sku, PrecioCoste = 10m, PrecioVenta = 20m, ProveedorId = proveedor.Id };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();
            return producto;
        }

        private static async Task<Presupuesto> CrearPresupuestoAsync(ApplicationDbContext context, int clienteId)
        {
            var presupuesto = new Presupuesto { Numero = $"PRES-{Guid.NewGuid():N}"[..15], ClienteId = clienteId, Estado = EstadoPresupuesto.Aceptado };
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            return presupuesto;
        }

        private static async Task<Pedido> CrearPedidoAsync(ApplicationDbContext context, int clienteId, int presupuestoOrigenId, EstadoPedido estado, decimal total = 0m, int? facturaId = null, params (Producto Producto, int Cantidad)[] lineas)
        {
            var pedido = new Pedido { Numero = $"PED-{Guid.NewGuid():N}"[..12], ClienteId = clienteId, PresupuestoOrigenId = presupuestoOrigenId, Estado = estado, Total = total, FacturaId = facturaId };
            context.Pedidos.Add(pedido);
            await context.SaveChangesAsync();

            foreach (var (producto, cantidad) in lineas)
                context.LineasPedido.Add(new LineaPedido { PedidoId = pedido.Id, ProductoId = producto.Id, Cantidad = cantidad, PrecioUnitario = producto.PrecioVenta, Subtotal = producto.PrecioVenta * cantidad });
            if (lineas.Length > 0)
                await context.SaveChangesAsync();

            return pedido;
        }

        // Atajo para montar un Pedido Completado con una linea, listo para facturar.
        private static async Task<(Cliente Cliente, Producto Producto, Pedido Pedido)> CrearPedidoFacturableAsync(ApplicationDbContext context, int cantidad = 2)
        {
            var cliente = await CrearClienteAsync(context);
            var producto = await CrearProductoAsync(context);
            var presupuesto = await CrearPresupuestoAsync(context, cliente.Id);
            var pedido = await CrearPedidoAsync(context, cliente.Id, presupuesto.Id, EstadoPedido.Completado, total: producto.PrecioVenta * cantidad, lineas: (producto, cantidad));
            return (cliente, producto, pedido);
        }

        // ---------- Generacion del numero correlativo ----------

        [Fact]
        public async Task GenerarNumeroAsync_SinFacturasPrevias_EmpiezaEn001()
        {
            using var context = DbContextFactory.Create();
            var service = CrearService(context);
            var año = DateTime.UtcNow.Year;

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"FAC-{año}-001", numero);
        }

        [Fact]
        public async Task GenerarNumeroAsync_ConFacturasDelMismoAño_ContinuaElCorrelativo()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var año = DateTime.UtcNow.Year;
            context.Facturas.Add(new Factura { Numero = $"FAC-{año}-004", ClienteId = cliente.Id, PedidoId = 1 });
            await context.SaveChangesAsync();
            var service = CrearService(context);

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"FAC-{año}-005", numero);
        }

        [Fact]
        public async Task GenerarNumeroAsync_IgnoraFacturasDeAñosAnteriores()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            context.Facturas.Add(new Factura { Numero = "FAC-2020-099", ClienteId = cliente.Id, PedidoId = 1 });
            await context.SaveChangesAsync();
            var service = CrearService(context);
            var año = DateTime.UtcNow.Year;

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"FAC-{año}-001", numero);
        }

        [Fact]
        public async Task GenerarNumeroAsync_IgnoraNumerosConFormatoInesperadoTrasElPrefijo()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var año = DateTime.UtcNow.Year;
            context.Facturas.Add(new Factura { Numero = $"FAC-{año}-ABC", ClienteId = cliente.Id, PedidoId = 1 });
            await context.SaveChangesAsync();
            var service = CrearService(context);

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"FAC-{año}-001", numero);
        }

        // ---------- GenerarDesdePedidoAsync: validaciones previas ----------

        [Fact]
        public async Task GenerarDesdePedidoAsync_ConPedidoInexistente_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var service = CrearService(context);

            var (ok, error, factura) = await service.GenerarDesdePedidoAsync(999);

            Assert.False(ok);
            Assert.Equal("Pedido no encontrado.", error);
            Assert.Null(factura);
        }

        [Fact]
        public async Task GenerarDesdePedidoAsync_ConPedidoNoCompletado_DevuelveError()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Pendiente);
            var service = CrearService(db.Context);

            var (ok, error, factura) = await service.GenerarDesdePedidoAsync(pedido.Id);

            Assert.False(ok);
            Assert.Equal("Solo se puede facturar un pedido en estado Completado (estado actual: Pendiente).", error);
            Assert.Null(factura);
        }

        [Fact]
        public async Task GenerarDesdePedidoAsync_ConPedidoYaFacturado_DevuelveError()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado, facturaId: 1);
            var service = CrearService(db.Context);

            var (ok, error, factura) = await service.GenerarDesdePedidoAsync(pedido.Id);

            Assert.False(ok);
            Assert.Equal("Este pedido ya tiene una factura generada.", error);
            Assert.Null(factura);
        }

        [Fact]
        public async Task GenerarDesdePedidoAsync_ConPedidoSinLineas_DevuelveError()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado);
            var service = CrearService(db.Context);

            var (ok, error, factura) = await service.GenerarDesdePedidoAsync(pedido.Id);

            Assert.False(ok);
            Assert.Equal("No se puede facturar un pedido sin líneas.", error);
            Assert.Null(factura);
        }

        // ---------- GenerarDesdePedidoAsync: creacion correcta ----------

        [Fact]
        public async Task GenerarDesdePedidoAsync_ConDatosValidos_CreaLaFacturaConElIvaCalculado()
        {
            using var db = SqliteContextoDePrueba.Create();
            var (cliente, _, pedido) = await CrearPedidoFacturableAsync(db.Context, cantidad: 2); // Total = 40
            var service = CrearService(db.Context);

            var (ok, error, factura) = await service.GenerarDesdePedidoAsync(pedido.Id);

            Assert.True(ok);
            Assert.Null(error);
            Assert.NotNull(factura);
            Assert.Equal(cliente.Id, factura!.ClienteId);
            Assert.Equal(pedido.Id, factura.PedidoId);
            Assert.Equal(EstadoFactura.Pendiente, factura.Estado);
            Assert.Equal(40m, factura.BaseImponible);
            Assert.Equal(21m, factura.PorcentajeIVA);
            Assert.Equal(8.4m, factura.TotalIVA); // 40 * 21 / 100
            Assert.Equal(48.4m, factura.Total);
            Assert.StartsWith("FAC-", factura.Numero);
        }

        [Fact]
        public async Task GenerarDesdePedidoAsync_ConDatosValidos_MarcaElPedidoComoFacturado()
        {
            using var db = SqliteContextoDePrueba.Create();
            var (_, _, pedido) = await CrearPedidoFacturableAsync(db.Context);
            var service = CrearService(db.Context);

            var (_, _, factura) = await service.GenerarDesdePedidoAsync(pedido.Id);

            using var lectura = db.NuevoContexto();
            var pedidoActualizado = await lectura.Pedidos.FindAsync(pedido.Id);
            Assert.Equal(factura!.Id, pedidoActualizado!.FacturaId);
        }

        [Fact]
        public async Task GenerarDesdePedidoAsync_ConDatosValidos_CreaElSnapshotHistorico()
        {
            using var db = SqliteContextoDePrueba.Create();
            var (_, _, pedido) = await CrearPedidoFacturableAsync(db.Context);
            var service = CrearService(db.Context);

            var (_, _, factura) = await service.GenerarDesdePedidoAsync(pedido.Id);

            using var lectura = db.NuevoContexto();
            var snapshot = Assert.Single(lectura.FacturaSnapshots);
            Assert.Equal(factura!.Id, snapshot.FacturaId);
            Assert.Contains(factura.Numero, snapshot.DatosJson); // el contenido detallado se prueba en FacturaSnapshotServiceTests
        }

        // Si faltan los datos fiscales de la empresa, FacturaSnapshotService.Crear rechaza generar
        // el snapshot; como esto ocurre DENTRO de la transaccion y antes del Commit, el rollback
        // implicito de "await using var transaction" deshace tambien el Add(factura) anterior: no
        // debe quedar ninguna factura huerfana (sin snapshot) en la base de datos.
        [Fact]
        public async Task GenerarDesdePedidoAsync_ConDatosDeEmpresaIncompletos_NoDejaNingunaFacturaGuardada()
        {
            using var db = SqliteContextoDePrueba.Create();
            var (_, _, pedido) = await CrearPedidoFacturableAsync(db.Context);
            var empresaIncompleta = Options.Create(new EmpresaOptions { Nombre = "", Cif = "J16793500", Direccion = "Calle" });
            var service = CrearService(db.Context, empresaIncompleta);

            var (ok, error, factura) = await service.GenerarDesdePedidoAsync(pedido.Id);

            Assert.False(ok);
            Assert.Contains("nombre", error);
            Assert.Null(factura);
            using var lectura = db.NuevoContexto();
            Assert.Empty(lectura.Facturas);
            Assert.Empty(lectura.FacturaSnapshots);
            Assert.Null((await lectura.Pedidos.FindAsync(pedido.Id))!.FacturaId); // tampoco queda marcado como facturado
        }

        // Restriccion real de EF/BD: builder.Entity<Factura>().HasIndex(f => f.Numero).IsUnique().
        [Fact]
        public async Task IndiceUnicoDeNumero_RechazaDosFacturasConElMismoNumeroAlGuardarEnElContexto()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedidoUno = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado);
            var pedidoDos = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado);
            db.Context.Facturas.Add(new Factura { Numero = "FAC-2026-001", ClienteId = cliente.Id, PedidoId = pedidoUno.Id });
            await db.Context.SaveChangesAsync();

            db.Context.Facturas.Add(new Factura { Numero = "FAC-2026-001", ClienteId = cliente.Id, PedidoId = pedidoDos.Id });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        }

        // Restriccion real de EF/BD: builder.Entity<Factura>().HasIndex(f => f.PedidoId).IsUnique()
        // (relacion 1 a 1 Pedido<->Factura). Confirma que la BD tambien rechazaria dos facturas para
        // el mismo pedido si, por lo que sea, la comprobacion "pedido.FacturaId is not null" se
        // saltara.
        [Fact]
        public async Task IndiceUnicoDePedidoId_RechazaDosFacturasParaElMismoPedidoAlGuardarEnElContexto()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado);
            db.Context.Facturas.Add(new Factura { Numero = "FAC-2026-001", ClienteId = cliente.Id, PedidoId = pedido.Id });
            await db.Context.SaveChangesAsync();

            // Se usa un contexto nuevo para la segunda factura: con el mismo contexto, el rastreador
            // de cambios de EF Core intenta mantener la relacion 1 a 1 Pedido<->Factura en memoria y
            // lanza su propia excepcion de seguimiento antes de llegar siquiera a la BD, en vez de
            // dejar que sea el indice unico real el que la rechace.
            using var otroContexto = db.NuevoContexto();
            otroContexto.Facturas.Add(new Factura { Numero = "FAC-2026-002", ClienteId = cliente.Id, PedidoId = pedido.Id });

            await Assert.ThrowsAsync<DbUpdateException>(() => otroContexto.SaveChangesAsync());
        }

        // ---------- CambiarEstadoAsync ----------

        [Fact]
        public async Task CambiarEstadoAsync_ConFacturaInexistente_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var service = CrearService(context);

            var (ok, error) = await service.CambiarEstadoAsync(999, EstadoFactura.Pagada);

            Assert.False(ok);
            Assert.Equal("Factura no encontrada.", error);
        }

        [Theory]
        [InlineData(EstadoFactura.Pagada)]
        [InlineData(EstadoFactura.Anulada)]
        public async Task CambiarEstadoAsync_DesdePendiente_PermitePagarOAnular(EstadoFactura nuevoEstado)
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var factura = new Factura { Numero = "FAC-2026-001", ClienteId = cliente.Id, PedidoId = 1, Estado = EstadoFactura.Pendiente };
            context.Facturas.Add(factura);
            await context.SaveChangesAsync();
            var service = CrearService(context);

            var (ok, error) = await service.CambiarEstadoAsync(factura.Id, nuevoEstado);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal(nuevoEstado, (await context.Facturas.FindAsync(factura.Id))!.Estado);
        }

        // Ninguna transicion sale de Pagada ni de Anulada: son estados finales.
        [Theory]
        [InlineData(EstadoFactura.Pagada, EstadoFactura.Anulada)]
        [InlineData(EstadoFactura.Pagada, EstadoFactura.Pendiente)]
        [InlineData(EstadoFactura.Anulada, EstadoFactura.Pagada)]
        [InlineData(EstadoFactura.Anulada, EstadoFactura.Pendiente)]
        [InlineData(EstadoFactura.Pendiente, EstadoFactura.Pendiente)]
        public async Task CambiarEstadoAsync_ConTransicionNoContemplada_DevuelveErrorYNoCambiaElEstado(EstadoFactura estadoInicial, EstadoFactura nuevoEstado)
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var factura = new Factura { Numero = "FAC-2026-001", ClienteId = cliente.Id, PedidoId = 1, Estado = estadoInicial };
            context.Facturas.Add(factura);
            await context.SaveChangesAsync();
            var service = CrearService(context);

            var (ok, error) = await service.CambiarEstadoAsync(factura.Id, nuevoEstado);

            Assert.False(ok);
            Assert.Equal($"No se puede cambiar de {estadoInicial} a {nuevoEstado}.", error);
            Assert.Equal(estadoInicial, (await context.Facturas.FindAsync(factura.Id))!.Estado);
        }

        // ---------- Busqueda / listado ----------

        [Fact]
        public async Task GetAllAsync_DevuelveTodasOrdenadasPorFechaEmisionDescendenteConCliente()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var antigua = new Factura { Numero = "FAC-2026-001", ClienteId = cliente.Id, PedidoId = 1, FechaEmision = new DateTime(2026, 1, 1) };
            var reciente = new Factura { Numero = "FAC-2026-002", ClienteId = cliente.Id, PedidoId = 2, FechaEmision = new DateTime(2026, 6, 1) };
            context.Facturas.AddRange(antigua, reciente);
            await context.SaveChangesAsync();
            var service = CrearService(context);

            var resultado = await service.GetAllAsync();

            Assert.Equal(2, resultado.Count);
            Assert.Equal("FAC-2026-002", resultado[0].Numero);
            Assert.NotNull(resultado[0].Cliente);
        }

        [Fact]
        public async Task GetByIdAsync_ConIdInexistente_DevuelveNull()
        {
            using var context = DbContextFactory.Create();
            var service = CrearService(context);

            var resultado = await service.GetByIdAsync(999);

            Assert.Null(resultado);
        }

        [Fact]
        public async Task GetByIdAsync_ConIdExistente_IncluyeClienteYPedido()
        {
            using var db = SqliteContextoDePrueba.Create();
            var (_, _, pedido) = await CrearPedidoFacturableAsync(db.Context);
            var service = CrearService(db.Context);
            var (_, _, factura) = await service.GenerarDesdePedidoAsync(pedido.Id);

            var resultado = await CrearService(db.NuevoContexto()).GetByIdAsync(factura!.Id);

            Assert.NotNull(resultado!.Cliente);
            Assert.NotNull(resultado.Pedido);
        }
    }
}
