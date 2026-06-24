using Microsoft.EntityFrameworkCore;
using PymeCore.Data;
using PymeCore.Models;

namespace PymeCore.Services
{
    public class ProductoService
    {
        private readonly ApplicationDbContext _context;

        public ProductoService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Producto>> GetAllAsync(string? buscar = null)
        {
            var query = _context.Productos.Include(p => p.Proveedor).AsQueryable();

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                var termino = buscar.ToLower();
                query = query.Where(p =>
                    p.Nombre.ToLower().Contains(termino) ||
                    p.Sku.ToLower().Contains(termino));
            }

            return await query.OrderBy(p => p.Nombre).ToListAsync();
        }

        public async Task<Producto?> GetByIdAsync(int id)
        {
            return await _context.Productos
                .Include(p => p.Proveedor)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<bool> ExisteSkuAsync(string sku, int excludeId = 0)
        {
            return await _context.Productos
                .AnyAsync(p => p.Sku == sku && p.Id != excludeId);
        }

        public async Task CreateAsync(Producto producto)
        {
            _context.Productos.Add(producto);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Producto producto)
        {
            _context.Productos.Update(producto);
            await _context.SaveChangesAsync();
        }

        public async Task<List<Producto>> GetBajoStockAsync()
        {
            return await _context.Productos
                .Include(p => p.Proveedor)
                .Where(p => p.StockActual < p.StockMinimo)
                .OrderBy(p => p.Nombre)
                .ToListAsync();
        }
    }
}
