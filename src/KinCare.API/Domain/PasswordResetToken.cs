namespace KinCare.API.Domain;

public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public AppUser User { get; set; } = null!;

    public bool IsValid => UsedAt is null && ExpiresAt > DateTime.UtcNow;
}
