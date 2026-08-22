using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PymeCore.Data;
using PymeCore.Models;

namespace PymeCore.Services
{
    public class FacturaService
    {
        private readonly ApplicationDbContext _context;
        private readonly FacturaSnapshotService _snapshotService;
        private readonly FacturaPdfService _pdfService;
        private readonly EmailService _emailService;

        public FacturaService(
            ApplicationDbContext context,
            FacturaSnapshotService snapshotService,
            FacturaPdfService pdfService,
            EmailService emailService)
        {
            _context = context;
            _snapshotService = snapshotService;
            _pdfService = pdfService;
            _emailService = emailService;
        }

        public async Task<List<Factura>> GetAllAsync()
        {
            return await _context.Facturas
                .Include(f => f.Cliente)
                .OrderByDescending(f => f.FechaEmision)
                .ToListAsync();
        }

        public async Task<Factura?> GetByIdAsync(int id)
        {
            return await _context.Facturas
                .Include(f => f.Cliente)
                .Include(f => f.Pedido)
                .Include(f => f.CreadoPorUsuario)
                .FirstOrDefaultAsync(f => f.Id == id);
        }

        public async Task<string> GenerarNumeroAsync()
        {
            var año = DateTime.UtcNow.Year;
            var prefix = $"FAC-{año}-";

            var numeros = await _context.Facturas
                .Where(f => f.Numero.StartsWith(prefix))
                .Select(f => f.Numero)
                .ToListAsync();

            var siguiente = numeros
                .Select(n => int.TryParse(n[prefix.Length..], out var num) ? num : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            return $"{prefix}{siguiente:D3}";
        }

        public async Task<(bool Ok, string? Error, Factura? Factura)> GenerarDesdePedidoAsync(int pedidoId, string? usuarioId)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.Cliente)
                .Include(p => p.Lineas)
                    .ThenInclude(l => l.Producto)
                .FirstOrDefaultAsync(p => p.Id == pedidoId);

            if (pedido is null)
                return (false, "Pedido no encontrado.", null);

            if (pedido.Estado != EstadoPedido.Completado)
                return (false, $"Solo se puede facturar un pedido en estado Completado (estado actual: {pedido.Estado}).", null);

            if (pedido.FacturaId is not null)
                return (false, "Este pedido ya tiene una factura generada.", null);

            if (!pedido.Lineas.Any())
                return (false, "No se puede facturar un pedido sin líneas.", null);

            const decimal porcentajeIva = 21m;
            var baseImponible = pedido.Total;
            var totalIva = Math.Round(baseImponible * porcentajeIva / 100m, 2);

            // Bucle de reintento: si otra petición se adelantó con el mismo número de factura
            // (choque detectado por el índice único IX_Facturas_Numero), se descarta la transacción,
            // se regenera el número y se vuelve a intentar, en vez de reventar con un error de BD.
            const int maxIntentos = 3;

            for (var intento = 1; ; intento++)
            {
                var factura = new Factura
                {
                    Numero = await GenerarNumeroAsync(),
                    ClienteId = pedido.ClienteId,
                    PedidoId = pedido.Id,
                    FechaEmision = DateTime.UtcNow,
                    Estado = EstadoFactura.Pendiente,
                    BaseImponible = baseImponible,
                    PorcentajeIVA = porcentajeIva,
                    TotalIVA = totalIva,
                    Total = baseImponible + totalIva,
                    CreadoPorUsuarioId = usuarioId
                };

                await using var transaction = await _context.Database.BeginTransactionAsync();

                _context.Facturas.Add(factura);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (EsNumeroDuplicado(ex) && intento < maxIntentos)
                {
                    _context.Entry(factura).State = EntityState.Detached;
                    continue;
                }

                pedido.FacturaId = factura.Id;

                var errorSnapshot = _snapshotService.Crear(factura, pedido);
                if (errorSnapshot is not null)
                    return (false, errorSnapshot, null);

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return (true, null, factura);
            }
        }

        private static bool EsNumeroDuplicado(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_Facturas_Numero" };

        public async Task<(bool Ok, string? Error)> CambiarEstadoAsync(int facturaId, EstadoFactura nuevoEstado)
        {
            var factura = await _context.Facturas.FirstOrDefaultAsync(f => f.Id == facturaId);

            if (factura is null)
                return (false, "Factura no encontrada.");

            var transicionValida = (factura.Estado, nuevoEstado) switch
            {
                (EstadoFactura.Pendiente, EstadoFactura.Pagada) => true,
                (EstadoFactura.Pendiente, EstadoFactura.Anulada) => true,
                _ => false
            };

            if (!transicionValida)
                return (false, $"No se puede cambiar de {factura.Estado} a {nuevoEstado}.");

            factura.Estado = nuevoEstado;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        private static readonly Regex FormatoEmail = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        // Envía (o reenvía) el PDF de la factura al cliente por email. No cambia el Estado:
        // es solo una notificación, a diferencia de PresupuestoService.EnviarAsync, que sí
        // marca el presupuesto como Enviado.
        public async Task<(bool Ok, string? Error)> EnviarPorEmailAsync(int id)
        {
            var factura = await _context.Facturas
                .Include(f => f.Cliente)
                .Include(f => f.Pedido)
                .FirstOrDefaultAsync(f => f.Id == id);

            if (factura is null) return (false, "Factura no encontrada.");

            if (factura.Estado == EstadoFactura.Anulada)
                return (false, "No se puede enviar por email una factura anulada.");

            if (factura.Pedido is null)
                return (false, "La factura no tiene un pedido de origen asociado.");

            if (factura.Cliente is null)
                return (false, "La factura no tiene un cliente asociado.");

            if (string.IsNullOrWhiteSpace(factura.Cliente.Email))
                return (false, "El cliente no tiene ningún email registrado. Añádelo desde su ficha antes de enviar la factura.");

            if (!FormatoEmail.IsMatch(factura.Cliente.Email))
                return (false, $"El email del cliente ('{factura.Cliente.Email}') no tiene un formato válido. Corrígelo desde su ficha antes de enviar la factura.");

            var (snapshot, error) = await _snapshotService.GetDtoByFacturaIdAsync(id);
            if (snapshot is null)
                return (false, error ?? "No se encontró el histórico de esta factura.");

            byte[] pdf;
            try
            {
                pdf = _pdfService.Generar(snapshot, factura.Estado);
            }
            catch (Exception)
            {
                return (false, "No se pudo generar el PDF de la factura.");
            }

            try
            {
                await _emailService.SendEmailWithAttachmentAsync(
                    factura.Cliente.Email,
                    $"Factura {factura.Numero}",
                    ConstruirCuerpoHtml(factura),
                    pdf,
                    $"Factura_{factura.Numero}.pdf");
            }
            catch (Exception)
            {
                return (false, "No se ha podido enviar el correo al cliente. Inténtalo de nuevo más tarde.");
            }

            return (true, null);
        }

        private static string ConstruirCuerpoHtml(Factura factura)
        {
            var cultura = CultureInfo.GetCultureInfo("es-ES");

            return $"""
                <h2>Factura {WebUtility.HtmlEncode(factura.Numero)}</h2>
                <p>Fecha de emisión: {factura.FechaEmision.ToLocalTime():dd/MM/yyyy}</p>
                <p>Pedido de origen: {WebUtility.HtmlEncode(factura.Pedido!.Numero)}</p>
                <p><strong>Total: {factura.Total.ToString("N2", cultura)} €</strong></p>
                <p>Adjuntamos el PDF de la factura.</p>
                """;
        }
    }
}
