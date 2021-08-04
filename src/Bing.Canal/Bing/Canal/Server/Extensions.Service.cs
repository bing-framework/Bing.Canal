using System;
using System.Linq;
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
        /// <param name="isCluster">是否集群服务</param>
        internal static IServiceCollection RegisterCanalService(IServiceCollection services, Action<CanalConsumeRegister> setupAction, bool isCluster)
        {
            if (setupAction == null)
                throw new ArgumentNullException(nameof(setupAction));
            var register = new CanalConsumeRegister();
            setupAction(register);
            if (!register.ConsumeList.Any() && !register.SingletonConsumeList.Any())
                throw new ArgumentNullException(nameof(register.ConsumeList));
            services.AddOptions();
            services.TryAddSingleton<IConfigureOptions<CanalOptions>, ConfigureCanalOptions>();
            if(isCluster)
            {
                services.AddHostedService<ClusterCanalClientHostedService>();
            }
            else
            {
                services.AddHostedService<SimpleCanalClientHostedService>();

            }
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

        /// <summary>
        /// 注册Canal服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="setupAction">操作</param>
        public static IServiceCollection AddCanalService(this IServiceCollection services, Action<CanalConsumeRegister> setupAction) => RegisterCanalService(services, setupAction, false);

        /// <summary>
        /// 注册Canal集群服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="setupAction">操作</param>
        public static IServiceCollection AddClusterCanalService(this IServiceCollection services, Action<CanalConsumeRegister> setupAction)=> RegisterCanalService(services, setupAction, true);
    }
}
