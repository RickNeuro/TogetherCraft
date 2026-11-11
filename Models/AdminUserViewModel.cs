namespace TogetherCraft.Models;

public sealed class AdminUserViewModel
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string? Email { get; set; }
    public string? MinecraftUsername { get; set; }
    public string Role { get; set; } = "User";
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAtUtc { get; set; }
}
