using Microsoft.EntityFrameworkCore;
using PymeCore.Data;

namespace PymeCore.Tests.TestHelpers
{
    // Cada llamada crea un ApplicationDbContext respaldado por una base de datos InMemory
    // con nombre distinto (Guid), para que los tests no compartan estado entre si ni con Postgres.
    public static class DbContextFactory
    {
        public static ApplicationDbContext Create()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
