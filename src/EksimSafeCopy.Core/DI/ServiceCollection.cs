namespace EksimSafeCopy.Core.DI;

public interface IServiceCollection
{
    IServiceCollection AddSingleton<TService, TImplementation>() where TService : class where TImplementation : class, TService;
    IServiceCollection AddSingleton<TService>(TService instance) where TService : class;
    IServiceCollection AddSingleton<TService>(Func<IServiceProvider, TService> factory) where TService : class;
    
    IServiceCollection AddTransient<TService, TImplementation>() where TService : class where TImplementation : class, TService;
    IServiceCollection AddTransient<TService>(Func<IServiceProvider, TService> factory) where TService : class;
    
    IServiceCollection AddScoped<TService, TImplementation>() where TService : class where TImplementation : class, TService;
    IServiceCollection AddScoped<TService>(Func<IServiceProvider, TService> factory) where TService : class;
    
    IServiceCollection TryAddSingleton<TService, TImplementation>() where TService : class where TImplementation : class, TService;
    IServiceCollection TryAddTransient<TService, TImplementation>() where TService : class where TImplementation : class, TService;
    IServiceCollection TryAddScoped<TService, TImplementation>() where TService : class where TImplementation : class, TService;
    
    IReadOnlyList<ServiceDescriptor> Services { get; }
}

public interface IServiceProvider
{
    object? GetService(Type serviceType);
    T? GetService<T>() where T : class;
    T GetRequiredService<T>() where T : class;
    object GetRequiredService(Type serviceType);
    IEnumerable<T> GetServices<T>() where T : class;
}

public sealed class ServiceDescriptor
{
    public Type ServiceType { get; }
    public Type? ImplementationType { get; }
    public object? ImplementationInstance { get; }
    public Func<IServiceProvider, object>? ImplementationFactory { get; }
    public ServiceLifetime Lifetime { get; }
    
    public ServiceDescriptor(Type serviceType, Type implementationType, ServiceLifetime lifetime)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
    }
    
    public ServiceDescriptor(Type serviceType, object implementationInstance)
    {
        ServiceType = serviceType;
        ImplementationInstance = implementationInstance;
        Lifetime = ServiceLifetime.Singleton;
    }
    
    public ServiceDescriptor(Type serviceType, Func<IServiceProvider, object> factory, ServiceLifetime lifetime)
    {
        ServiceType = serviceType;
        ImplementationFactory = factory;
        Lifetime = lifetime;
    }
}

public enum ServiceLifetime
{
    Singleton,
    Scoped,
    Transient
}

public sealed class ServiceCollection : IServiceCollection
{
    private readonly List<ServiceDescriptor> _descriptors = new();
    
    public IReadOnlyList<ServiceDescriptor> Services => _descriptors.AsReadOnly();
    
    public IServiceCollection AddSingleton<TService, TImplementation>() where TService : class where TImplementation : class, TService
    {
        _descriptors.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Singleton));
        return this;
    }
    
    public IServiceCollection AddSingleton<TService>(TService instance) where TService : class
    {
        _descriptors.Add(new ServiceDescriptor(typeof(TService), instance));
        return this;
    }
    
    public IServiceCollection AddSingleton<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        _descriptors.Add(new ServiceDescriptor(typeof(TService), p => factory(p), ServiceLifetime.Singleton));
        return this;
    }
    
    public IServiceCollection AddTransient<TService, TImplementation>() where TService : class where TImplementation : class, TService
    {
        _descriptors.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Transient));
        return this;
    }
    
    public IServiceCollection AddTransient<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        _descriptors.Add(new ServiceDescriptor(typeof(TService), p => factory(p), ServiceLifetime.Transient));
        return this;
    }
    
    public IServiceCollection AddScoped<TService, TImplementation>() where TService : class where TImplementation : class, TService
    {
        _descriptors.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Scoped));
        return this;
    }
    
    public IServiceCollection AddScoped<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        _descriptors.Add(new ServiceDescriptor(typeof(TService), p => factory(p), ServiceLifetime.Scoped));
        return this;
    }
    
    public IServiceCollection TryAddSingleton<TService, TImplementation>() where TService : class where TImplementation : class, TService
    {
        if (!_descriptors.Any(d => d.ServiceType == typeof(TService)))
            _descriptors.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Singleton));
        return this;
    }
    
    public IServiceCollection TryAddTransient<TService, TImplementation>() where TService : class where TImplementation : class, TService
    {
        if (!_descriptors.Any(d => d.ServiceType == typeof(TService)))
            _descriptors.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Transient));
        return this;
    }
    
    public IServiceCollection TryAddScoped<TService, TImplementation>() where TService : class where TImplementation : class, TService
    {
        if (!_descriptors.Any(d => d.ServiceType == typeof(TService)))
            _descriptors.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Scoped));
        return this;
    }
}

public sealed class ServiceProvider : IServiceProvider, IDisposable
{
    private readonly Dictionary<ServiceDescriptor, object> _singletons = new();
    private readonly Dictionary<ServiceDescriptor, object> _scoped = new();
    private readonly IReadOnlyList<ServiceDescriptor> _descriptors;
    private bool _disposed;
    
    public ServiceProvider(IServiceCollection services)
    {
        _descriptors = services.Services;
    }
    
    public object? GetService(Type serviceType)
    {
        var descriptor = _descriptors.LastOrDefault(d => d.ServiceType == serviceType);
        if (descriptor == null) return null;
        
        return descriptor.Lifetime switch
        {
            ServiceLifetime.Singleton => GetOrCreateSingleton(descriptor),
            ServiceLifetime.Scoped => GetOrCreateScoped(descriptor),
            ServiceLifetime.Transient => CreateInstance(descriptor),
            _ => throw new InvalidOperationException($"Unknown lifetime: {descriptor.Lifetime}")
        };
    }
    
    public T? GetService<T>() where T : class => GetService(typeof(T)) as T;
    
    public T GetRequiredService<T>() where T : class => GetService<T>() ?? throw new InvalidOperationException($"Required service {typeof(T).Name} not registered");
    
    public object GetRequiredService(Type serviceType) => GetService(serviceType) ?? throw new InvalidOperationException($"Required service {serviceType.Name} not registered");
    
    public IEnumerable<T> GetServices<T>() where T : class
    {
        return _descriptors
            .Where(d => d.ServiceType == typeof(T))
            .Select(d => GetService<T>())
            .Where(s => s != null)!;
    }
    
    private object GetOrCreateSingleton(ServiceDescriptor descriptor)
    {
        lock (_singletons)
        {
            if (_singletons.TryGetValue(descriptor, out var instance))
                return instance;
            
            instance = CreateInstance(descriptor);
            _singletons[descriptor] = instance;
            return instance;
        }
    }
    
    private object GetOrCreateScoped(ServiceDescriptor descriptor)
    {
        lock (_scoped)
        {
            if (_scoped.TryGetValue(descriptor, out var instance))
                return instance;
            
            instance = CreateInstance(descriptor);
            _scoped[descriptor] = instance;
            return instance;
        }
    }
    
    private object CreateInstance(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance != null)
            return descriptor.ImplementationInstance;
        
        if (descriptor.ImplementationFactory != null)
            return descriptor.ImplementationFactory(this);
        
        if (descriptor.ImplementationType != null)
            return ActivatorUtilities.CreateInstance(this, descriptor.ImplementationType);
        
        throw new InvalidOperationException("No implementation for service: " + descriptor.ServiceType.Name);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        foreach (var instance in _singletons.Values.OfType<IDisposable>())
            instance.Dispose();
        
        foreach (var instance in _scoped.Values.OfType<IDisposable>())
            instance.Dispose();
        
        _singletons.Clear();
        _scoped.Clear();
        _disposed = true;
    }
}

public static class ActivatorUtilities
{
    public static object CreateInstance(IServiceProvider provider, Type type)
    {
        var constructors = type.GetConstructors();
        if (constructors.Length == 0)
            throw new InvalidOperationException($"Type {type.Name} has no public constructors");
        
        var constructor = constructors.OrderByDescending(c => c.GetParameters().Length).First();
        var parameters = constructor.GetParameters()
            .Select(p => provider.GetRequiredService(p.ParameterType))
            .ToArray();
        
        return constructor.Invoke(parameters);
    }
    
    public static T CreateInstance<T>(IServiceProvider provider) => (T)CreateInstance(provider, typeof(T));
}

public static class ServiceCollectionExtensions
{
    public static IServiceProvider BuildServiceProvider(this IServiceCollection services)
        => new ServiceProvider(services);
}