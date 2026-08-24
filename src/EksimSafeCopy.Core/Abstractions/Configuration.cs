namespace EksimSafeCopy.Core.Abstractions;

using System.Text.Json;

public interface IConfiguration
{
    string? this[string key] { get; set; }
    
    T Get<T>(string key, T defaultValue = default!);
    T GetRequired<T>(string key);
    IConfigurationSection GetSection(string key);
    IEnumerable<IConfigurationSection> GetChildren();
    IChangeToken GetReloadToken();
}

public interface IConfigurationSection : IConfiguration
{
    string Key { get; }
    string Path { get; }
    string? Value { get; set; }
}

public interface IChangeToken
{
    bool HasChanged { get; }
    bool ActiveChangeCallbacks { get; }
    IDisposable RegisterChangeCallback(Action<object?> callback, object? state);
}

public sealed class ConfigurationRoot : IConfiguration
{
    private readonly List<IConfigurationProvider> _providers;
    
    public ConfigurationRoot(IList<IConfigurationProvider> providers)
    {
        _providers = providers.ToList();
    }
    
    public string? this[string key]
    {
        get => _providers.LastOrDefault(p => p.TryGet(key, out var value))?.Get(key);
        set
        {
            foreach (var provider in _providers)
                provider.Set(key, value);
        }
    }
    
    public T Get<T>(string key, T defaultValue = default!)
    {
        var value = this[key];
        if (value == null) return defaultValue!;
        
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue!;
        }
    }
    
    public T GetRequired<T>(string key)
    {
        var value = this[key] ?? throw new KeyNotFoundException($"Configuration key '{key}' not found");
        return (T)Convert.ChangeType(value, typeof(T));
    }
    
    public IConfigurationSection GetSection(string key) => new ConfigurationSection(this, key);
    
    public IEnumerable<IConfigurationSection> GetChildren()
    {
        var prefixes = _providers.SelectMany(p => p.GetChildKeys(Enumerable.Empty<string>(), null))
            .Distinct()
            .Select(k => k.Split(':')[0])
            .Distinct();
        
        return prefixes.Select(p => new ConfigurationSection(this, p));
    }
    
    public IChangeToken GetReloadToken()
    {
        return new CompositeChangeToken(_providers.Select(p => p.GetReloadToken()));
    }
}

public sealed class ConfigurationSection : IConfigurationSection
{
    private readonly IConfiguration _root;
    private readonly string _key;
    
    public ConfigurationSection(IConfiguration root, string key)
    {
        _root = root;
        _key = key;
    }
    
    public string Key => _key.Split(':').Last();
    public string Path => _key;
    
    public string? this[string key]
    {
        get => _root[$"{_key}:{key}"];
        set => _root[$"{_key}:{key}"] = value;
    }
    
    public string? Value
    {
        get => _root[_key];
        set => _root[_key] = value;
    }
    
    public T Get<T>(string key, T defaultValue = default!) => _root.Get<T>($"{_key}:{key}", defaultValue);
    public T GetRequired<T>(string key) => _root.GetRequired<T>($"{_key}:{key}");
    
    public IConfigurationSection GetSection(string key) => new ConfigurationSection(_root, $"{_key}:{key}");
    
    public IEnumerable<IConfigurationSection> GetChildren() => _root.GetSection(_key).GetChildren();
    
    public IChangeToken GetReloadToken() => _root.GetReloadToken();
}

public interface IConfigurationProvider
{
    bool TryGet(string key, out string? value);
    string? Get(string key);
    void Set(string key, string? value);
    IChangeToken GetReloadToken();
    IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath);
}

public sealed class MemoryConfigurationProvider : IConfigurationProvider
{
    private readonly Dictionary<string, string?> _data = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _tokenSource = new();
    
    public bool TryGet(string key, out string? value) => _data.TryGetValue(key, out value);
    
    public string? Get(string key) => _data.TryGetValue(key, out var value) ? value : null;
    
    public void Set(string key, string? value)
    {
        if (value == null) _data.Remove(key);
        else _data[key] = value;
        _tokenSource.Cancel();
    }
    
    public IChangeToken GetReloadToken() => new CancellationChangeToken(_tokenSource.Token);
    
    public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
    {
        var prefix = parentPath == null ? "" : parentPath + ":";
        return _data.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Substring(prefix.Length).Split(':')[0])
            .Distinct()
            .Concat(earlierKeys)
            .Distinct();
    }
}

public sealed class JsonConfigurationProvider : IConfigurationProvider
{
    private readonly string _path;
    private readonly bool _optional;
    private readonly bool _reloadOnChange;
    private Dictionary<string, string?> _data = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _tokenSource = new();
    private FileSystemWatcher? _watcher;
    
    public JsonConfigurationProvider(string path, bool optional = false, bool reloadOnChange = true)
    {
        _path = path;
        _optional = optional;
        _reloadOnChange = reloadOnChange;
        Load();
        
        if (reloadOnChange && File.Exists(_path))
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(_path)!, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite
            };
            _watcher.Changed += (_, _) => Reload();
            _watcher.EnableRaisingEvents = true;
        }
    }
    
    private void Load()
    {
        if (!File.Exists(_path))
        {
            if (!_optional) throw new FileNotFoundException($"Configuration file not found: {_path}");
            return;
        }
        
        var json = File.ReadAllText(_path);
        _data = Flatten(JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new());
    }
    
    private void Reload()
    {
        Load();
        _tokenSource.Cancel();
    }
    
    private static Dictionary<string, string?> Flatten(Dictionary<string, object> dict, string prefix = "")
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var (key, value) in dict)
        {
            var fullKey = string.IsNullOrEmpty(prefix) ? key : $"{prefix}:{key}";
            
            if (value is Dictionary<string, object> nested)
            {
                foreach (var (nk, nv) in Flatten(nested, fullKey))
                    result[nk] = nv;
            }
            else
            {
                result[fullKey] = value?.ToString();
            }
        }
        
        return result;
    }
    
    public bool TryGet(string key, out string? value) => _data.TryGetValue(key, out value);
    
    public string? Get(string key) => _data.TryGetValue(key, out var value) ? value : null;
    
    public void Set(string key, string? value)
    {
        if (value == null) _data.Remove(key);
        else _data[key] = value;
    }
    
    public IChangeToken GetReloadToken() => new CancellationChangeToken(_tokenSource.Token);
    
    public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
    {
        var prefix = parentPath == null ? "" : parentPath + ":";
        return _data.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Substring(prefix.Length).Split(':')[0])
            .Distinct()
            .Concat(earlierKeys)
            .Distinct();
    }
}

public sealed class CancellationChangeToken : IChangeToken
{
    private readonly CancellationToken _token;
    
    public CancellationChangeToken(CancellationToken token) => _token = token;
    
    public bool HasChanged => _token.IsCancellationRequested;
    public bool ActiveChangeCallbacks => true;
    
    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        return _token.Register(callback, state);
    }
}

public sealed class CompositeChangeToken : IChangeToken
{
    private readonly IChangeToken[] _tokens;
    
    public CompositeChangeToken(IEnumerable<IChangeToken> tokens)
    {
        _tokens = tokens.ToArray();
    }
    
    public bool HasChanged => _tokens.Any(t => t.HasChanged);
    public bool ActiveChangeCallbacks => _tokens.Any(t => t.ActiveChangeCallbacks);
    
    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        var disposables = _tokens.Select(t => t.RegisterChangeCallback(callback, state)).ToArray();
        return new CompositeDisposable(disposables);
    }
    
    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _disposables;
        public CompositeDisposable(IDisposable[] disposables) => _disposables = disposables;
        public void Dispose() => Array.ForEach(_disposables, d => d.Dispose());
    }
}

public static class ConfigurationBuilder
{
    public static IConfiguration Build(params IConfigurationProvider[] providers)
        => new ConfigurationRoot(providers);
    
    public static IConfiguration BuildFromJson(string path, bool optional = false, bool reloadOnChange = true)
        => Build(new JsonConfigurationProvider(path, optional, reloadOnChange));
    
    public static IConfiguration BuildFromMemory(Action<MemoryConfigurationProvider> configure)
    {
        var provider = new MemoryConfigurationProvider();
        configure(provider);
        return Build(provider);
    }
}