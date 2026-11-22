namespace Nomi.Academic.Domain;

public sealed class UserProfile
{
    private UserProfile() { }
    public UserProfile(Guid userId, string? identityName)
    {
        UserId = userId;
        DisplayName = string.IsNullOrWhiteSpace(identityName) ? "Estudante" : identityName.Trim()[..Math.Min(identityName.Trim().Length, 100)];
    }
    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = "Estudante";
    public string Theme { get; private set; } = "system";
    public string Locale { get; private set; } = "pt-BR";
    public string TimeZone { get; private set; } = "America/Sao_Paulo";
    public int Version { get; private set; }

    public void Update(string displayName, string theme, string locale, string timeZone)
    {
        DisplayName = displayName;
        Theme = theme;
        Locale = locale;
        TimeZone = timeZone;
        Version++;
    }
}
