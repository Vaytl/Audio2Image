namespace Audio2Image.Core.Models;

public class UserTag
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public string Color { get; set; } = "#FF6B35";
}
