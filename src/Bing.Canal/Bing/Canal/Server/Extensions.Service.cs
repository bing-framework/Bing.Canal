using System;
using System.Linq;
using Bing.Canal.Server.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bing.Canal.Server
{
    /// <summary>
    /// 服务扩展 - Canal服务
    /// </summary>
    public static partial class Extensions
    {
        /// <summary>
        /// 注册Canal服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="setupAction">操作</param>
        public static IServiceCollection AddCanalService(this IServiceCollection services, Action<CanalConsumeRegister> setupAction)
        {
            if (setupAction == null)
                throw new ArgumentNullException(nameof(setupAction));
            var register = new CanalConsumeRegister();
            setupAction(register);
            if (!register.ConsumeList.Any() && !register.SingletonConsumeList.Any())
                throw new ArgumentNullException(nameof(register.ConsumeList));
            services.AddOptions();
            services.TryAddSingleton<IConfigureOptions<CanalOptions>, ConfigureCanalOptions>();
            services.AddSingleton<ICanalProcessingServer, SimpleSyncCanalProcessingServer>();
            services.AddSingleton<ICanalProcessingServer, SimpleAsyncCanalProcessingServer>();
            services.AddSingleton<ICanalProcessingServer, ClusterSyncCanalProcessingServer>();
            services.AddSingleton<ICanalProcessingServer, ClusterAsyncCanalProcessingServer>();
            services.AddHostedService<DefaultBootstrapper>();
            if (register.ConsumeList.Any())
            {
                foreach (var type in register.ConsumeList)
                    services.TryAddTransient(type);
            }

            if (register.SingletonConsumeList.Any())
            {
                foreach (var type in register.SingletonConsumeList)
                    services.TryAddSingleton(type);
            }

            services.AddSingleton(register);
            return services;
        }
    }
}
