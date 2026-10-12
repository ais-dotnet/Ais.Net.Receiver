// <copyright file="TypeRegistrar.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Ais.Net.Receiver.Host.Console.Infrastructure;

public sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection builder;

    public TypeRegistrar(IServiceCollection builder)
    {
        this.builder = builder;
    }

    public ITypeResolver Build() => new TypeResolver(this.builder.BuildServiceProvider());

    public void Register(Type service, Type implementation) => this.builder.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) => this.builder.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) => this.builder.AddSingleton(service, _ => factory());
}