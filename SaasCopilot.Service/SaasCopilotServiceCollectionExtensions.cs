using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace SaasCopilot.Service;

public static class SaasCopilotServiceCollectionExtensions
{
	public static IServiceCollection AddSaasCopilotService(this IServiceCollection services, Action<SaasCopilotServiceOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		var options = new SaasCopilotServiceOptions();
		configure?.Invoke(options);

		services.AddMcpServer(serverOptions =>
		{
			serverOptions.ServerInfo = new()
			{
				Name = options.ServerName,
				Version = options.ServerVersion ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
			};
		})
			.WithHttpTransport(transportOptions =>
			{
				transportOptions.Stateless = options.HttpTransportStateless;
			})
			.WithTools<DbContextTools>();

		return services;
	}
}