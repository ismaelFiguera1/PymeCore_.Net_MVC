using Microsoft.EntityFrameworkCore;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using Xunit;

namespace PymeCore.Tests
{
    // RegistrarMovimientoAsync usa ExecuteUpdateAsync para restar/sumar stock de forma atomica
    // (la comprobacion de "no negativo" y la resta ocurren en la MISMA sentencia UPDATE), algo
    // que el proveedor InMemory de EF Core no soporta (es exclusivo de proveedores relacionales).
    // Por eso todos estos tests usan SqliteContextoDePrueba en vez de DbContextFactory.
    public class StockServiceTests
    {
        private static async Task<Producto> CrearProductoAsync(ApplicationDbContext context, int stockActual = 0, string sku = "TES-0001")
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

        // ---------- Calculo del delta segun el tipo de movimiento ----------

        [Theory]
        [InlineData(TipoMovimiento.Entrada, 5, 15)]     // Entrada: suma
        [InlineData(TipoMovimiento.Devolucion, 5, 15)]  // Devolucion: suma, igual que Entrada
        [InlineData(TipoMovimiento.Salida, 5, 5)]       // Salida: resta (invierte el signo de Cantidad)
        public async Task RegistrarMovimientoAsync_AplicaElDeltaSegunElTipo(TipoMovimiento tipo, int cantidad, int stockEsperado)
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 10);
            var service = new StockService(db.Context);

            var error = await service.RegistrarMovimientoAsync(new MovimientoStock
            {
                ProductoId = producto.Id,
                Tipo = tipo,
                Cantidad = cantidad
            });

            Assert.Null(error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(stockEsperado, (await lectura.Productos.FindAsync(producto.Id))!.StockActual);
        }

        // A diferencia de los demas tipos, en Ajuste la Cantidad ya viene con el signo puesto por
        // quien llama: positiva suma, negativa resta. Aqui se prueban las dos direcciones.
        [Theory]
        [InlineData(5, 15)]
        [InlineData(-5, 5)]
        public async Task RegistrarMovimientoAsync_ConTipoAjuste_UsaLaCantidadTalCualConSuSigno(int cantidad, int stockEsperado)
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 10);
            var service = new StockService(db.Context);

            var error = await service.RegistrarMovimientoAsync(new MovimientoStock
            {
                ProductoId = producto.Id,
                Tipo = TipoMovimiento.Ajuste,
                Cantidad = cantidad
            });

            Assert.Null(error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(stockEsperado, (await lectura.Productos.FindAsync(producto.Id))!.StockActual);
        }

        // ---------- Registro del historico ----------

        [Fact]
        public async Task RegistrarMovimientoAsync_ConMovimientoValido_GuardaElHistoricoConFechaAsignada()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 10);
            var service = new StockService(db.Context);
            var antes = DateTime.UtcNow;

            await service.RegistrarMovimientoAsync(new MovimientoStock
            {
                ProductoId = producto.Id,
                Tipo = TipoMovimiento.Entrada,
                Cantidad = 5,
                Motivo = "Reposicion"
            });

            using var lectura = db.NuevoContexto();
            var movimiento = Assert.Single(lectura.MovimientosStock);
            Assert.Equal("Reposicion", movimiento.Motivo);
            Assert.True(movimiento.Fecha >= antes && movimiento.Fecha <= DateTime.UtcNow);
        }

        // ---------- Rechazo de stock negativo ----------

        [Fact]
        public async Task RegistrarMovimientoAsync_SiElResultadoQuedaNegativo_DevuelveErrorYNoModificaNada()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 3);
            var service = new StockService(db.Context);

            var error = await service.RegistrarMovimientoAsync(new MovimientoStock
            {
                ProductoId = producto.Id,
                Tipo = TipoMovimiento.Salida,
                Cantidad = 5
            });

            Assert.Equal("El stock no puede quedar en negativo.", error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(3, (await lectura.Productos.FindAsync(producto.Id))!.StockActual);
            Assert.Empty(lectura.MovimientosStock); // el movimiento no se registra si la resta falla
        }

        // El limite exacto (llegar a 0) SI debe permitirse: la condicion es ">= 0", no "> 0".
        [Fact]
        public async Task RegistrarMovimientoAsync_SiElResultadoQuedaExactamenteEnCero_SePermite()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 5);
            var service = new StockService(db.Context);

            var error = await service.RegistrarMovimientoAsync(new MovimientoStock
            {
                ProductoId = producto.Id,
                Tipo = TipoMovimiento.Salida,
                Cantidad = 5
            });

            Assert.Null(error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(0, (await lectura.Productos.FindAsync(producto.Id))!.StockActual);
        }

        // permitirNegativo=true se salta la comprobacion de "no negativo" en la propia consulta SQL
        // (ExecuteUpdateAsync), pero NO se salta el CHECK constraint de la tabla Productos
        // (CK_Productos_StockActual_NoNegativo, definido en ApplicationDbContext). El resultado es
        // que, hoy, pasar permitirNegativo=true y terminar en negativo no deja el stock en negativo:
        // lanza una excepcion. Ademas, ningun sitio del codigo llama actualmente a
        // RegistrarMovimientoAsync con permitirNegativo=true, asi que este parametro esta sin usar
        // y, tal cual esta la BD, no serviria para lo que su nombre promete.
        [Fact]
        public async Task RegistrarMovimientoAsync_ConPermitirNegativoTrue_ElCheckConstraintDeLaBdLoImpideIgualmente()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 3);
            var service = new StockService(db.Context);

            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => service.RegistrarMovimientoAsync(new MovimientoStock
            {
                ProductoId = producto.Id,
                Tipo = TipoMovimiento.Salida,
                Cantidad = 5
            }, permitirNegativo: true));
        }

        // ---------- Producto inexistente ----------

        [Fact]
        public async Task RegistrarMovimientoAsync_ConProductoInexistente_DevuelveError()
        {
            using var db = SqliteContextoDePrueba.Create();
            var service = new StockService(db.Context);

            var error = await service.RegistrarMovimientoAsync(new MovimientoStock
            {
                ProductoId = 999,
                Tipo = TipoMovimiento.Entrada,
                Cantidad = 5
            });

            Assert.Equal("Producto no encontrado.", error);
            Assert.Empty(db.Context.MovimientosStock);
        }

        // ---------- Tipo no reconocido ----------

        // CalcularDelta devuelve 0 para cualquier valor de TipoMovimiento fuera del enum (por ejemplo,
        // si llega un entero invalido desde fuera del rango tipado). El movimiento igualmente se
        // registra en el historico, pero sin efecto real sobre el stock.
        [Fact]
        public async Task RegistrarMovimientoAsync_ConTipoNoReconocido_NoModificaElStockPeroRegistraElMovimiento()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 10);
            var service = new StockService(db.Context);

            var error = await service.RegistrarMovimientoAsync(new MovimientoStock
            {
                ProductoId = producto.Id,
                Tipo = (TipoMovimiento)999,
                Cantidad = 5
            });

            Assert.Null(error);
            using var lectura = db.NuevoContexto();
            Assert.Equal(10, (await lectura.Productos.FindAsync(producto.Id))!.StockActual);
            Assert.Single(lectura.MovimientosStock);
        }

        // ---------- Reutilizacion de una transaccion externa ----------

        // Si quien llama ya abrio una transaccion (como hace PedidoService al completar un pedido),
        // RegistrarMovimientoAsync no debe intentar abrir otra encima (SQLite no soporta transacciones
        // anidadas y lanzaria una excepcion). Este test simula esa llamada anidada llamando dos veces
        // seguidas dentro de una transaccion abierta manualmente.
        [Fact]
        public async Task RegistrarMovimientoAsync_DentroDeUnaTransaccionExterna_NoAbreOtraYFuncionaCorrectamente()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, stockActual: 10);
            var service = new StockService(db.Context);

            await using var tx = await db.Context.Database.BeginTransactionAsync();

            var error1 = await service.RegistrarMovimientoAsync(new MovimientoStock
            {
                ProductoId = producto.Id,
                Tipo = TipoMovimiento.Salida,
                Cantidad = 3
            });
            var error2 = await service.RegistrarMovimientoAsync(new MovimientoStock
            {
                ProductoId = producto.Id,
                Tipo = TipoMovimiento.Salida,
                Cantidad = 3
            });

            await tx.CommitAsync();

            Assert.Null(error1);
            Assert.Null(error2);
            using var lectura = db.NuevoContexto();
            Assert.Equal(4, (await lectura.Productos.FindAsync(producto.Id))!.StockActual);
            Assert.Equal(2, lectura.MovimientosStock.Count());
        }

        // ---------- Historico por producto ----------

        [Fact]
        public async Task GetHistorialPorProductoAsync_DevuelveSoloLosMovimientosDelProductoIndicado()
        {
            using var context = DbContextFactory.Create();
            var proveedor = new Proveedor { Nombre = "Proveedor", Cif = "12345678A", Email = "p@test.com" };
            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();
            var productoA = new Producto { Nombre = "A", Sku = "AAA-0001", ProveedorId = proveedor.Id };
            var productoB = new Producto { Nombre = "B", Sku = "BBB-0001", ProveedorId = proveedor.Id };
            context.Productos.AddRange(productoA, productoB);
            await context.SaveChangesAsync();
            context.MovimientosStock.AddRange(
                new MovimientoStock { ProductoId = productoA.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 1, Fecha = DateTime.UtcNow },
                new MovimientoStock { ProductoId = productoB.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 2, Fecha = DateTime.UtcNow });
            await context.SaveChangesAsync();
            var service = new StockService(context);

            var resultado = await service.GetHistorialPorProductoAsync(productoA.Id);

            Assert.Single(resultado);
            Assert.Equal(productoA.Id, resultado[0].ProductoId);
        }

        [Fact]
        public async Task GetHistorialPorProductoAsync_OrdenaDelMasRecienteAlMasAntiguo()
        {
            using var context = DbContextFactory.Create();
            var proveedor = new Proveedor { Nombre = "Proveedor", Cif = "12345678A", Email = "p@test.com" };
            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();
            var producto = new Producto { Nombre = "A", Sku = "AAA-0001", ProveedorId = proveedor.Id };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();
            var antiguo = new MovimientoStock { ProductoId = producto.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 1, Fecha = new DateTime(2026, 1, 1), Motivo = "antiguo" };
            var reciente = new MovimientoStock { ProductoId = producto.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 2, Fecha = new DateTime(2026, 6, 1), Motivo = "reciente" };
            context.MovimientosStock.AddRange(antiguo, reciente);
            await context.SaveChangesAsync();
            var service = new StockService(context);

            var resultado = await service.GetHistorialPorProductoAsync(producto.Id);

            Assert.Equal(2, resultado.Count);
            Assert.Equal("reciente", resultado[0].Motivo);
            Assert.Equal("antiguo", resultado[1].Motivo);
        }
    }
}
