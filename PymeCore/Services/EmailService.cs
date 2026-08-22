using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using MimeKit;

namespace PymeCore.Services
{
    public class EmailService : IEmailSender
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var cuerpo = new BodyBuilder { HtmlBody = htmlMessage };
            await EnviarAsync(email, subject, cuerpo);
        }

        // Igual que SendEmailAsync, pero con un archivo adjunto (usado, por ejemplo, para mandar
        // el PDF de una factura). No forma parte de IEmailSender porque esa interfaz la exige
        // Identity con una firma fija sin adjuntos; se ofrece como método adicional del servicio.
        public async Task SendEmailWithAttachmentAsync(string email, string subject, string htmlMessage, byte[] adjunto, string nombreAdjunto)
        {
            var cuerpo = new BodyBuilder { HtmlBody = htmlMessage };
            cuerpo.Attachments.Add(nombreAdjunto, adjunto, ContentType.Parse("application/pdf"));
            await EnviarAsync(email, subject, cuerpo);
        }

        private async Task EnviarAsync(string email, string subject, BodyBuilder cuerpo)
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
            mensaje.To.Add(MailboxAddress.Parse(email));
            mensaje.Subject = subject;
            mensaje.Body = cuerpo.ToMessageBody();

            using var cliente = new SmtpClient();
            await cliente.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.StartTls);
            await cliente.AuthenticateAsync(_settings.Username, _settings.Password);
            await cliente.SendAsync(mensaje);
            await cliente.DisconnectAsync(true);
        }
    }
}
