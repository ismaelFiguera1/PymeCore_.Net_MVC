using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using MimeKit;

namespace PymeCore.Services
{
    public class EmailService : IEmailSender
    {
        private readonly GmailApiSettings _gmailSettings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<GmailApiSettings> gmailSettings, ILogger<EmailService> logger)
        {
            _gmailSettings = gmailSettings.Value;
            _logger = logger;
        }

        // Correo "básico" (sin adjunto): confirmación de cuenta de Identity y envío de
        // presupuestos. Va por Gmail API en vez de SMTP porque Railway bloquea SMTP.
        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            await EnviarPorGmailApiAsync(email, subject, new BodyBuilder { HtmlBody = htmlMessage });
        }

        private async Task EnviarPorGmailApiAsync(string email, string subject, BodyBuilder cuerpo)
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress(_gmailSettings.FromName, _gmailSettings.FromEmail));
            mensaje.To.Add(MailboxAddress.Parse(email));
            mensaje.Subject = subject;
            mensaje.Body = cuerpo.ToMessageBody();

            using var buffer = new MemoryStream();
            await mensaje.WriteToAsync(buffer);
            var raw = Convert.ToBase64String(buffer.ToArray())
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", string.Empty);

            try
            {
                using var gmail = CrearGmailService();
                await gmail.Users.Messages.Send(new Message { Raw = raw }, "me").ExecuteAsync();
            }
            catch (Exception ex)
            {
                // No se registra el token ni el cuerpo del correo, solo el motivo del fallo.
                _logger.LogWarning(ex, "Fallo al enviar correo mediante Gmail API a {Destinatario}", email);
                throw;
            }
        }

        private GmailService CrearGmailService()
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _gmailSettings.ClientId,
                    ClientSecret = _gmailSettings.ClientSecret
                },
                Scopes = new[] { GmailService.Scope.GmailSend }
            });

            var token = new TokenResponse { RefreshToken = _gmailSettings.RefreshToken };
            var credential = new UserCredential(flow, "PymeCore", token);

            return new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "PymeCore"
            });
        }

        // Igual que SendEmailAsync, pero con un archivo adjunto (usado, por ejemplo, para mandar
        // el PDF de una factura). No forma parte de IEmailSender porque esa interfaz la exige
        // Identity con una firma fija sin adjuntos; se ofrece como método adicional del servicio.
        public async Task SendEmailWithAttachmentAsync(string email, string subject, string htmlMessage, byte[] adjunto, string nombreAdjunto)
        {
            var cuerpo = new BodyBuilder { HtmlBody = htmlMessage };
            cuerpo.Attachments.Add(nombreAdjunto, adjunto, ContentType.Parse("application/pdf"));
            await EnviarPorGmailApiAsync(email, subject, cuerpo);
        }
    }
}
