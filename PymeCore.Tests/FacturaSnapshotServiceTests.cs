using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PymeCore.Data;
using PymeCore.Dtos.Facturas;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using Xunit;

namespace PymeCore.Tests
{
    public class FacturaSnapshotServiceTests
    {
        private static IOptions<EmpresaOptions> EmpresaValida() => Options.Create(new EmpresaOptions
        {
            Nombre = "PymeCore S.L.",
            Cif = "J16793500",
            Direccion = "C. Caleruega 31, Burgos"
        });

        private static async Task<Cliente> CrearClienteAsync(ApplicationDbContext context)
        {
            var cliente = new Cliente { Nombre = "Cliente de Prueba", Nif = "12345678A", Email = "cliente@test.com", Telefono = "600123456", Direccion = "Calle Falsa 123" };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            return cliente;
        }

        private static async Task<Producto> CrearProductoAsync(ApplicationDbContext context)
        {
            var proveedor = new Proveedor { Nombre = "Proveedor de Prueba", Cif = Guid.NewGuid().ToString("N")[..9], Email = "proveedor@test.com" };
            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();
            var producto = new Producto { Nombre = "Producto de Prueba", Sku = "TES-0001", PrecioCoste = 10m, PrecioVenta = 20m, ProveedorId = proveedor.Id };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();
            return producto;
        }

        private static async Task<Pedido> CrearPedidoConLineaAsync(ApplicationDbContext context, Cliente cliente, Producto producto, string? descripcionLinea = "Descripcion manual")
        {
            var presupuesto = new Presupuesto { Numero = $"PRES-{Guid.NewGuid():N}"[..15], ClienteId = cliente.Id, Estado = EstadoPresupuesto.Aceptado };
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            var pedido = new Pedido { Numero = $"PED-{Guid.NewGuid():N}"[..12], ClienteId = cliente.Id, PresupuestoOrigenId = presupuesto.Id, Estado = EstadoPedido.Completado };
            context.Pedidos.Add(pedido);
            await context.SaveChangesAsync();
            context.LineasPedido.Add(new LineaPedido { PedidoId = pedido.Id, ProductoId = producto.Id, Descripcion = descripcionLinea, Cantidad = 2, PrecioUnitario = producto.PrecioVenta, Subtotal = producto.PrecioVenta * 2 });
            await context.SaveChangesAsync();
            return await context.Pedidos
                .Include(p => p.Cliente)
                .Include(p => p.Lineas).ThenInclude(l => l.Producto)
                .FirstAsync(p => p.Id == pedido.Id);
        }

        private static Factura FacturaValida(int clienteId, int pedidoId) => new()
        {
            Numero = "FAC-2026-001",
            ClienteId = clienteId,
            PedidoId = pedidoId,
            FechaEmision = new DateTime(2026, 3, 15),
            BaseImponible = 40m,
            PorcentajeIVA = 21m,
            TotalIVA = 8.4m,
            Total = 48.4m
        };

        // ---------- Crear: validacion de datos de empresa ----------

        [Theory]
        [InlineData("", "J16793500", "Direccion", "nombre")]
        [InlineData("PymeCore S.L.", "", "Direccion", "CIF")]
        [InlineData("PymeCore S.L.", "J16793500", "", "dirección")]
        public async Task Crear_ConDatosDeEmpresaIncompletos_DevuelveErrorYNoAñadeElSnapshot(string nombre, string cif, string direccion, string campoEnElMensaje)
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var producto = await CrearProductoAsync(context);
            var pedido = await CrearPedidoConLineaAsync(context, cliente, producto);
            var factura = FacturaValida(cliente.Id, pedido.Id);
            var empresa = Options.Create(new EmpresaOptions { Nombre = nombre, Cif = cif, Direccion = direccion });
            var service = new FacturaSnapshotService(context, empresa);

            var error = service.Crear(factura, pedido);

            Assert.NotNull(error);
            Assert.Contains(campoEnElMensaje, error);
            Assert.Empty(context.FacturaSnapshots);
        }

        // ---------- Crear: contenido del snapshot ----------

        // Crear() no llama a SaveChangesAsync (eso lo hace quien la invoca, FacturaService, dentro
        // de su propia transaccion); solo hace _context.FacturaSnapshots.Add(...). Con el proveedor
        // InMemory, una entidad Add-eada pero sin guardar NO aparece al consultar el DbSet (a
        // diferencia de SQLite/Postgres reales, done el motor si veria la fila dentro de la misma
        // transaccion). Por eso aqui hace falta guardar explicitamente antes de comprobar el
        // contenido, igual que hace el codigo real.
        [Fact]
        public async Task Crear_ConDatosValidos_DevuelveNullYAñadeElSnapshotConElContenidoCorrecto()
        {
            using var context = DbContextFactory.Create();
            var cliente = new Cliente { Id = 1, Nombre = "Cliente de Prueba", Nif = "12345678A", Direccion = "Calle Falsa 123" };
            var producto = new Producto { Id = 1, Nombre = "Producto de Prueba" };
            var pedido = new Pedido
            {
                Id = 1,
                Cliente = cliente,
                Numero = "PED-2026-001",
                ClienteId = cliente.Id,
                PresupuestoOrigenId = 1,
                Lineas = new List<LineaPedido>
                {
                    new() { ProductoId = producto.Id, Producto = producto, Descripcion = "Descripcion explicita", Cantidad = 2, PrecioUnitario = 20m, Subtotal = 40m }
                }
            };
            var factura = FacturaValida(cliente.Id, pedido.Id);
            factura.Id = 7;
            var service = new FacturaSnapshotService(context, EmpresaValida());

            var error = service.Crear(factura, pedido);
            await context.SaveChangesAsync();

            Assert.Null(error);
            var snapshot = Assert.Single(context.FacturaSnapshots);
            Assert.Equal(7, snapshot.FacturaId);
            Assert.Equal(1, snapshot.Version);

            var dto = JsonSerializer.Deserialize<FacturaSnapshotDto>(snapshot.DatosJson)!;
            Assert.Equal("FAC-2026-001", dto.NumeroFactura);
            Assert.Equal("PymeCore S.L.", dto.Empresa.Nombre);
            Assert.Equal("J16793500", dto.Empresa.Cif);
            Assert.Equal("Cliente de Prueba", dto.Cliente.Nombre);
            Assert.Equal("12345678A", dto.Cliente.Nif);
            var linea = Assert.Single(dto.Lineas);
            Assert.Equal("Descripcion explicita", linea.Descripcion);
            Assert.Equal(2, linea.Cantidad);
            Assert.Equal(40m, linea.Subtotal);
            Assert.Equal(40m, dto.BaseImponible);
            Assert.Equal(8.4m, dto.TotalIVA);
            Assert.Equal(48.4m, dto.Total);
        }

        // Si la linea del pedido no tiene Descripcion propia (o esta en blanco), el snapshot usa
        // el nombre del Producto como respaldo, para que la factura nunca muestre una linea vacia.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Crear_ConLineaSinDescripcionPropia_UsaElNombreDelProductoComoRespaldo(string? descripcionLinea)
        {
            using var context = DbContextFactory.Create();
            var cliente = new Cliente { Id = 1, Nombre = "Cliente de Prueba", Nif = "12345678A" };
            var producto = new Producto { Id = 1, Nombre = "Nombre Del Producto" };
            var pedido = new Pedido
            {
                Id = 1,
                Cliente = cliente,
                Numero = "PED-2026-001",
                ClienteId = cliente.Id,
                PresupuestoOrigenId = 1,
                Lineas = new List<LineaPedido>
                {
                    new() { ProductoId = producto.Id, Producto = producto, Descripcion = descripcionLinea, Cantidad = 1, PrecioUnitario = 20m, Subtotal = 20m }
                }
            };
            var factura = FacturaValida(cliente.Id, pedido.Id);
            var service = new FacturaSnapshotService(context, EmpresaValida());

            service.Crear(factura, pedido);
            await context.SaveChangesAsync();

            var snapshot = context.FacturaSnapshots.Single();
            var dto = JsonSerializer.Deserialize<FacturaSnapshotDto>(snapshot.DatosJson)!;
            Assert.Equal("Nombre Del Producto", dto.Lineas.Single().Descripcion);
        }

        // ---------- GetDtoByFacturaIdAsync ----------

        [Fact]
        public async Task GetDtoByFacturaIdAsync_SinSnapshot_DevuelveNullYNull()
        {
            using var context = DbContextFactory.Create();
            var service = new FacturaSnapshotService(context, EmpresaValida());

            var (snapshot, error) = await service.GetDtoByFacturaIdAsync(999);

            Assert.Null(snapshot);
            Assert.Null(error);
        }

        [Fact]
        public async Task GetDtoByFacturaIdAsync_ConVersionNoSoportada_DevuelveNullYError()
        {
            using var context = DbContextFactory.Create();
            context.FacturaSnapshots.Add(new FacturaSnapshot { FacturaId = 1, DatosJson = "{}", Version = 99 });
            await context.SaveChangesAsync();
            var service = new FacturaSnapshotService(context, EmpresaValida());

            var (snapshot, error) = await service.GetDtoByFacturaIdAsync(1);

            Assert.Null(snapshot);
            Assert.Equal("El histórico de esta factura tiene una versión no soportada (99).", error);
        }

        [Fact]
        public async Task GetDtoByFacturaIdAsync_ConJsonCorrupto_DevuelveNullYError()
        {
            using var context = DbContextFactory.Create();
            context.FacturaSnapshots.Add(new FacturaSnapshot { FacturaId = 1, DatosJson = "{ esto no es json valido", Version = 1 });
            await context.SaveChangesAsync();
            var service = new FacturaSnapshotService(context, EmpresaValida());

            var (snapshot, error) = await service.GetDtoByFacturaIdAsync(1);

            Assert.Null(snapshot);
            Assert.Equal("El histórico de esta factura está corrupto.", error);
        }

        [Fact]
        public async Task GetDtoByFacturaIdAsync_ConJsonVacio_DevuelveNullYError()
        {
            using var context = DbContextFactory.Create();
            context.FacturaSnapshots.Add(new FacturaSnapshot { FacturaId = 1, DatosJson = "null", Version = 1 });
            await context.SaveChangesAsync();
            var service = new FacturaSnapshotService(context, EmpresaValida());

            var (snapshot, error) = await service.GetDtoByFacturaIdAsync(1);

            Assert.Null(snapshot);
            Assert.Equal("El histórico de esta factura está vacío o corrupto.", error);
        }

        [Fact]
        public async Task GetDtoByFacturaIdAsync_ConSnapshotValido_DevuelveElDtoDeserializado()
        {
            using var context = DbContextFactory.Create();
            var dtoOriginal = new FacturaSnapshotDto { NumeroFactura = "FAC-2026-001", Total = 48.4m };
            context.FacturaSnapshots.Add(new FacturaSnapshot { FacturaId = 1, DatosJson = JsonSerializer.Serialize(dtoOriginal), Version = 1 });
            await context.SaveChangesAsync();
            var service = new FacturaSnapshotService(context, EmpresaValida());

            var (snapshot, error) = await service.GetDtoByFacturaIdAsync(1);

            Assert.Null(error);
            Assert.NotNull(snapshot);
            Assert.Equal("FAC-2026-001", snapshot!.NumeroFactura);
            Assert.Equal(48.4m, snapshot.Total);
        }

        // ---------- Restriccion real de EF/BD: indice unico de FacturaId ----------

        // builder.Entity<FacturaSnapshot>().HasIndex(s => s.FacturaId).IsUnique() en
        // ApplicationDbContext (relacion 1 a 1 Factura<->FacturaSnapshot). En la practica
        // FacturaSnapshotService.Crear solo se llama una vez por factura (dentro de
        // GenerarDesdePedidoAsync), pero esto confirma que, si algo se saltara esa disciplina, la
        // BD igualmente rechazaria un segundo snapshot para la misma factura.
        [Fact]
        public async Task IndiceUnicoDeFacturaId_RechazaDosSnapshotsParaLaMismaFacturaAlGuardarEnElContexto()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var pedido = await CrearPedidoConLineaAsync(db.Context, cliente, producto);
            db.Context.Facturas.Add(new Factura { Numero = "FAC-2026-001", ClienteId = cliente.Id, PedidoId = pedido.Id });
            await db.Context.SaveChangesAsync();
            var facturaId = db.Context.Facturas.Single().Id;
            db.Context.FacturaSnapshots.Add(new FacturaSnapshot { FacturaId = facturaId, DatosJson = "{}", Version = 1 });
            await db.Context.SaveChangesAsync();

            // Contexto nuevo, igual que con Factura.PedidoId en FacturaServiceTests: con el mismo
            // contexto, el rastreador de cambios de EF Core interfiere con la relacion 1 a 1 antes
            // de llegar a la BD.
            using var otroContexto = db.NuevoContexto();
            otroContexto.FacturaSnapshots.Add(new FacturaSnapshot { FacturaId = facturaId, DatosJson = "{}", Version = 1 });

            await Assert.ThrowsAsync<DbUpdateException>(() => otroContexto.SaveChangesAsync());
        }
    }
}
