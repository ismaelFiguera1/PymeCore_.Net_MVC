using Microsoft.EntityFrameworkCore;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using Xunit;

namespace PymeCore.Tests
{
    public class PresupuestoServiceTests
    {
        private static async Task<Cliente> CrearClienteAsync(ApplicationDbContext context, string nif = "12345678A")
        {
            var cliente = new Cliente { Nombre = "Cliente de Prueba", Nif = nif, Email = "cliente@test.com", Telefono = "600123456" };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            return cliente;
        }

        private static async Task<Producto> CrearProductoAsync(ApplicationDbContext context, string sku = "TES-0001", decimal precioVenta = 20m)
        {
            var proveedor = new Proveedor { Nombre = "Proveedor de Prueba", Cif = Guid.NewGuid().ToString("N")[..9], Email = "proveedor@test.com" };
            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();
            var producto = new Producto { Nombre = "Producto de Prueba", Sku = sku, PrecioCoste = 10m, PrecioVenta = precioVenta, ProveedorId = proveedor.Id };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();
            return producto;
        }

        private static Presupuesto PresupuestoValido(int clienteId, EstadoPresupuesto estado = EstadoPresupuesto.Borrador, string? numero = null) => new()
        {
            Numero = numero ?? $"MANUAL-{Guid.NewGuid():N}"[..15],
            ClienteId = clienteId,
            Estado = estado,
            Observaciones = "Observaciones de prueba"
        };

        // ---------- Generacion del numero correlativo ----------

        [Fact]
        public async Task GenerarNumeroAsync_SinPresupuestosPrevios_EmpiezaEn001()
        {
            using var context = DbContextFactory.Create();
            var service = new PresupuestoService(context);
            var año = DateTime.UtcNow.Year;

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"PRES-{año}-001", numero);
        }

        [Fact]
        public async Task GenerarNumeroAsync_ConPresupuestosDelMismoAño_ContinuaElCorrelativo()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var año = DateTime.UtcNow.Year;
            context.Presupuestos.Add(PresupuestoValido(cliente.Id, numero: $"PRES-{año}-004"));
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"PRES-{año}-005", numero);
        }

        // El prefijo incluye el año, asi que la busqueda de "el numero mas alto" (StartsWith(prefix))
        // ignora automaticamente cualquier presupuesto de años anteriores: el correlativo se reinicia
        // cada año sin necesidad de logica extra.
        [Fact]
        public async Task GenerarNumeroAsync_IgnoraPresupuestosDeAñosAnteriores()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            context.Presupuestos.Add(PresupuestoValido(cliente.Id, numero: "PRES-2020-099"));
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);
            var año = DateTime.UtcNow.Year;

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"PRES-{año}-001", numero);
        }

        // Si algun Numero no tiene el formato esperado tras el prefijo (parte no numerica), se
        // ignora (cuenta como 0) en vez de reventar con FormatException.
        [Fact]
        public async Task GenerarNumeroAsync_IgnoraNumerosConFormatoInesperadoTrasElPrefijo()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var año = DateTime.UtcNow.Year;
            context.Presupuestos.Add(PresupuestoValido(cliente.Id, numero: $"PRES-{año}-ABC"));
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var numero = await service.GenerarNumeroAsync();

            Assert.Equal($"PRES-{año}-001", numero);
        }

        // ---------- Creacion ----------

        [Fact]
        public async Task CreateAsync_AsignaUnNumeroGeneradoIgnorandoElQueTraigaLaEntidad()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var service = new PresupuestoService(context);
            var presupuesto = PresupuestoValido(cliente.Id, numero: "IGNORADO");

            await service.CreateAsync(presupuesto);

            var año = DateTime.UtcNow.Year;
            Assert.Equal($"PRES-{año}-001", presupuesto.Numero);
            Assert.NotEqual(0, presupuesto.Id);
        }

        // Restriccion real de EF/BD: builder.Entity<Presupuesto>().HasIndex(p => p.Numero).IsUnique()
        // en ApplicationDbContext. GuardarConNumeroUnicoAsync existe precisamente para reintentar
        // cuando esta restriccion salta por una colision entre peticiones concurrentes, pero el
        // reintento solo se activa si la excepcion es una PostgresException con SqlState 23505
        // (EsNumeroDuplicado). Con SQLite, el motor que aplica el mismo indice unico devuelve una
        // SqliteException, no una PostgresException, asi que el filtro nunca coincide bajo SQLite y
        // esta prueba no puede reproducir el reintento en si (solo puede confirmar que la
        // restriccion de la BD existe y se aplica de verdad, insertando directamente contra el
        // contexto en vez de pasar por CreateAsync).
        [Fact]
        public async Task IndiceUnicoDeNumero_RechazaDosPresupuestosConElMismoNumeroAlGuardarEnElContexto()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            db.Context.Presupuestos.Add(PresupuestoValido(cliente.Id, numero: "PRES-2026-001"));
            await db.Context.SaveChangesAsync();

            db.Context.Presupuestos.Add(PresupuestoValido(cliente.Id, numero: "PRES-2026-001"));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        }

        // PresupuestoService no comprueba que el ClienteId exista antes de guardar: confia por
        // completo en la restriccion de clave foranea de la BD para rechazarlo (igual que
        // ProductoService.CreateAsync con ProveedorId).
        [Fact]
        public async Task CreateAsync_ConClienteIdInexistente_LanzaExcepcionPorRestriccionDeClaveForanea()
        {
            using var db = SqliteContextoDePrueba.Create();
            var service = new PresupuestoService(db.Context);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.CreateAsync(PresupuestoValido(clienteId: 999)));
        }

        // ---------- Duplicar ----------

        [Fact]
        public async Task DuplicarAsync_ConIdInexistente_LanzaExcepcion()
        {
            using var context = DbContextFactory.Create();
            var service = new PresupuestoService(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DuplicarAsync(999));
        }

        [Fact]
        public async Task DuplicarAsync_CopiaLasLineasYRecalculaElTotalEnEstadoBorrador()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var original = PresupuestoValido(cliente.Id, estado: EstadoPresupuesto.Aceptado);
            db.Context.Presupuestos.Add(original);
            await db.Context.SaveChangesAsync();
            db.Context.LineasPresupuesto.Add(new LineaPresupuesto { PresupuestoId = original.Id, ProductoId = producto.Id, Cantidad = 3, PrecioUnitario = 20m, Subtotal = 60m });
            await db.Context.SaveChangesAsync();
            var service = new PresupuestoService(db.Context);

            var copia = await service.DuplicarAsync(original.Id);

            Assert.NotEqual(original.Id, copia.Id);
            Assert.Equal(EstadoPresupuesto.Borrador, copia.Estado);
            Assert.Equal(60m, copia.Total);
            using var lectura = db.NuevoContexto();
            var lineaCopiada = Assert.Single(lectura.LineasPresupuesto.Where(l => l.PresupuestoId == copia.Id));
            Assert.Equal(producto.Id, lineaCopiada.ProductoId);
            Assert.Equal(3, lineaCopiada.Cantidad);
        }

        // ---------- Enviar ----------

        [Fact]
        public async Task EnviarAsync_ConIdInexistente_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var service = new PresupuestoService(context);

            var (ok, error) = await service.EnviarAsync(999);

            Assert.False(ok);
            Assert.Equal("Presupuesto no encontrado.", error);
        }

        [Fact]
        public async Task EnviarAsync_ConEstadoDistintoDeBorrador_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var presupuesto = PresupuestoValido(cliente.Id, estado: EstadoPresupuesto.Enviado);
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var (ok, error) = await service.EnviarAsync(presupuesto.Id);

            Assert.False(ok);
            Assert.Equal("Solo se puede enviar un presupuesto en estado Borrador (estado actual: Enviado).", error);
        }

        [Fact]
        public async Task EnviarAsync_SinLineas_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var presupuesto = PresupuestoValido(cliente.Id);
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var (ok, error) = await service.EnviarAsync(presupuesto.Id);

            Assert.False(ok);
            Assert.Equal("No se puede enviar un presupuesto sin líneas. Añade al menos una.", error);
        }

        [Fact]
        public async Task EnviarAsync_ConLineasYEnBorrador_CambiaAEnviado()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var producto = await CrearProductoAsync(context);
            var presupuesto = PresupuestoValido(cliente.Id);
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            context.LineasPresupuesto.Add(new LineaPresupuesto { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 20m, Subtotal = 20m });
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var (ok, error) = await service.EnviarAsync(presupuesto.Id);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal(EstadoPresupuesto.Enviado, (await service.GetByIdAsync(presupuesto.Id))!.Estado);
        }

        // ---------- Aceptar ----------

        [Fact]
        public async Task AceptarAsync_ConEstadoDistintoDeEnviado_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var presupuesto = PresupuestoValido(cliente.Id, estado: EstadoPresupuesto.Borrador);
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var (ok, error) = await service.AceptarAsync(presupuesto.Id);

            Assert.False(ok);
            Assert.Equal("Solo se puede aceptar un presupuesto en estado Enviado (estado actual: Borrador).", error);
        }

        [Fact]
        public async Task AceptarAsync_ConEstadoEnviado_CambiaAAceptado()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var presupuesto = PresupuestoValido(cliente.Id, estado: EstadoPresupuesto.Enviado);
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var (ok, error) = await service.AceptarAsync(presupuesto.Id);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal(EstadoPresupuesto.Aceptado, (await service.GetByIdAsync(presupuesto.Id))!.Estado);
        }

        // ---------- Rechazar ----------

        [Fact]
        public async Task RechazarAsync_ConEstadoDistintoDeEnviado_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var presupuesto = PresupuestoValido(cliente.Id, estado: EstadoPresupuesto.Borrador);
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var (ok, error) = await service.RechazarAsync(presupuesto.Id);

            Assert.False(ok);
            Assert.Equal("Solo se puede rechazar un presupuesto en estado Enviado (estado actual: Borrador).", error);
        }

        [Fact]
        public async Task RechazarAsync_ConEstadoEnviado_CambiaARechazado()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var presupuesto = PresupuestoValido(cliente.Id, estado: EstadoPresupuesto.Enviado);
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var (ok, error) = await service.RechazarAsync(presupuesto.Id);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal(EstadoPresupuesto.Rechazado, (await service.GetByIdAsync(presupuesto.Id))!.Estado);
        }

        // ---------- Lineas: agregar / eliminar / recalculo del total ----------

        [Fact]
        public async Task AgregarLineaAsync_CalculaElSubtotalYRecalculaElTotalDelPresupuesto()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = PresupuestoValido(cliente.Id);
            db.Context.Presupuestos.Add(presupuesto);
            await db.Context.SaveChangesAsync();
            var service = new PresupuestoService(db.Context);

            await service.AgregarLineaAsync(new LineaPresupuesto { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = 3, PrecioUnitario = 15m });

            using var lectura = db.NuevoContexto();
            var linea = Assert.Single(lectura.LineasPresupuesto);
            Assert.Equal(45m, linea.Subtotal); // 3 * 15
            Assert.Equal(45m, (await lectura.Presupuestos.FindAsync(presupuesto.Id))!.Total);
        }

        [Fact]
        public async Task AgregarLineaAsync_ConVariasLineas_SumaTodosLosSubtotalesEnElTotal()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var productoA = await CrearProductoAsync(db.Context, sku: "AAA-0001");
            var productoB = await CrearProductoAsync(db.Context, sku: "BBB-0001");
            var presupuesto = PresupuestoValido(cliente.Id);
            db.Context.Presupuestos.Add(presupuesto);
            await db.Context.SaveChangesAsync();
            var service = new PresupuestoService(db.Context);

            await service.AgregarLineaAsync(new LineaPresupuesto { PresupuestoId = presupuesto.Id, ProductoId = productoA.Id, Cantidad = 2, PrecioUnitario = 10m });
            await service.AgregarLineaAsync(new LineaPresupuesto { PresupuestoId = presupuesto.Id, ProductoId = productoB.Id, Cantidad = 1, PrecioUnitario = 5m });

            using var lectura = db.NuevoContexto();
            Assert.Equal(25m, (await lectura.Presupuestos.FindAsync(presupuesto.Id))!.Total); // 20 + 5
        }

        [Fact]
        public async Task EliminarLineaAsync_EliminaLaLineaYRecalculaElTotal()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = PresupuestoValido(cliente.Id);
            db.Context.Presupuestos.Add(presupuesto);
            await db.Context.SaveChangesAsync();
            var service = new PresupuestoService(db.Context);
            await service.AgregarLineaAsync(new LineaPresupuesto { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = 2, PrecioUnitario = 10m });
            using var lecturaLinea = db.NuevoContexto();
            var lineaId = lecturaLinea.LineasPresupuesto.Single().Id;

            await service.EliminarLineaAsync(lineaId, presupuesto.Id);

            using var lectura = db.NuevoContexto();
            Assert.Empty(lectura.LineasPresupuesto);
            Assert.Equal(0m, (await lectura.Presupuestos.FindAsync(presupuesto.Id))!.Total);
        }

        // EliminarLineaAsync filtra por (lineaId Y presupuestoId) a la vez: si el presupuestoId
        // indicado no es el dueño real de la linea, la busqueda no encuentra nada y el metodo
        // termina sin eliminar nada ni lanzar ningun error, en vez de avisar de que el id no
        // corresponde. El controlador tampoco distingue este caso: siempre redirige como si hubiera
        // ido bien.
        [Fact]
        public async Task EliminarLineaAsync_ConPresupuestoIdQueNoEsElDueñoDeLaLinea_NoEliminaNadaYNoLanzaError()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuestoA = PresupuestoValido(cliente.Id, numero: "PRES-A");
            var presupuestoB = PresupuestoValido(cliente.Id, numero: "PRES-B");
            db.Context.Presupuestos.AddRange(presupuestoA, presupuestoB);
            await db.Context.SaveChangesAsync();
            var service = new PresupuestoService(db.Context);
            await service.AgregarLineaAsync(new LineaPresupuesto { PresupuestoId = presupuestoA.Id, ProductoId = producto.Id, Cantidad = 2, PrecioUnitario = 10m });
            using var lecturaLinea = db.NuevoContexto();
            var lineaId = lecturaLinea.LineasPresupuesto.Single().Id;

            await service.EliminarLineaAsync(lineaId, presupuestoB.Id); // presupuestoId equivocado

            using var lectura = db.NuevoContexto();
            Assert.Single(lectura.LineasPresupuesto); // sigue existiendo
            Assert.Equal(20m, (await lectura.Presupuestos.FindAsync(presupuestoA.Id))!.Total); // intacto
        }

        // A diferencia de Producto (que tiene 4 CHECK constraints de "no negativo" en la BD),
        // LineaPresupuesto no tiene ningun CHECK constraint ni comprobacion en el servicio para
        // Cantidad o PrecioUnitario. El [Range] del ViewModel es la UNICA barrera; quien construya
        // una LineaPresupuesto directamente (otro servicio, una futura API) puede guardar cantidades
        // o precios negativos sin que nada lo impida, y RecalcularTotalAsync los suma tal cual al
        // Total del presupuesto.
        [Fact]
        public async Task AgregarLineaAsync_ConCantidadNegativa_NoLaRechazaYElTotalQuedaNegativo()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = PresupuestoValido(cliente.Id);
            db.Context.Presupuestos.Add(presupuesto);
            await db.Context.SaveChangesAsync();
            var service = new PresupuestoService(db.Context);

            await service.AgregarLineaAsync(new LineaPresupuesto { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = -3, PrecioUnitario = 10m });

            using var lectura = db.NuevoContexto();
            var linea = Assert.Single(lectura.LineasPresupuesto);
            Assert.Equal(-30m, linea.Subtotal);
            Assert.Equal(-30m, (await lectura.Presupuestos.FindAsync(presupuesto.Id))!.Total);
        }

        // Igual que con ClienteId en CreateAsync: AgregarLineaAsync no comprueba que el ProductoId
        // exista antes de guardar, se apoya por completo en la restriccion de clave foranea.
        [Fact]
        public async Task AgregarLineaAsync_ConProductoIdInexistente_LanzaExcepcionPorRestriccionDeClaveForanea()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = PresupuestoValido(cliente.Id);
            db.Context.Presupuestos.Add(presupuesto);
            await db.Context.SaveChangesAsync();
            var service = new PresupuestoService(db.Context);

            await Assert.ThrowsAsync<DbUpdateException>(() => service.AgregarLineaAsync(
                new LineaPresupuesto { PresupuestoId = presupuesto.Id, ProductoId = 999, Cantidad = 1, PrecioUnitario = 10m }));
        }

        [Fact]
        public async Task EliminarLineaAsync_ConLineaIdInexistente_NoLanzaError()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var presupuesto = PresupuestoValido(cliente.Id);
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            await service.EliminarLineaAsync(999, presupuesto.Id); // no debe lanzar excepcion

            Assert.Empty(context.LineasPresupuesto);
        }

        // ---------- Busqueda / listado ----------

        [Fact]
        public async Task GetAllAsync_SinFiltro_DevuelveTodosOrdenadosPorFechaDescendente()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var antiguo = PresupuestoValido(cliente.Id, numero: "PRES-2026-001");
            antiguo.Fecha = new DateTime(2026, 1, 1);
            var reciente = PresupuestoValido(cliente.Id, numero: "PRES-2026-002");
            reciente.Fecha = new DateTime(2026, 6, 1);
            context.Presupuestos.AddRange(antiguo, reciente);
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var resultado = await service.GetAllAsync();

            Assert.Equal(2, resultado.Count);
            Assert.Equal("PRES-2026-002", resultado[0].Numero); // el mas reciente primero
            Assert.Equal("PRES-2026-001", resultado[1].Numero);
        }

        [Fact]
        public async Task GetAllAsync_ConBuscarPorNumero_FiltraCoincidencias()
        {
            using var context = DbContextFactory.Create();
            var cliente = await CrearClienteAsync(context);
            var presupuestoUno = PresupuestoValido(cliente.Id, numero: "PRES-2026-001");
            var presupuestoDos = PresupuestoValido(cliente.Id, numero: "PRES-2026-002");
            context.Presupuestos.AddRange(presupuestoUno, presupuestoDos);
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var resultado = await service.GetAllAsync("2026-001");

            Assert.Single(resultado);
            Assert.Equal("PRES-2026-001", resultado[0].Numero);
        }

        [Fact]
        public async Task GetAllAsync_ConBuscarPorNombreDeCliente_FiltraCoincidencias()
        {
            using var context = DbContextFactory.Create();
            var clienteBuscado = await CrearClienteAsync(context, nif: "11111111A");
            clienteBuscado.Nombre = "Construcciones Perez";
            var otroCliente = await CrearClienteAsync(context, nif: "22222222B");
            otroCliente.Nombre = "Otra Empresa";
            await context.SaveChangesAsync();
            context.Presupuestos.AddRange(
                PresupuestoValido(clienteBuscado.Id, numero: "PRES-2026-001"),
                PresupuestoValido(otroCliente.Id, numero: "PRES-2026-002"));
            await context.SaveChangesAsync();
            var service = new PresupuestoService(context);

            var resultado = await service.GetAllAsync("construc");

            Assert.Single(resultado);
            Assert.Equal("PRES-2026-001", resultado[0].Numero);
        }

        [Fact]
        public async Task GetByIdAsync_ConIdInexistente_DevuelveNull()
        {
            using var context = DbContextFactory.Create();
            var service = new PresupuestoService(context);

            var resultado = await service.GetByIdAsync(999);

            Assert.Null(resultado);
        }
    }
}
