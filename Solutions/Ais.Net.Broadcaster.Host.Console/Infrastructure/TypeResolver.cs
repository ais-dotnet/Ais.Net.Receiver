// <copyright file="TypeResolver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Spectre.Console.Cli;

namespace Ais.Net.Broadcaster.Host.Console.Infrastructure;

/// <summary>
/// Resolves Spectre.Console.Cli types from the built service provider, and disposes it with the app.
/// </summary>
public sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider provider;

    public TypeResolver(IServiceProvider provider)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public object? Resolve(Type? type) => type is null ? null : this.provider.GetService(type);

    public void Dispose()
    {
        if (this.provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
