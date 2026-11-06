namespace TogetherCraft.Models;

public sealed class UserAccount
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string UsernameNormalized { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? Email { get; set; }
    public string? MinecraftUsername { get; set; }
    public Guid? MinecraftUuid { get; set; }
    public bool WantsFabric { get; set; } = true;
    public bool WantsCreate { get; set; } = true;
    public string Role { get; set; } = "User";
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}
