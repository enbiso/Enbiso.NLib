﻿using System;
using System.Linq;
using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Enbiso.NLib.Cqrs
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddCqrs(this IServiceCollection services)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => !a.IsDynamic);
            return services.AddCqrs(assembly);
        }

        public static IServiceCollection AddCqrs(this IServiceCollection services, params Assembly[] assemblies)
        {
            services.AddMediatR(assemblies);
            services.AddSingleton<ICommandBus, CommandBus>();                        
            return services;
        }        
    }
}