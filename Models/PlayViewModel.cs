namespace TogetherCraft.Models;

public sealed class GameConnectionItem
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";

    public GameConnectionItem() { }

    public GameConnectionItem(string name, string address)
    {
        Name = name;
        Address = address;
    }
}

public sealed class PlayViewModel
{
    public string Username { get; set; } = "";
    public IReadOnlyList<GameConnectionItem> Servers { get; set; } = Array.Empty<GameConnectionItem>();
}
