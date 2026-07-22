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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Producto>()
                .HasIndex(p => p.Sku)
                .IsUnique();

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
        }
    }
}
