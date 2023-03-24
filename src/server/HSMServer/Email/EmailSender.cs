using NLog;
using System;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace HSMServer.Email
{
    public class EmailSender
    {
        private readonly string _server;
        private readonly string _login;
        private readonly string _password;
        private readonly string _fromEmail;
        private readonly string _toEmail;
        private readonly int? _port;
        private readonly ILogger _logger;

        public EmailSender(string server, int? port, string login, string password,
            string fromEmail, string toEmail)
        {
            _server = server;
            _login = login;
            _port = port;
            _password = password;
            _fromEmail = fromEmail;
            _toEmail = toEmail;
            _logger = LogManager.GetCurrentClassLogger();
        }

        public void Send(string subject, string body)
        {
            InternalSend(subject, body);
        }

        private void InternalSend(string subject, string body)
        {
            if (string.IsNullOrEmpty(_fromEmail) || string.IsNullOrEmpty(_toEmail)
                                                 || string.IsNullOrEmpty(body))
                return;

            try
            {
                MailMessage message = new MailMessage
                {
                    Subject = subject,
                    Body = body,
                    BodyEncoding = Encoding.UTF8,
                    From = new MailAddress(_fromEmail),
                    IsBodyHtml = false
                };
                message.To.Add(_toEmail);


                SmtpClient client = _port.HasValue ? new SmtpClient(_server, _port.Value)
                    : new SmtpClient(_server);
                client.Host = _server;
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(_login, _password);
                client.EnableSsl = true;

                client.Send(message);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to send email!");
            }
        }
    }
}
