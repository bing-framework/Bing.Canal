using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bing.Canal.Server
{
    /// <summary>
    /// 默认启动服务
    /// </summary>
    internal class DefaultBootstrapper : BackgroundService, IBootstrapper
    {
        /// <summary>
        /// 日志组件
        /// </summary>
        private readonly ILogger<DefaultBootstrapper> _logger;

        /// <summary>
        /// Canal 选项配置
        /// </summary>
        private readonly CanalOptions _options;

        /// <summary>
        /// 初始化一个<see cref="DefaultBootstrapper"/>类型的实例
        /// </summary>
        /// <param name="logger">日志组件</param>
        /// <param name="options">Canal选项配置</param>
        /// <param name="processors">处理服务器</param>
        public DefaultBootstrapper(ILogger<DefaultBootstrapper> logger,
            IOptions<CanalOptions> options,
            IEnumerable<ICanalProcessingServer> processors)
        {
            _logger = logger;
            _options = options.Value;
            Processors = processors;
        }

        /// <summary>
        /// 处理服务器列表
        /// </summary>
        private IEnumerable<ICanalProcessingServer> Processors { get; }

        /// <summary>
        /// 启动
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        public async Task BootstrapAsync(CancellationToken stoppingToken)
        {
            _logger.LogDebug("### Canal background task is starting.");
            stoppingToken.Register(() =>
            {
                _logger.LogDebug("### Canal background task is stopping.");
                try
                {
                    var item = Processors.First(x => x.Mode == _options.Mode && x.Async == _options.Async);
                    item.DisposeAsync();
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, $"Expected an OperationCanceledException, but found '{ex.Message}'.");
                }
            });
            await BootstrapCoreAsync();
            _logger.LogInformation("### Canal started!");
        }

        /// <summary>
        /// 核心启动服务
        /// </summary>
        protected virtual async Task BootstrapCoreAsync()
        {
            try
            {
                var item = Processors.First(x => x.Mode == _options.Mode && x.Async == _options.Async);
                await item.StartAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Starting the processors throw an exception.");
            }
        }

        /// <summary>
        /// 执行
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) => await BootstrapAsync(stoppingToken);
    }
}
