using Microsoft.EntityFrameworkCore;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using Xunit;

namespace PymeCore.Tests
{
    // A diferencia de ClienteService/ProveedorService, ProductoService.CreateAsync abre SIEMPRE una
    // transaccion (Database.BeginTransactionAsync) y, si hay stock inicial, StockService usa
    // ExecuteUpdateAsync para restar/sumar stock de forma atomica. Ninguna de las dos cosas esta
    // soportada por el proveedor InMemory de EF Core (son funciones exclusivas de proveedores
    // relacionales). Por eso todo lo que pasa por CreateAsync usa SqliteContextoDePrueba; las
    // consultas de solo lectura (GetAllAsync, GetByIdAsync, GenerarSkuAsync, GetBajoStockAsync) y
    // UpdateAsync (que no abre transaccion) si pueden usar el InMemory de DbContextFactory.
    public class ProductoServiceTests
    {
        private static async Task<Proveedor> CrearProveedorAsync(ApplicationDbContext context, string cif = "12345678A")
        {
            var proveedor = new Proveedor { Nombre = "Proveedor de Prueba", Cif = cif, Email = "proveedor@test.com" };
            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();
            return proveedor;
        }

        private static Producto ProductoValido(int proveedorId, string sku = "TES-0001") => new()
        {
            Nombre = "Producto de Prueba",
            Sku = sku,
            PrecioCoste = 10m,
            PrecioVenta = 20m,
            StockActual = 0,
            StockMinimo = 2,
            ProveedorId = proveedorId
        };

        // ---------- Creacion y stock inicial (SQLite: CreateAsync usa transaccion + ExecuteUpdateAsync) ----------

        [Fact]
        public async Task CreateAsync_SinStockInicial_GuardaProductoSinRegistrarMovimiento()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var service = new ProductoService(db.Context, new StockService(db.Context));

            var (ok, error) = await service.CreateAsync(ProductoValido(proveedor.Id), stockInicial: 0);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Empty(db.Context.MovimientosStock);
            Assert.Equal(0, db.Context.Productos.Single().StockActual);
        }

        [Fact]
        public async Task CreateAsync_ConStockInicialPositivo_RegistraMovimientoDeEntradaYActualizaStock()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var service = new ProductoService(db.Context, new StockService(db.Context));
            var producto = ProductoValido(proveedor.Id);

            var (ok, error) = await service.CreateAsync(producto, stockInicial: 8);

            Assert.True(ok);
            Assert.Null(error);
            // Lectura con un contexto nuevo: RegistrarMovimientoAsync usa ExecuteUpdateAsync, que
            // escribe el stock directamente en la BD sin pasar por el contexto que ya tiene "producto"
            // rastreado en memoria (con su StockActual original, sin el movimiento aplicado).
            using var lectura = db.NuevoContexto();
            var guardado = await new ProductoService(lectura, new StockService(lectura)).GetByIdAsync(producto.Id);
            Assert.Equal(8, guardado!.StockActual);
            var movimiento = Assert.Single(lectura.MovimientosStock);
            Assert.Equal(TipoMovimiento.Entrada, movimiento.Tipo);
            Assert.Equal(8, movimiento.Cantidad);
            Assert.Equal(producto.Id, movimiento.ProductoId);
        }

        // ---------- Validacion de valores no negativos en el servicio ----------
        // ValidarValoresNoNegativos se ejecuta ANTES de tocar la base de datos, asi que estos tests
        // pueden usar InMemory: nunca llegan a la transaccion ni al ExecuteUpdateAsync.

        [Fact]
        public async Task CreateAsync_ConPrecioCosteNegativo_DevuelveErrorYNoGuardaNada()
        {
            using var context = DbContextFactory.Create();
            var proveedor = await CrearProveedorAsync(context);
            var service = new ProductoService(context, new StockService(context));
            var producto = ProductoValido(proveedor.Id);
            producto.PrecioCoste = -1m;

            var (ok, error) = await service.CreateAsync(producto, stockInicial: 0);

            Assert.False(ok);
            Assert.Equal("El precio de coste no puede ser negativo.", error);
            Assert.Empty(context.Productos);
        }

        [Fact]
        public async Task CreateAsync_ConPrecioVentaNegativo_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var proveedor = await CrearProveedorAsync(context);
            var service = new ProductoService(context, new StockService(context));
            var producto = ProductoValido(proveedor.Id);
            producto.PrecioVenta = -1m;

            var (ok, error) = await service.CreateAsync(producto, stockInicial: 0);

            Assert.False(ok);
            Assert.Equal("El precio de venta no puede ser negativo.", error);
        }

        [Fact]
        public async Task CreateAsync_ConStockActualNegativo_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var proveedor = await CrearProveedorAsync(context);
            var service = new ProductoService(context, new StockService(context));
            var producto = ProductoValido(proveedor.Id);
            producto.StockActual = -1;

            var (ok, error) = await service.CreateAsync(producto, stockInicial: 0);

            Assert.False(ok);
            Assert.Equal("El stock actual no puede ser negativo.", error);
        }

        [Fact]
        public async Task CreateAsync_ConStockMinimoNegativo_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var proveedor = await CrearProveedorAsync(context);
            var service = new ProductoService(context, new StockService(context));
            var producto = ProductoValido(proveedor.Id);
            producto.StockMinimo = -1;

            var (ok, error) = await service.CreateAsync(producto, stockInicial: 0);

            Assert.False(ok);
            Assert.Equal("El stock mínimo no puede ser negativo.", error);
        }

        // Documenta la asimetria frente al ViewModel (que exige precios > 0 con [Range(0.01,...)]):
        // a nivel de servicio, un precio exactamente en 0 SI se permite. Usa SQLite (no InMemory)
        // porque, al no haber error de validacion, CreateAsync SI llega a abrir su transaccion
        // (BeginTransactionAsync), que InMemory no soporta.
        [Fact]
        public async Task CreateAsync_ConPrecioCosteCero_SePermite()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var service = new ProductoService(db.Context, new StockService(db.Context));
            var producto = ProductoValido(proveedor.Id);
            producto.PrecioCoste = 0m;
            producto.PrecioVenta = 0m;

            var (ok, error) = await service.CreateAsync(producto, stockInicial: 0);

            Assert.True(ok);
            Assert.Null(error);
        }

        // A diferencia de ClienteService/ProveedorService (que comprueban ExisteNifAsync/ExisteCifAsync
        // antes de guardar y ademas capturan el error 23505 de Postgres), ProductoService no tiene
        // ningun ExisteSkuAsync ni ningun catch para duplicados. Si dos productos acaban con el mismo
        // Sku (por ejemplo, una condicion de carrera calculando el siguiente numero), CreateAsync deja
        // que la excepcion se propague sin control, en vez de devolver un error legible.
        [Fact]
        public async Task CreateAsync_ConSkuDuplicado_PropagaLaExcepcionSinCapturarla()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var service = new ProductoService(db.Context, new StockService(db.Context));
            await service.CreateAsync(ProductoValido(proveedor.Id, sku: "TES-0001"), stockInicial: 0);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.CreateAsync(ProductoValido(proveedor.Id, sku: "TES-0001"), stockInicial: 0));
        }

        // ---------- Restricciones reales de EF/BD (SQLite) ----------

        [Fact]
        public async Task IndiceUnicoDeSku_RechazaDosProductosConElMismoSkuAlGuardarEnElContexto()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            db.Context.Productos.Add(ProductoValido(proveedor.Id, sku: "TES-0001"));
            await db.Context.SaveChangesAsync();

            db.Context.Productos.Add(ProductoValido(proveedor.Id, sku: "TES-0001"));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        }

        // ProductoService.CreateAsync no comprueba que el ProveedorId exista antes de guardar: confia
        // por completo en la restriccion de clave foranea de la BD (Restrict) para rechazarlo.
        [Fact]
        public async Task CreateAsync_ConProveedorIdInexistente_LanzaExcepcionPorRestriccionDeClaveForanea()
        {
            using var db = SqliteContextoDePrueba.Create();
            var service = new ProductoService(db.Context, new StockService(db.Context));

            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.CreateAsync(ProductoValido(proveedorId: 999), stockInicial: 0));
        }

        // Los 4 CHECK constraints (PrecioCoste/PrecioVenta/StockActual/StockMinimo >= 0) son la
        // ultima barrera si alguien inserta un Producto directamente contra el DbContext, saltandose
        // ValidarValoresNoNegativos (que solo protege el camino de ProductoService).
        [Fact]
        public async Task CheckConstraintPrecioCoste_RechazaValorNegativoAlGuardarDirectamente()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var producto = ProductoValido(proveedor.Id);
            producto.PrecioCoste = -1m;

            db.Context.Productos.Add(producto);

            await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        }

        [Fact]
        public async Task CheckConstraintPrecioVenta_RechazaValorNegativoAlGuardarDirectamente()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var producto = ProductoValido(proveedor.Id);
            producto.PrecioVenta = -1m;

            db.Context.Productos.Add(producto);

            await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        }

        [Fact]
        public async Task CheckConstraintStockActual_RechazaValorNegativoAlGuardarDirectamente()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var producto = ProductoValido(proveedor.Id);
            producto.StockActual = -1;

            db.Context.Productos.Add(producto);

            await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        }

        [Fact]
        public async Task CheckConstraintStockMinimo_RechazaValorNegativoAlGuardarDirectamente()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var producto = ProductoValido(proveedor.Id);
            producto.StockMinimo = -1;

            db.Context.Productos.Add(producto);

            await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        }

        // ---------- Generacion de SKU (InMemory: GenerarSkuAsync solo hace una consulta de lectura) ----------

        [Theory]
        [InlineData("Silla de Oficina", "SIL")]
        [InlineData("Al", "ALX")]        // menos de 3 letras -> se rellena con 'X'
        [InlineData("Óleo", "OLE")]      // los acentos se ignoran, no cuentan como caracteres distintos
        [InlineData("123", "PRD")]       // sin ninguna letra -> prefijo por defecto
        public async Task GenerarSkuAsync_CalculaElPrefijoSegunElNombre(string nombre, string prefijoEsperado)
        {
            using var context = DbContextFactory.Create();
            var service = new ProductoService(context, new StockService(context));

            var sku = await service.GenerarSkuAsync(nombre);

            Assert.Equal($"{prefijoEsperado}-0001", sku);
        }

        [Fact]
        public async Task GenerarSkuAsync_NumeraDeFormaGlobalSinReiniciarPorPrefijo()
        {
            using var context = DbContextFactory.Create();
            var proveedor = await CrearProveedorAsync(context);
            context.Productos.Add(ProductoValido(proveedor.Id, sku: "ZZZ-0007"));
            await context.SaveChangesAsync();
            var service = new ProductoService(context, new StockService(context));

            var sku = await service.GenerarSkuAsync("Cualquier Cosa"); // prefijo distinto ("CUA")

            Assert.Equal("CUA-0008", sku); // sigue el maximo global (7), no reinicia por ser otro prefijo
        }

        [Fact]
        public async Task GenerarSkuAsync_IgnoraSkusConFormatoInesperadoAlCalcularElSiguienteNumero()
        {
            using var context = DbContextFactory.Create();
            var proveedor = await CrearProveedorAsync(context);
            context.Productos.Add(ProductoValido(proveedor.Id, sku: "SINGUION"));       // sin guion
            context.Productos.Add(ProductoValido(proveedor.Id, sku: "AAA-BBB-0007"));   // dos guiones
            context.Productos.Add(ProductoValido(proveedor.Id, sku: "AAA-ABC"));        // parte final no numerica
            await context.SaveChangesAsync();
            var service = new ProductoService(context, new StockService(context));

            var sku = await service.GenerarSkuAsync("Producto Nuevo");

            Assert.Equal("PRO-0001", sku); // ninguno de los 3 anteriores cuenta, arranca en 1
        }

        // ---------- Edicion (InMemory: UpdateAsync no abre transaccion propia) ----------

        // Usa SQLite: la fase de preparacion (CreateAsync con datos validos) llega a abrir su propia
        // transaccion (BeginTransactionAsync), que InMemory no soporta.
        [Fact]
        public async Task UpdateAsync_ConDatosValidos_PersisteLosCambios()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var service = new ProductoService(db.Context, new StockService(db.Context));
            var producto = ProductoValido(proveedor.Id);
            await service.CreateAsync(producto, stockInicial: 0);

            producto.Nombre = "Nombre Actualizado";
            producto.PrecioVenta = 30m;
            var (ok, error) = await service.UpdateAsync(producto);

            Assert.True(ok);
            Assert.Null(error);
            var actualizado = await service.GetByIdAsync(producto.Id);
            Assert.Equal("Nombre Actualizado", actualizado!.Nombre);
            Assert.Equal(30m, actualizado.PrecioVenta);
        }

        [Fact]
        public async Task UpdateAsync_ConPrecioNegativo_DevuelveErrorYNoModificaNada()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var service = new ProductoService(db.Context, new StockService(db.Context));
            var producto = ProductoValido(proveedor.Id);
            await service.CreateAsync(producto, stockInicial: 0);

            producto.PrecioCoste = -5m;
            var (ok, error) = await service.UpdateAsync(producto);

            Assert.False(ok);
            Assert.Equal("El precio de coste no puede ser negativo.", error);
        }

        // ---------- Bajo stock ----------

        [Fact]
        public async Task GetBajoStockAsync_DevuelveSoloLosQueTienenStockPorDebajoDelMinimo()
        {
            using var context = DbContextFactory.Create();
            var proveedor = await CrearProveedorAsync(context);
            var service = new ProductoService(context, new StockService(context));
            var bajoStock = ProductoValido(proveedor.Id, sku: "BAJ-0001");
            bajoStock.StockActual = 1;
            bajoStock.StockMinimo = 5;
            var stockSuficiente = ProductoValido(proveedor.Id, sku: "SUF-0001");
            stockSuficiente.StockActual = 10;
            stockSuficiente.StockMinimo = 5;
            context.Productos.AddRange(bajoStock, stockSuficiente);
            await context.SaveChangesAsync();

            var resultado = await service.GetBajoStockAsync();

            Assert.Single(resultado);
            Assert.Equal("BAJ-0001", resultado[0].Sku);
        }

        // ---------- Busqueda / listado ----------

        [Fact]
        public async Task GetAllAsync_SinFiltro_DevuelveTodosOrdenadosPorNombre()
        {
            using var context = DbContextFactory.Create();
            var proveedor = await CrearProveedorAsync(context);
            var zeta = ProductoValido(proveedor.Id, sku: "ZZZ-0001");
            zeta.Nombre = "Zeta";
            var alfa = ProductoValido(proveedor.Id, sku: "AAA-0001");
            alfa.Nombre = "Alfa";
            context.Productos.AddRange(zeta, alfa);
            await context.SaveChangesAsync();
            var service = new ProductoService(context, new StockService(context));

            var resultado = await service.GetAllAsync();

            Assert.Equal(2, resultado.Count);
            Assert.Equal("Alfa", resultado[0].Nombre);
        }

        [Fact]
        public async Task GetAllAsync_ConBuscarPorNombreOSku_FiltraCoincidencias()
        {
            using var context = DbContextFactory.Create();
            var proveedor = await CrearProveedorAsync(context);
            var martillo = ProductoValido(proveedor.Id, sku: "MAR-0001");
            martillo.Nombre = "Martillo";
            var otro = ProductoValido(proveedor.Id, sku: "OTR-0001");
            otro.Nombre = "Otra Cosa";
            context.Productos.AddRange(martillo, otro);
            await context.SaveChangesAsync();
            var service = new ProductoService(context, new StockService(context));

            var resultado = await service.GetAllAsync("mar-0001");

            Assert.Single(resultado);
            Assert.Equal("Martillo", resultado[0].Nombre);
        }

        // ---------- Consulta por Id ----------

        [Fact]
        public async Task GetByIdAsync_ConIdInexistente_DevuelveNull()
        {
            using var context = DbContextFactory.Create();
            var service = new ProductoService(context, new StockService(context));

            var resultado = await service.GetByIdAsync(999);

            Assert.Null(resultado);
        }

        // Usa SQLite: CreateAsync con datos validos llega a abrir su propia transaccion
        // (BeginTransactionAsync), que InMemory no soporta.
        [Fact]
        public async Task GetByIdAsync_ConIdExistente_IncluyeElProveedor()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var service = new ProductoService(db.Context, new StockService(db.Context));
            var producto = ProductoValido(proveedor.Id);
            await service.CreateAsync(producto, stockInicial: 0);

            var resultado = await service.GetByIdAsync(producto.Id);

            Assert.NotNull(resultado!.Proveedor);
            Assert.Equal(proveedor.Id, resultado.Proveedor.Id);
        }
    }
}
