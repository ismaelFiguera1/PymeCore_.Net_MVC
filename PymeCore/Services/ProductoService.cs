using System.Globalization;
using System.Text;
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

        // Combina un prefijo de 3 letras (según el nombre) con un número correlativo, ej. "SIL-0004"
        public async Task<string> GenerarSkuAsync(string nombre)
        {
            var prefijo = ObtenerPrefijo(nombre);
            var siguienteNumero = await ObtenerSiguienteNumeroAsync();
            return $"{prefijo}-{siguienteNumero:D4}";
        }

        private static string ObtenerPrefijo(string nombre)
        {
            // Normalize(FormD) separa cada letra acentuada en (letra base + marca diacrítica),
            // para poder filtrar la marca a continuación y quedarse solo con la letra sin acento
            var normalizado = nombre.Normalize(NormalizationForm.FormD);
            var letras = normalizado
                // descarta las marcas diacríticas (acentos) y cualquier carácter que no sea letra (espacios, números...)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetter(c))
                .Select(char.ToUpperInvariant)
                // se queda solo con las 3 primeras letras válidas del nombre
                .Take(3)
                .ToArray();

            // si el nombre no tiene ninguna letra usable, usa "PRD" por defecto;
            // si tiene menos de 3 letras, rellena el resto con 'X' hasta completar 3 caracteres
            return letras.Length == 0
                ? "PRD"
                : new string(letras).PadRight(3, 'X');
        }

        private async Task<int> ObtenerSiguienteNumeroAsync()
        {
            // 1) Saca de la BD solo la columna Sku de todos los productos (no hace falta el resto de campos)
            var skus = await _context.Productos.Select(p => p.Sku).ToListAsync();

            // 2) Recorre cada SKU existente para encontrar el número más alto ya usado
            var maxNumero = 0;
            foreach (var sku in skus)
            {
                // Un SKU válido tiene forma "PREFIJO-0004", así que separarlo por '-' debe dar 2 trozos:
                // partes[0] = "PREFIJO", partes[1] = "0004"
                var partes = sku.Split('-');

                // Solo se tiene en cuenta el SKU si de verdad tiene esos 2 trozos Y el segundo trozo
                // es convertible a número (int.TryParse); si no cumple esto, se ignora ese SKU
                if (partes.Length == 2 && int.TryParse(partes[1], out var numero))
                    // Se va guardando el número más alto visto hasta ahora entre todos los SKU
                    maxNumero = Math.Max(maxNumero, numero);
            }

            // 3) El siguiente número a usar es ese máximo + 1 (cuenta entre TODOS los productos,
            // no reinicia por cada prefijo distinto)
            return maxNumero + 1;
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

        public async Task<(bool Ok, string? Error)> CreateAsync(Producto producto)
        {
            var error = ValidarValoresNoNegativos(producto);
            if (error is not null) return (false, error);

            _context.Productos.Add(producto);
            await _context.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool Ok, string? Error)> UpdateAsync(Producto producto)
        {
            var error = ValidarValoresNoNegativos(producto);
            if (error is not null) return (false, error);

            _context.Productos.Update(producto);
            await _context.SaveChangesAsync();
            return (true, null);
        }

        // Última barrera antes de guardar: el ViewModel ya bloquea esto en el formulario web,
        // pero cualquier otro camino que construya un Producto directamente (otro servicio,
        // una futura API, datos de semilla) pasa también por aquí.
        private static string? ValidarValoresNoNegativos(Producto producto)
        {
            if (producto.PrecioCoste < 0) return "El precio de coste no puede ser negativo.";
            if (producto.PrecioVenta < 0) return "El precio de venta no puede ser negativo.";
            if (producto.StockActual < 0) return "El stock actual no puede ser negativo.";
            if (producto.StockMinimo < 0) return "El stock mínimo no puede ser negativo.";
            return null;
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
