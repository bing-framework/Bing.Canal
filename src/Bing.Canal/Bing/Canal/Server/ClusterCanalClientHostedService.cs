using System;
using System.Threading.Tasks;
using CanalSharp.Connections;
using CanalSharp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Exception = System.Exception;

namespace Bing.Canal.Server
{
    /// <summary>
    /// Canal客户端 后台服务
    /// </summary>
    internal class ClusterCanalClientHostedService : CanalClientHostedServiceBase
    {
        /// <summary>
        /// 日志
        /// </summary>
        private readonly ILogger<ClusterCanalClientHostedService> _logger;

        /// <summary>
        /// 日志工厂
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Canal选项配置
        /// </summary>
        private readonly CanalOptions _options;

        /// <summary>
        /// Canal 连接
        /// </summary>
        private ClusterCanalConnection _canalConnection;

        /// <summary>
        /// 初始化一个<see cref="ClusterCanalClientHostedService"/>类型的实例
        /// </summary>
        /// <param name="logger">日志</param>
        /// <param name="loggerFactory">日志工厂</param>
        /// <param name="options">选项配置</param>
        /// <param name="serviceScopeFactory">服务作用域工厂</param>
        /// <param name="register">消费者注册器</param>
        public ClusterCanalClientHostedService(ILogger<ClusterCanalClientHostedService> logger
            , ILoggerFactory loggerFactory
            , IOptions<CanalOptions> options
            , IServiceScopeFactory serviceScopeFactory
            , CanalConsumeRegister register)
            : base(logger, options, serviceScopeFactory, register)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _options = options.Value;
        }

        /// <summary>
        /// 连接
        /// </summary>
        protected override async Task ConnectAsync()
        {
            _canalConnection = new ClusterCanalConnection(_options.Cluster, _loggerFactory);
            await _canalConnection.ConnectAsync();
            await _canalConnection.SubscribeAsync(_options.Filter);
            // 回滚寻找上次中断的位置
            await _canalConnection.RollbackAsync(0);
        }

        /// <summary>
        /// 重新连接
        /// </summary>
        protected override async Task ReConnectAsync()
        {
            try
            {
                _logger.LogInformation("canal receive worker reconnect...");
                await _canalConnection.ReConnectAsync();
            }
            catch (Exception e)
            {
                //ignore
                _logger.LogError(e, "canal receive worker reconnect error...");
            }
        }

        /// <summary>
        /// 获取数据
        /// </summary>
        /// <param name="fetchSize">获取数据大小</param>
        protected override async Task<Message> GetMessageAsync(int fetchSize) => await _canalConnection.GetWithoutAckAsync(fetchSize);

        /// <summary>
        /// 确认消息
        /// </summary>
        /// <param name="batchId">批次标识</param>
        protected override async Task AckAsync(long batchId) => await _canalConnection.AckAsync(batchId);

        /// <summary>
        /// 校验
        /// </summary>
        protected override bool Valid() => _canalConnection != null;

        /// <summary>
        /// 释放资源
        /// </summary>
        protected override async Task DisposeAsync()
        {
            if (IsDispose)
                return;
            IsDispose = true;
            Cts.Cancel();
            try
            {
                await _canalConnection.UnSubscribeAsync(_options.Filter);
                await _canalConnection.DisConnectAsync();
                _logger.LogInformation($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] canal client stop success...");
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] canal client stop error...");
            }

            _canalConnection = null;
            Scope.Dispose();
        }
    }
}
