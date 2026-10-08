using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.DomainEvents;

public static class DomainEventRegistration
{
    public static IServiceCollection AddDomainEvents(
        this IServiceCollection services,
        params Assembly[] handlerAssemblies)
    {
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        foreach (Assembly assembly in handlerAssemblies)
        {
            RegisterHandlersFromAssembly(services, assembly);
        }

        return services;
    }

    private static void RegisterHandlersFromAssembly(IServiceCollection services, Assembly assembly)
    {
        Type handlerInterfaceType = typeof(IDomainEventHandler<>);

        IEnumerable<Type> handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterfaceType));

        foreach (Type handlerType in handlerTypes)
        {
            IEnumerable<Type> implementedHandlerInterfaces = handlerType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterfaceType);

            foreach (Type interfaceType in implementedHandlerInterfaces)
            {
                services.AddScoped(interfaceType, handlerType);

                Type eventType = interfaceType.GetGenericArguments()[0];
                Type wrapperType = typeof(DomainEventHandlerWrapper<>).MakeGenericType(eventType);

                // Register wrapper only once per event type
                if (!services.Any(sd => sd.ServiceType == wrapperType))
                {
                    services.AddScoped(wrapperType);
                }
            }
        }
    }
}
