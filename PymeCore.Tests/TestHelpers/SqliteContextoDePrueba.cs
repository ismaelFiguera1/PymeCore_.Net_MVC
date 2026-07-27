using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PymeCore.Data;

namespace PymeCore.Tests.TestHelpers
{
    // El proveedor InMemory de EF Core NO hace cumplir indices unicos secundarios (solo garantiza
    // la clave primaria), asi que no sirve para probar builder.Entity<Cliente>().HasIndex(c => c.Nif).IsUnique().
    // SQLite en memoria si crea un esquema relacional real y aplica esa restriccion de verdad.
    // Se usa solo en los tests que necesitan comprobar esa restriccion concreta; el resto de tests
    // siguen usando DbContextFactory (InMemory), que es mas simple y mas rapido.
    //
    // La conexion debe permanecer abierta mientras se use el contexto: con "Filename=:memory:",
    // la base de datos SQLite desaparece en cuanto se cierra la conexion. Por eso esta clase
    // implementa IDisposable y libera conexion + contexto juntos.
    public sealed class SqliteContextoDePrueba : IDisposable
    {
        private readonly SqliteConnection _connection;
        public ApplicationDbContext Context { get; }

        private SqliteContextoDePrueba(SqliteConnection connection, ApplicationDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public static SqliteContextoDePrueba Create()
        {
            var connection = new SqliteConnection("Filename=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new ApplicationDbContext(options);
            context.Database.EnsureCreated();

            return new SqliteContextoDePrueba(connection, context);
        }

        // Crea un ApplicationDbContext NUEVO sobre la MISMA conexion/base de datos en memoria.
        // Hace falta para operaciones como StockService.RegistrarMovimientoAsync, que usa
        // ExecuteUpdateAsync: eso escribe en la base de datos directamente, sin pasar por el
        // rastreador de cambios del contexto que lo llamo. Si despues se vuelve a consultar esa
        // misma entidad con el MISMO contexto, EF Core devuelve la instancia que ya tenia en
        // memoria (para no perder cambios pendientes sin guardar) en vez de releer la fila real de
        // la base de datos, y el valor sale desactualizado.
        // Ademas, es mas fiel a como funciona la app real: cada peticion HTTP usa su propio
        // DbContext (inyeccion de dependencias con ambito por peticion), asi que Create y Edit
        // nunca comparten el mismo rastreador de cambios como si se llamara dos veces al mismo
        // metodo dentro de un test.
        public ApplicationDbContext NuevoContexto()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options;

            return new ApplicationDbContext(options);
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }
}
