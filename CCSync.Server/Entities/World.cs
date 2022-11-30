using YamlDotNet.Serialization;

namespace CCSync.Server.Entities;

public record World
{
    public string Id { get; set; }
    
    [YamlMember(Description = "Friendly world name that gets shown to the clients")]
    public required string Name { get; init; }
    
    [YamlMember(Description = "Absolute path to the world folder which contains the ComputerCraft computers files. ~/world/computercraft/computer/")]
    public required string Path { get; init; }
}