namespace TogetherCraft.Models;

public sealed class PlayViewModel
{
    public string Username { get; set; } = "";
    public bool CanAccessVanilla { get; set; }
    public bool CanAccessCreate { get; set; }
}

public sealed class WorldConnectionViewModel
{
    public string Username { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kicker { get; set; } = "";
    public string Description { get; set; } = "";
    public string Address { get; set; } = "";
}
