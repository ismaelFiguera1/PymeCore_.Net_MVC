using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using PymeCore.Controllers;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using PymeCore.ViewModels.Productos;
using Xunit;

namespace PymeCore.Tests
{
    // ProductoService depende de StockService, y Create/Edit tambien usan ProveedorService para
    // cargar el desplegable de proveedores (CargarProveedoresAsync). Como Create pasa por
    // ProductoService.CreateAsync (transaccion + posible ExecuteUpdateAsync), todos estos tests usan
    // SqliteContextoDePrueba, no el InMemory de DbContextFactory (ver ProductoServiceTests para el
    // detalle de por que).
    //
    // CrearController crea SIEMPRE un ApplicationDbContext nuevo (db.NuevoContexto()), igual que en
    // la app real: cada peticion HTTP recibe su propio DbContext por inyeccion de dependencias.
    // Por eso, cuando un test necesita simular dos peticiones (por ejemplo, Create y luego Edit),
    // llama a CrearController(db) dos veces, una por cada "peticion". Si se reutilizara el mismo
    // controlador para las dos, ambas comparten un unico rastreador de cambios, y StockService.
    // RegistrarMovimientoAsync (que usa ExecuteUpdateAsync, y por tanto no pasa por el rastreador)
    // dejaria valores desactualizados en memoria que Update() volveria a escribir mal en la BD.
    // Por el mismo motivo, cualquier lectura de verificacion posterior a una accion se hace con
    // db.NuevoContexto(), nunca con db.Context directamente.
    public class ProductosControllerTests
    {
        private static async Task<Proveedor> CrearProveedorAsync(ApplicationDbContext context, bool activo = true, string cif = "12345678A")
        {
            var proveedor = new Proveedor { Nombre = "Proveedor de Prueba", Cif = cif, Email = "proveedor@test.com", Activo = activo };
            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();
            return proveedor;
        }

        private static ProductosController CrearController(SqliteContextoDePrueba db)
        {
            var context = db.NuevoContexto();
            return new ProductosController(new ProductoService(context, new StockService(context)), new ProveedorService(context));
        }

        private static ProductoFormViewModel VmValido(int proveedorId) => new()
        {
            Nombre = "Producto de Prueba",
            Sku = "IGNORADO-9999", // se comprueba que el controlador lo descarta y genera el suyo
            PrecioCoste = 10m,
            PrecioVenta = 20m,
            StockActual = 5,
            StockMinimo = 2,
            ProveedorId = proveedorId
        };

        // ---------- Details ----------

        [Fact]
        public async Task Details_ConIdInexistente_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);

            var resultado = await controller.Details(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task Details_ConIdExistente_DevuelveViewConProductoIncluidoProveedor()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            await CrearController(db).Create(VmValido(proveedor.Id));
            using var lectura = db.NuevoContexto();
            var producto = lectura.Productos.Single();

            var resultado = await CrearController(db).Details(producto.Id);

            var view = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<Producto>(view.Model);
            Assert.Equal(producto.Id, modelo.Id);
            Assert.NotNull(modelo.Proveedor);
        }

        // ---------- Create (GET): desplegable de proveedores ----------

        // CargarProveedoresAsync filtra por Activo == true. Si un proveedor dado de baja apareciera
        // en el desplegable, se podrian crear productos nuevos ligados a un proveedor inactivo.
        [Fact]
        public async Task Create_Get_CargaSoloProveedoresActivosEnElDesplegable()
        {
            using var db = SqliteContextoDePrueba.Create();
            await CrearProveedorAsync(db.Context, activo: true, cif: "11111111A");
            await CrearProveedorAsync(db.Context, activo: false, cif: "22222222B");
            var controller = CrearController(db);

            await controller.Create();

            var lista = Assert.IsType<SelectList>(controller.ViewBag.Proveedores);
            Assert.Single(lista);
        }

        // ---------- Create (POST) ----------

        [Fact]
        public async Task Create_Post_ConModelStateInvalido_DevuelveViewYRecargaProveedores()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var controller = CrearController(db);
            controller.ModelState.AddModelError("Nombre", "El nombre es obligatorio");

            var resultado = await controller.Create(VmValido(proveedor.Id));

            Assert.IsType<ViewResult>(resultado);
            Assert.NotNull(controller.ViewBag.Proveedores);
            using var lectura = db.NuevoContexto();
            Assert.Empty(lectura.Productos); // no se ha creado nada
        }

        [Fact]
        public async Task Create_Post_ConDatosValidos_GeneraSkuAutomaticoIgnorandoElEnviadoYRedirigeAIndex()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var controller = CrearController(db);

            var resultado = await controller.Create(VmValido(proveedor.Id));

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Index", redirect.ActionName);
            using var lectura = db.NuevoContexto();
            var guardado = Assert.Single(lectura.Productos);
            Assert.NotEqual("IGNORADO-9999", guardado.Sku);
            Assert.StartsWith("PRO-", guardado.Sku); // "Producto de Prueba" -> prefijo "PRO"
        }

        [Fact]
        public async Task Create_Post_ConStockActualPositivo_CreaProductoConEseStockYUnMovimientoDeEntrada()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var controller = CrearController(db);
            var vm = VmValido(proveedor.Id);
            vm.StockActual = 15;

            await controller.Create(vm);

            // Lectura con un contexto nuevo: RegistrarMovimientoAsync escribe el stock con
            // ExecuteUpdateAsync directamente en la BD, sin pasar por el contexto del controlador.
            using var lectura = db.NuevoContexto();
            var guardado = Assert.Single(lectura.Productos);
            Assert.Equal(15, guardado.StockActual);
            var movimiento = Assert.Single(lectura.MovimientosStock);
            Assert.Equal(TipoMovimiento.Entrada, movimiento.Tipo);
            Assert.Equal(15, movimiento.Cantidad);
        }

        // ---------- Edit ----------

        [Fact]
        public async Task Edit_Get_ConIdInexistente_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);

            var resultado = await controller.Edit(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        // El id de la URL coincide con el del ViewModel (pasa el primer chequeo), pero no existe
        // ningun producto con ese id en la base de datos (por ejemplo, borrado entre el GET y el POST).
        [Fact]
        public async Task Edit_Post_ConIdQueNoExisteEnBaseDeDatos_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var controller = CrearController(db);
            var vm = VmValido(proveedor.Id);
            vm.Id = 999;

            var resultado = await controller.Edit(999, vm);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task Edit_Post_ConIdDistintoAlDelViewModel_DevuelveBadRequest()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);
            var vm = VmValido(1);
            vm.Id = 5;

            var resultado = await controller.Edit(id: 999, vm: vm);

            Assert.IsType<BadRequestResult>(resultado);
        }

        [Fact]
        public async Task Edit_Post_ConDatosValidos_ActualizaLosCamposPermitidos()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            await CrearController(db).Create(VmValido(proveedor.Id));
            using var lecturaInicial = db.NuevoContexto();
            var productoId = lecturaInicial.Productos.Select(p => p.Id).Single();

            var vm = VmValido(proveedor.Id);
            vm.Id = productoId;
            vm.Nombre = "Nombre Editado";
            vm.PrecioVenta = 99m;

            var resultado = await CrearController(db).Edit(productoId, vm);

            Assert.IsType<RedirectToActionResult>(resultado);
            using var lecturaFinal = db.NuevoContexto();
            var actualizado = await lecturaFinal.Productos.FindAsync(productoId);
            Assert.Equal("Nombre Editado", actualizado!.Nombre);
            Assert.Equal(99m, actualizado.PrecioVenta);
        }

        // Detalle no obvio: el formulario de edicion pide y valida StockActual, pero el controlador
        // nunca lo reasigna al producto. El stock solo cambia via movimientos, no editando el producto.
        [Fact]
        public async Task Edit_Post_NoModificaElStockActualAunqueElFormularioEnvieOtroValor()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            var vmCreacion = VmValido(proveedor.Id);
            vmCreacion.StockActual = 10;
            await CrearController(db).Create(vmCreacion);
            using var lecturaInicial = db.NuevoContexto();
            var productoId = lecturaInicial.Productos.Select(p => p.Id).Single();

            var vmEdicion = VmValido(proveedor.Id);
            vmEdicion.Id = productoId;
            vmEdicion.StockActual = 999; // el formulario "pide" cambiarlo a 999

            await CrearController(db).Edit(productoId, vmEdicion);

            using var lecturaFinal = db.NuevoContexto();
            var actualizado = await lecturaFinal.Productos.FindAsync(productoId);
            Assert.Equal(10, actualizado!.StockActual); // se mantiene el valor de la creacion, no 999
        }

        // Igual que con StockActual: el Sku no se puede editar, aunque el ViewModel lo incluya.
        [Fact]
        public async Task Edit_Post_NoModificaElSkuAunqueElFormularioEnvieOtroValor()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            await CrearController(db).Create(VmValido(proveedor.Id));
            using var lecturaInicial = db.NuevoContexto();
            var original = lecturaInicial.Productos.Single();
            var productoId = original.Id;
            var skuOriginal = original.Sku;

            var vmEdicion = VmValido(proveedor.Id);
            vmEdicion.Id = productoId;
            vmEdicion.Sku = "OTRO-SKU";

            await CrearController(db).Edit(productoId, vmEdicion);

            using var lecturaFinal = db.NuevoContexto();
            var actualizado = await lecturaFinal.Productos.FindAsync(productoId);
            Assert.Equal(skuOriginal, actualizado!.Sku);
        }

        [Fact]
        public async Task Edit_Post_ConModelStateInvalido_DevuelveViewYRecargaProveedores()
        {
            using var db = SqliteContextoDePrueba.Create();
            var proveedor = await CrearProveedorAsync(db.Context);
            await CrearController(db).Create(VmValido(proveedor.Id));
            using var lecturaInicial = db.NuevoContexto();
            var productoId = lecturaInicial.Productos.Select(p => p.Id).Single();

            var controllerEdicion = CrearController(db);
            controllerEdicion.ModelState.AddModelError("Nombre", "El nombre es obligatorio");
            var vm = VmValido(proveedor.Id);
            vm.Id = productoId;

            var resultado = await controllerEdicion.Edit(productoId, vm);

            Assert.IsType<ViewResult>(resultado);
            Assert.NotNull(controllerEdicion.ViewBag.Proveedores);
        }
    }
}
