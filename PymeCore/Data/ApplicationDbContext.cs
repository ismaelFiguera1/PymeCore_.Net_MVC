using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PymeCore.Models;

namespace PymeCore.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<Proveedor> Proveedores { get; set; }
        public DbSet<Producto> Productos { get; set; }
        public DbSet<MovimientoStock> MovimientosStock { get; set; }
        public DbSet<Presupuesto> Presupuestos { get; set; }
        public DbSet<LineaPresupuesto> LineasPresupuesto { get; set; }
        public DbSet<Pedido> Pedidos { get; set; }
        public DbSet<LineaPedido> LineasPedido { get; set; }
        public DbSet<Factura> Facturas { get; set; }
        public DbSet<FacturaSnapshot> FacturaSnapshots { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Producto>()
                .HasIndex(p => p.Sku)
                .IsUnique();

            // Garantía a nivel de base de datos: aunque algún código (o alguien con acceso
            // directo a la BD) se salte la validación de ProductoService, PostgreSQL rechaza
            // igualmente precios o stock negativos.
            builder.Entity<Producto>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_Productos_PrecioCoste_NoNegativo", "\"PrecioCoste\" >= 0");
                t.HasCheckConstraint("CK_Productos_PrecioVenta_NoNegativo", "\"PrecioVenta\" >= 0");
                t.HasCheckConstraint("CK_Productos_StockActual_NoNegativo", "\"StockActual\" >= 0");
                t.HasCheckConstraint("CK_Productos_StockMinimo_NoNegativo", "\"StockMinimo\" >= 0");
            });

            builder.Entity<Producto>()
                .HasOne(p => p.Proveedor)
                .WithMany()
                .HasForeignKey(p => p.ProveedorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Proveedor>()
                .HasIndex(p => p.Cif)
                .IsUnique();

            builder.Entity<Cliente>()
                .HasIndex(c => c.Nif)
                .IsUnique();

            // El correlativo (PRES-2026-001, etc.) se calcula en el Service leyendo el máximo actual
            // y sumando 1; sin este índice, dos peticiones simultáneas podrían calcular el mismo
            // número y guardarlo duplicado. El índice único obliga a que la BD rechace el segundo
            // intento, y el Service reacciona regenerando el número y reintentando.
            builder.Entity<Presupuesto>()
                .HasIndex(p => p.Numero)
                .IsUnique();

            builder.Entity<Pedido>()
                .HasIndex(p => p.Numero)
                .IsUnique();

            builder.Entity<Factura>()
                .HasIndex(f => f.Numero)
                .IsUnique();

            // Sin esto, EF Core aplica Cascade por defecto (ClienteId es un int no-nulo, así que
            // la relación cuenta como "obligatoria"): borrar un Cliente borraría en cascada sus
            // facturas, pedidos y presupuestos. Con Restrict, la BD rechaza el borrado del Cliente
            // mientras tenga registros asociados.
            builder.Entity<Factura>()
                .HasOne(f => f.Cliente)
                .WithMany()
                .HasForeignKey(f => f.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Pedido>()
                .HasOne(p => p.Cliente)
                .WithMany()
                .HasForeignKey(p => p.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Presupuesto>()
                .HasOne(p => p.Cliente)
                .WithMany()
                .HasForeignKey(p => p.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Pedido>()
                .HasOne(p => p.PresupuestoOrigen)
                .WithMany()
                .HasForeignKey(p => p.PresupuestoOrigenId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Factura>()
                .HasOne(f => f.Pedido)
                .WithOne()
                .HasForeignKey<Factura>(f => f.PedidoId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Factura>()
                .HasIndex(f => f.PedidoId)
                .IsUnique();

            builder.Entity<FacturaSnapshot>()
                .HasOne(s => s.Factura)
                .WithOne()
                .HasForeignKey<FacturaSnapshot>(s => s.FacturaId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<FacturaSnapshot>()
                .HasIndex(s => s.FacturaId)
                .IsUnique();

            builder.Entity<FacturaSnapshot>()
                .Property(s => s.DatosJson)
                .HasColumnType("jsonb");
        }
    }
}
