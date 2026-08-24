using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PymeCore.Data;
using PymeCore.Models;

namespace PymeCore.Services
{
    public class PresupuestoService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public PresupuestoService(ApplicationDbContext context, IEmailSender emailSender, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _emailSender = emailSender;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<List<Presupuesto>> GetAllAsync(string? buscar = null)
        {
            var query = _context.Presupuestos.Include(p => p.Cliente).AsQueryable();

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                var termino = buscar.ToLower();
                query = query.Where(p =>
                    p.Numero.ToLower().Contains(termino) ||
                    p.Cliente!.Nombre.ToLower().Contains(termino));
            }

            return await query.OrderByDescending(p => p.Fecha).ToListAsync();
        }

        public async Task<Presupuesto?> GetByIdAsync(int id)
        {
            return await _context.Presupuestos
                .Include(p => p.Cliente)
                .Include(p => p.Lineas)
                    .ThenInclude(l => l.Producto)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<string> GenerarNumeroAsync()
        {

            var año = DateTime.UtcNow.Year;
            var prefix = $"PRES-{año}-";

            // 2) Trae de la BD solo el campo Numero, y solo de los presupuestos de ESTE año
            // (StartsWith(prefix) se traduce a un LIKE 'PRES-2026-%' en SQL, así que filtra en la propia BD,
            // no trae todos los presupuestos de todos los años para descartarlos después).
            var numeros = await _context.Presupuestos
                .Where(p => p.Numero.StartsWith(prefix))
                .Select(p => p.Numero)
                .ToListAsync();

            // 3) De cada número ya guardado (ej. "PRES-2026-004"), se queda con lo que hay después del prefijo
            // (n[prefix.Length..] es "004") y lo intenta convertir a int; si algún Numero no tuviera ese formato
            // (dato corrupto o antiguo), int.TryParse fallaría y se usa 0 en su lugar en vez de reventar.
            // DefaultIfEmpty(0) cubre el caso de que sea el primer presupuesto del año (la lista numeros está vacía,
            // y Max() sobre una secuencia vacía lanzaría excepción sin esto).
            // Al máximo encontrado se le suma 1: ese es el siguiente correlativo a usar.
            var siguiente = numeros
                .Select(n => int.TryParse(n[prefix.Length..], out var num) ? num : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            // 4) Se compone el número final con el correlativo en 3 dígitos (D3 → "004"), ej. "PRES-2026-004"
            return $"{prefix}{siguiente:D3}";
        }

        public async Task CreateAsync(Presupuesto presupuesto)
        {
            await GuardarConNumeroUnicoAsync(presupuesto);
        }

        // Genera el número y lo guarda; si otra petición se adelantó con el mismo número
        // (choque detectado por el índice único IX_Presupuestos_Numero), regenera y reintenta
        // en vez de dejar reventar la petición con un error de base de datos.
        private async Task GuardarConNumeroUnicoAsync(Presupuesto presupuesto)
        {
            const int maxIntentos = 3;

            for (var intento = 1; ; intento++)
            {
                presupuesto.Numero = await GenerarNumeroAsync();
                _context.Presupuestos.Add(presupuesto);

                try
                {
                    await _context.SaveChangesAsync();
                    return;
                }
                catch (DbUpdateException ex) when (EsNumeroDuplicado(ex) && intento < maxIntentos)
                {
                    _context.Entry(presupuesto).State = EntityState.Detached;
                }
            }
        }

        private static bool EsNumeroDuplicado(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_Presupuestos_Numero" };

        public async Task UpdateAsync(Presupuesto presupuesto)
        {
            _context.Presupuestos.Update(presupuesto);
            await _context.SaveChangesAsync();
        }

        // Igual que en PedidoService.CrearDesdePresupuestoAsync: el presupuesto nuevo, sus
        // líneas copiadas y el total recalculado se guardan en una única transacción, y el
        // reintento por choque de número (IX_Presupuestos_Numero) repite la operación entera.
        public async Task<Presupuesto> DuplicarAsync(int id)
        {
            var original = await GetByIdAsync(id);
            if (original is null) throw new InvalidOperationException("Presupuesto no encontrado.");

            const int maxIntentos = 3;

            for (var intento = 1; ; intento++)
            {
                var nuevo = new Presupuesto
                {
                    Numero        = await GenerarNumeroAsync(),
                    ClienteId     = original.ClienteId,
                    Fecha         = DateTime.UtcNow,
                    Estado        = EstadoPresupuesto.Borrador,
                    Observaciones = original.Observaciones,
                    Total         = 0
                };

                await using var tx = await _context.Database.BeginTransactionAsync();

                _context.Presupuestos.Add(nuevo);

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (EsNumeroDuplicado(ex) && intento < maxIntentos)
                {
                    _context.Entry(nuevo).State = EntityState.Detached;
                    continue;
                }

                foreach (var linea in original.Lineas)
                {
                    _context.LineasPresupuesto.Add(new LineaPresupuesto
                    {
                        PresupuestoId  = nuevo.Id,
                        ProductoId     = linea.ProductoId,
                        Descripcion    = linea.Descripcion,
                        Cantidad       = linea.Cantidad,
                        PrecioUnitario = linea.PrecioUnitario,
                        Subtotal       = linea.Subtotal
                    });
                }

                nuevo.Total = original.Lineas.Sum(l => l.Subtotal);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return nuevo;
            }
        }

        private static readonly Regex FormatoEmail = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        public async Task<(bool Ok, string? Error)> EnviarAsync(int id)
        {
            var presupuesto = await GetByIdAsync(id);
            if (presupuesto is null) return (false, "Presupuesto no encontrado.");
            if (presupuesto.Estado != EstadoPresupuesto.Borrador)
                return (false, $"Solo se puede enviar un presupuesto en estado Borrador (estado actual: {presupuesto.Estado}).");
            if (!presupuesto.Lineas.Any())
                return (false, "No se puede enviar un presupuesto sin líneas. Añade al menos una.");

            if (presupuesto.Cliente is null)
                return (false, "El presupuesto no tiene un cliente asociado.");

            if (string.IsNullOrWhiteSpace(presupuesto.Cliente.Email))
                return (false, "El cliente no tiene ningún email registrado. Añádelo desde su ficha antes de enviar el presupuesto.");

            if (!FormatoEmail.IsMatch(presupuesto.Cliente.Email))
                return (false, $"El email del cliente ('{presupuesto.Cliente.Email}') no tiene un formato válido. Corrígelo desde su ficha antes de enviar el presupuesto.");

            // Token de un solo uso para que el cliente pueda responder desde el correo sin tener
            // cuenta en PymeCore. Se genera aquí (no antes) porque solo debe quedar preparado si
            // el envío llega a completarse; nunca se guarda ni se registra el token en claro, solo
            // su hash SHA-256 (64 caracteres hex, ya previsto en TokenRespuestaHash).
            var tokenBytes = RandomNumberGenerator.GetBytes(32);
            var tokenParaUrl = WebEncoders.Base64UrlEncode(tokenBytes);
            var tokenHash = CalcularHashToken(tokenParaUrl);
            var tokenExpiraUtc = DateTime.UtcNow.AddDays(7);

            var request = _httpContextAccessor.HttpContext!.Request;
            var baseUrl = $"{request.Scheme}://{request.Host}";
            var urlAceptar = $"{baseUrl}/Presupuestos/Responder?token={tokenParaUrl}&decision=aceptar";
            var urlRechazar = $"{baseUrl}/Presupuestos/Responder?token={tokenParaUrl}&decision=rechazar";

            // El correo se envía y se espera a que termine ANTES de tocar el estado: si Gmail no lo
            // acepta (fallo de conexión, autenticación, etc.), no debe quedar como "Enviado" ni
            // guardarse el token/caducidad de una respuesta que nunca se pudo ofrecer al cliente.
            try
            {
                await _emailSender.SendEmailAsync(
                    presupuesto.Cliente.Email,
                    $"Presupuesto {presupuesto.Numero}",
                    ConstruirCuerpoHtml(presupuesto, urlAceptar, urlRechazar));
            }
            catch (Exception)
            {
                return (false, "No se ha podido enviar el correo al cliente. Inténtalo de nuevo más tarde.");
            }

            presupuesto.Estado = EstadoPresupuesto.Enviado;
            presupuesto.TokenRespuestaHash = tokenHash;
            presupuesto.TokenRespuestaExpiraUtc = tokenExpiraUtc;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        private static string CalcularHashToken(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        // Usado por la página pública de confirmación (GET /Presupuestos/Responder): recalcula el
        // hash del token recibido y solo devuelve el presupuesto si el hash coincide, sigue en
        // estado Enviado y el token no ha caducado. Cualquier fallo se trata igual (null), para que
        // el llamador muestre siempre el mismo mensaje genérico sin revelar cuál fue el problema.
        public async Task<Presupuesto?> ValidarTokenRespuestaAsync(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            var tokenHash = CalcularHashToken(token);

            var presupuesto = await _context.Presupuestos
                .Include(p => p.Cliente)
                .Include(p => p.Lineas)
                    .ThenInclude(l => l.Producto)
                .FirstOrDefaultAsync(p => p.TokenRespuestaHash == tokenHash);

            if (presupuesto is null) return null;
            if (presupuesto.Estado != EstadoPresupuesto.Enviado) return null;
            if (presupuesto.TokenRespuestaExpiraUtc is null || presupuesto.TokenRespuestaExpiraUtc <= DateTime.UtcNow) return null;

            return presupuesto;
        }

        // Usado por el POST público (ConfirmarRespuesta): repite TODAS las comprobaciones de
        // ValidarTokenRespuestaAsync (hash, caducidad, estado Enviado) y además valida la propia
        // decisión. Si cualquier cosa falla, no toca la entidad ni llama a SaveChangesAsync.
        public async Task<(bool Ok, string? Numero, string? Decision)> ConfirmarRespuestaAsync(string? token, string? decision)
        {
            if (decision != "aceptar" && decision != "rechazar")
                return (false, null, null);

            var presupuesto = await ValidarTokenRespuestaAsync(token);
            if (presupuesto is null)
                return (false, null, null);

            presupuesto.Estado = decision == "aceptar" ? EstadoPresupuesto.Aceptado : EstadoPresupuesto.Rechazado;

            // Token de un solo uso: una vez consumido, ya no debe servir para volver a confirmar
            // (ni el enlace de "aceptar" ni el de "rechazar" del mismo correo funcionarán de nuevo).
            presupuesto.TokenRespuestaHash = null;
            presupuesto.TokenRespuestaExpiraUtc = null;

            await _context.SaveChangesAsync();

            return (true, presupuesto.Numero, decision);
        }

        // Mismo verde que usa FacturaPdfDocument para el banner "FACTURA PAGADA"
        // (QuestPDF.Helpers.Colors.Green.Darken1 = #43A047) y que FacturaService usa en su
        // correo, para que los dos correos de PymeCore compartan el mismo color corporativo.
        private const string ColorCorporativo = "#43A047";

        private static string ConstruirCuerpoHtml(Presupuesto presupuesto, string urlAceptar, string urlRechazar)
        {
            var cultura = CultureInfo.GetCultureInfo("es-ES");
            var sb = new StringBuilder();

            var numero = WebUtility.HtmlEncode(presupuesto.Numero);
            var clienteNombre = WebUtility.HtmlEncode(presupuesto.Cliente!.Nombre);
            var urlAceptarSegura = WebUtility.HtmlEncode(urlAceptar);
            var urlRechazarSegura = WebUtility.HtmlEncode(urlRechazar);

            // Tabla exterior al 100% que centra una tabla interior de máximo 680px (mismo
            // patrón "bulletproof" de email que FacturaService.ConstruirCuerpoHtml).
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;\">");
            sb.Append("<tr><td align=\"center\" style=\"padding:24px 16px;\">");
            sb.Append("<table role=\"presentation\" width=\"680\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" align=\"center\" style=\"width:100%; max-width:680px; font-family: Arial, Helvetica, sans-serif; color:#202124;\">");
            sb.Append("<tr><td style=\"padding:24px;\">");

            sb.Append($"<h1 style=\"margin:0 0 12px 0; font-size:22px; color:{ColorCorporativo};\">PymeCore</h1>");
            sb.Append($"<div style=\"border-top:3px solid {ColorCorporativo}; margin:0 0 20px 0; font-size:0; line-height:0;\">&nbsp;</div>");

            sb.Append($"<p style=\"margin:0 0 16px 0; font-size:14px;\">Hola, {clienteNombre}:</p>");
            sb.Append($"<p style=\"margin:0 0 20px 0; font-size:14px;\">Te enviamos el presupuesto {numero} para que puedas revisarlo.</p>");

            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"10\" cellspacing=\"0\" style=\"border-collapse:collapse; margin:0 0 20px 0; font-size:14px;\">");
            sb.Append("<tr style=\"background-color:#f8f9fa;\">");
            sb.Append("<th align=\"left\" style=\"border:1px solid #dadce0;\">Producto</th>");
            sb.Append("<th align=\"left\" style=\"border:1px solid #dadce0;\">Descripción</th>");
            sb.Append("<th align=\"left\" style=\"border:1px solid #dadce0;\">Cantidad</th>");
            sb.Append("<th align=\"left\" style=\"border:1px solid #dadce0;\">Precio unitario</th>");
            sb.Append("<th align=\"left\" style=\"border:1px solid #dadce0;\">Subtotal</th>");
            sb.Append("</tr>");

            foreach (var linea in presupuesto.Lineas)
            {
                sb.Append("<tr>");
                sb.Append($"<td style=\"border:1px solid #dadce0;\">{WebUtility.HtmlEncode(linea.Producto?.Nombre)}</td>");
                sb.Append($"<td style=\"border:1px solid #dadce0;\">{WebUtility.HtmlEncode(linea.Descripcion)}</td>");
                sb.Append($"<td style=\"border:1px solid #dadce0;\">{linea.Cantidad}</td>");
                sb.Append($"<td style=\"border:1px solid #dadce0;\">{linea.PrecioUnitario.ToString("N2", cultura)} €</td>");
                sb.Append($"<td style=\"border:1px solid #dadce0;\">{linea.Subtotal.ToString("N2", cultura)} €</td>");
                sb.Append("</tr>");
            }

            sb.Append("<tr style=\"background-color:#f8f9fa;\">");
            sb.Append("<td colspan=\"4\" align=\"center\" style=\"border:1px solid #dadce0; font-weight:bold;\">Total:</td>");
            sb.Append($"<td style=\"border:1px solid #dadce0; font-weight:bold; font-size:16px;\">{presupuesto.Total.ToString("N2", cultura)} €</td>");
            sb.Append("</tr>");
            sb.Append("</table>");

            if (!string.IsNullOrWhiteSpace(presupuesto.Observaciones))
            {
                sb.Append($"<p style=\"margin:0 0 20px 0; font-size:14px;\">Observaciones: {WebUtility.HtmlEncode(presupuesto.Observaciones)}</p>");
            }

            sb.Append("<p style=\"margin:0 0 16px 0; font-size:14px;\">Puedes aceptar o rechazar el presupuesto utilizando los siguientes botones.</p>");

            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:0 0 24px 0;\"><tr>");
            sb.Append($"<td style=\"padding-right:10px;\"><a href=\"{urlAceptarSegura}\" style=\"display:inline-block;padding:10px 20px;background-color:{ColorCorporativo};color:#ffffff;text-decoration:none;border-radius:4px;font-size:14px;\">Aceptar presupuesto</a></td>");
            sb.Append($"<td><a href=\"{urlRechazarSegura}\" style=\"display:inline-block;padding:10px 20px;background-color:#dc3545;color:#ffffff;text-decoration:none;border-radius:4px;font-size:14px;\">Rechazar presupuesto</a></td>");
            sb.Append("</tr></table>");

            sb.Append("<p style=\"margin:0 0 16px 0; font-size:14px;\">Gracias por confiar en nosotros.</p>");
            sb.Append("<p style=\"margin:0 0 24px 0; font-size:14px; font-weight:bold;\">Equipo de PymeCore</p>");

            sb.Append("<div style=\"border-top:1px solid #dadce0; margin:0 0 12px 0; font-size:0; line-height:0;\">&nbsp;</div>");
            sb.Append("<p style=\"margin:0; font-size:12px; color:#5f6368;\">Este es un correo automático. Por favor, no respondas a este mensaje.</p>");

            sb.Append("</td></tr></table>");
            sb.Append("</td></tr></table>");

            return sb.ToString();
        }

        // Guardar la línea y recalcular el Total ocurren dentro de la misma transacción:
        // si falla el recálculo, tampoco queda guardada la línea (todo o nada).
        public async Task AgregarLineaAsync(LineaPresupuesto linea)
        {
            linea.Subtotal = linea.Cantidad * linea.PrecioUnitario;

            await using var tx = await _context.Database.BeginTransactionAsync();

            _context.LineasPresupuesto.Add(linea);
            await _context.SaveChangesAsync();

            await RecalcularTotalAsync(linea.PresupuestoId);
            await tx.CommitAsync();
        }

        public async Task EliminarLineaAsync(int lineaId, int presupuestoId)
        {
            var linea = await _context.LineasPresupuesto
                .FirstOrDefaultAsync(l => l.Id == lineaId && l.PresupuestoId == presupuestoId);
            if (linea is null) return;

            await using var tx = await _context.Database.BeginTransactionAsync();

            _context.LineasPresupuesto.Remove(linea);
            await _context.SaveChangesAsync();

            await RecalcularTotalAsync(presupuestoId);
            await tx.CommitAsync();
        }

        private async Task RecalcularTotalAsync(int presupuestoId)
        {
            var presupuesto = await _context.Presupuestos
                .Include(p => p.Lineas)
                .FirstOrDefaultAsync(p => p.Id == presupuestoId);

            if (presupuesto is null) return;

            presupuesto.Total = presupuesto.Lineas.Sum(l => l.Subtotal);
            await _context.SaveChangesAsync();
        }
    }
}
