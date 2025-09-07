using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.DependencyInjection;

namespace TCAMPlugin;

public class TCAMPlugin : AssettoServerModule<TCAMPluginConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        // Register the main service
        builder.RegisterType<TCAMService>().AsSelf().As<Microsoft.Extensions.Hosting.IHostedService>().SingleInstance();
        
        // Register the command module
        builder.RegisterType<TCAMCommandModule>().AsSelf().SingleInstance();
    }
    
    public override void ConfigureServices(IServiceCollection services)
    {
        // Additional service configuration if needed
        // The command module will be automatically discovered and registered by AssettoServer
    }
}