using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
namespace CFIS.Api;
// SMTP delivery is configured by deployment; reset codes are never logged or returned by the API.
public sealed class IdentityEmail(IConfiguration config) : IEmailSender<IdentityUser>
{
    // Email confirmation is optional in this installation. Invitations verify possession of their secret.
    public Task SendConfirmationLinkAsync(IdentityUser user, string email, string link) => string.IsNullOrWhiteSpace(config["Smtp:Host"]) ? Task.CompletedTask : Send(email, "CFIS 邮箱确认", "请打开：" + link);
    public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string link) => Send(email, "CFIS 密码重置", "请打开：" + link);
    public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string code) => Send(email, "CFIS 密码重置", "重置代码：" + code);
    async Task Send(string email, string subject, string body)
    {
        var host = config["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("SMTP delivery is not configured");
        using var client = new SmtpClient(host, config.GetValue("Smtp:Port", 587)) { EnableSsl = config.GetValue("Smtp:EnableSsl", true) };
        if (config["Smtp:User"] is string user)
            client.Credentials = new NetworkCredential(user, config["Smtp:Password"]);
        using var message = new MailMessage(config["Smtp:From"] ?? throw new InvalidOperationException("Smtp:From required"), email, subject, body);
        await client.SendMailAsync(message);
    }
}
