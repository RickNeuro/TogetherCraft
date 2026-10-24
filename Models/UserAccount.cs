namespace TogetherCraft.Models;

public sealed class UserAccount
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string UsernameNormalized { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string MinecraftUsername { get; set; } = "";
    public string Role { get; set; } = "User";
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}
