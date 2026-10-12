// <copyright file="TypeRegistrar.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console.Cli;

namespace Ais.Net.Broadcaster.Host.Console.Infrastructure;

/// <summary>
/// Lets Spectre.Console.Cli resolve commands from the Microsoft.Extensions.DependencyInjection
/// container the host builder populated.
/// </summary>
public sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection services;

    public TypeRegistrar(IServiceCollection services)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public ITypeResolver Build() => new TypeResolver(this.services.BuildServiceProvider());

    public void Register(Type service, Type implementation) => this.services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) => this.services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) => this.services.AddSingleton(service, _ => factory());
}
