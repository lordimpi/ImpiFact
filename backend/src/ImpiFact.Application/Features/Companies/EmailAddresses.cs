using System.Net.Mail;

namespace ImpiFact.Application.Features.Companies;

/// <summary>The email address rule of the company data, shared by the company email and its mail server sender.</summary>
internal static class EmailAddresses
{
    /// <summary>Empty, or a single address with a dot in its domain (also used by the company mail server).</summary>
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return true;
        }

        try
        {
            var address = new MailAddress(email);
            return address.Address == email && email.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
