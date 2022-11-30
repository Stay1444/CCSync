using Ardalis.GuardClauses;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CCSync.Server.Utils;

public sealed class CCSyncYaml
{
    private static ISerializer? _serializer;
    private static IDeserializer? _deserializer;

    public static ISerializer Serializer
    {
        get
        {
            if (_serializer is not null) return _serializer;
            _serializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
            return _serializer;
        }
    }

    public static IDeserializer Deserializer
    {
        get
        {
            if (_deserializer is not null) return _deserializer;
            _deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
            return _deserializer;
        }
    } 
    
    public static Task<T> ReadYamlAsync<T>(string path, Func<T> factory) where T : class, new()
    {
        return ReadYamlAsync<T>(path, factory, CancellationToken.None);
    }

    public static Task WriteYamlAsync<T>(string path, T obj)
    {
        return WriteYamlAsync(path, obj, CancellationToken.None);
    }
    
    public static async Task<T> ReadYamlAsync<T>(string path, Func<T> factory, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(path);

        if (!File.Exists(path))
        {
            var n = factory();
            await WriteYamlAsync(path, n, cancellationToken);
            return n;
        }
        
        var serialized = await File.ReadAllTextAsync(path, cancellationToken);

        return Deserializer.Deserialize<T>(serialized);
    }
    
    public static async Task WriteYamlAsync<T>(string path, T obj, CancellationToken cancellationToken)
    {
        Guard.Against.Null(obj);
        Guard.Against.NullOrEmpty(path);
        
        var serialized = Serializer.Serialize(obj);

        await File.WriteAllTextAsync(path, serialized, cancellationToken);
    }
}