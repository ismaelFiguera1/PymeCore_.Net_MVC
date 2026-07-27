using Microsoft.AspNetCore.Mvc;
using PymeCore.Controllers;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using PymeCore.ViewModels.Stock;
using Xunit;

namespace PymeCore.Tests
{
    // RegistrarMovimientoAsync (llamado desde Create POST) usa ExecuteUpdateAsync, que el
    // proveedor InMemory de EF Core no soporta. Por eso, igual que en ProductosControllerTests,
    // todos estos tests usan SqliteContextoDePrueba.
    //
    // CrearController crea SIEMPRE un ApplicationDbContext nuevo (db.NuevoContexto()), como en la
    // app real (un DbContext por peticion HTTP). Cualquier lectura de verificacion posterior a una
    // accion se hace tambien con un contexto nuevo, nunca con el mismo que uso el controlador,
    // porque ExecuteUpdateAsync no pasa por su rastreador de cambios.
    public class MovimientoStockControllerTests
    {
        private static async Task<Producto> CrearProductoAsync(ApplicationDbContext context, int stockActual = 10, string sku = "TES-0001")
        {
            var proveedor = new Proveedor { Nombre = "Proveedor de Prueba", Cif = "12345678A", Email = "proveedor@test.com" };
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

        private static MovimientoStockController CrearController(SqliteContextoDePrueba db)
        {
            var context = db.NuevoContexto();
            return new MovimientoStockController(new StockService(context), new ProductoService(context, new StockService(context)));
        }

        private static MovimientoStockFormViewModel VmValido(int productoId, TipoMovimiento tipo = TipoMovimiento.Entrada, int cantidad = 5) => new()
        {
            ProductoId = productoId,
            Tipo = tipo,
            Cantidad = cantidad
        };

        // ---------- Index ----------

        [Fact]
        public async Task Index_ConProductoInexistente_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);

            var resultado = await controller.Index(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task Index_ConProductoExistente_DevuelveVistaConHistorialYProductoEnViewBag()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context);
            var controller = CrearController(db);

            var resultado = await controller.Index(producto.Id);

            var view = Assert.IsType<ViewResult>(resultado);
            Assert.IsAssignableFrom<List<MovimientoStock>>(view.Model);
            var productoEnViewBag = Assert.IsType<Producto>(controller.ViewBag.Producto);
            Assert.Equal(producto.Id, productoEnViewBag.Id);
        }

        // ---------- Create (GET) ----------

        [Fact]
        public async Task Create_Get_ConProductoInexistente_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);

            var resultado = await controller.Create(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task Create_Get_ConProductoExistente_PrecargaElViewModelConLosDatosDelProducto()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 42);
            var controller = CrearController(db);

            var resultado = await controller.Create(producto.Id);

            var view = Assert.IsType<ViewResult>(resultado);
            var vm = Assert.IsType<MovimientoStockFormViewModel>(view.Model);
            Assert.Equal(producto.Id, vm.ProductoId);
            Assert.Equal(producto.Nombre, vm.NombreProducto);
            Assert.Equal(42, vm.StockActual);
        }

        // ---------- Create (POST): validacion de cantidad segun el tipo ----------

        // Para Entrada/Salida/Devolucion, Cantidad <= 0 se rechaza a nivel de controlador, antes de
        // llegar siquiera al servicio.
        [Theory]
        [InlineData(TipoMovimiento.Entrada, 0)]
        [InlineData(TipoMovimiento.Entrada, -1)]
        [InlineData(TipoMovimiento.Salida, 0)]
        public async Task Create_Post_ConTipoDistintoDeAjusteYCantidadNoPositiva_AnadeErrorYRecargaDatosDelProducto(TipoMovimiento tipo, int cantidad)
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 10);
            var controller = CrearController(db);
            var vm = VmValido(producto.Id, tipo, cantidad);

            var resultado = await controller.Create(vm);

            var view = Assert.IsType<ViewResult>(resultado);
            var vmDevuelto = Assert.IsType<MovimientoStockFormViewModel>(view.Model);
            Assert.False(controller.ModelState.IsValid);
            Assert.Equal(producto.Nombre, vmDevuelto.NombreProducto); // se recargan aunque falle la validacion
            Assert.Equal(10, vmDevuelto.StockActual);
            using var lectura = db.NuevoContexto();
            Assert.Empty(lectura.MovimientosStock); // no llega a registrar nada
        }

        // Para Ajuste, Cantidad puede ser 0 o negativa (representa una correccion en cualquier
        // sentido): el controlador no anade el error de "cantidad debe ser mayor que cero".
        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        public async Task Create_Post_ConTipoAjusteYCantidadNoPositiva_NoAnadeElErrorDeCantidadDelControlador(int cantidad)
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 10);
            var controller = CrearController(db);
            var vm = VmValido(producto.Id, TipoMovimiento.Ajuste, cantidad);

            var resultado = await controller.Create(vm);

            Assert.IsType<RedirectToActionResult>(resultado);
        }

        // ---------- Create (POST): ModelState invalido por otros motivos ----------

        [Fact]
        public async Task Create_Post_ConModelStateInvalido_DevuelveViewYRecargaNombreYStockActual()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 7);
            var controller = CrearController(db);
            controller.ModelState.AddModelError("Tipo", "El tipo es obligatorio");
            var vm = VmValido(producto.Id);

            var resultado = await controller.Create(vm);

            var view = Assert.IsType<ViewResult>(resultado);
            var vmDevuelto = Assert.IsType<MovimientoStockFormViewModel>(view.Model);
            Assert.Equal(producto.Nombre, vmDevuelto.NombreProducto);
            Assert.Equal(7, vmDevuelto.StockActual);
        }

        // Si el producto ya no existe (por ejemplo, borrado entre el GET y el POST), la recarga de
        // datos no falla: NombreProducto y StockActual caen a sus valores por defecto.
        [Fact]
        public async Task Create_Post_ConModelStateInvalidoYProductoInexistente_RecargaValoresPorDefecto()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);
            controller.ModelState.AddModelError("Tipo", "El tipo es obligatorio");
            var vm = VmValido(productoId: 999);

            var resultado = await controller.Create(vm);

            var view = Assert.IsType<ViewResult>(resultado);
            var vmDevuelto = Assert.IsType<MovimientoStockFormViewModel>(view.Model);
            Assert.Equal(string.Empty, vmDevuelto.NombreProducto);
            Assert.Equal(0, vmDevuelto.StockActual);
        }

        // ---------- Create (POST): exito ----------

        [Fact]
        public async Task Create_Post_ConDatosValidos_RegistraElMovimientoYRedirigeAIndexConElProductoId()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 10);
            var controller = CrearController(db);
            var vm = VmValido(producto.Id, TipoMovimiento.Entrada, 5);

            var resultado = await controller.Create(vm);

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal(producto.Id, redirect.RouteValues!["productoId"]);
            using var lectura = db.NuevoContexto();
            Assert.Equal(15, (await lectura.Productos.FindAsync(producto.Id))!.StockActual);
            Assert.Single(lectura.MovimientosStock);
        }

        // ---------- Create (POST): el servicio rechaza el movimiento ----------

        // Cuando StockService devuelve un error (p.ej. "El stock no puede quedar en negativo"),
        // el controlador lo anade como error de ModelState sobre Cantidad y vuelve a la vista,
        // recargando tambien NombreProducto y StockActual con los valores reales del producto.
        [Fact]
        public async Task Create_Post_ConStockInsuficiente_AnadeErrorDelServicioYDevuelveViewConDatosRecargados()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 3);
            var controller = CrearController(db);
            var vm = VmValido(producto.Id, TipoMovimiento.Salida, 5);

            var resultado = await controller.Create(vm);

            var view = Assert.IsType<ViewResult>(resultado);
            var vmDevuelto = Assert.IsType<MovimientoStockFormViewModel>(view.Model);
            Assert.False(controller.ModelState.IsValid);
            Assert.Contains(controller.ModelState["Cantidad"]!.Errors, e => e.ErrorMessage == "El stock no puede quedar en negativo.");
            Assert.Equal(3, vmDevuelto.StockActual); // se recarga con el valor real, sin aplicar el movimiento fallido
            using var lectura = db.NuevoContexto();
            Assert.Empty(lectura.MovimientosStock);
        }
    }
}
