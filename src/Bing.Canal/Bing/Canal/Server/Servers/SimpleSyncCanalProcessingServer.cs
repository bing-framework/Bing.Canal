using System;
using System.Threading.Tasks;
using CanalSharp.Connections;
using CanalSharp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bing.Canal.Server.Servers
{
    /// <summary>
    /// 单机 同步 Canal 处理服务器
    /// </summary>
    public class SimpleSyncCanalProcessingServer : SyncCanalProcessingServerBase
    {
        /// <summary>
        /// 日志
        /// </summary>
        private readonly ILogger<SimpleSyncCanalProcessingServer> _logger;

        /// <summary>
        /// Canal 连接
        /// </summary>
        private SimpleCanalConnection _canalConnection;

        /// <summary>
        /// 日志工厂
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// 初始化一个<see cref="SimpleAsyncCanalProcessingServer"/>类型的实例
        /// </summary>
        public SimpleSyncCanalProcessingServer(
            ILogger<SimpleSyncCanalProcessingServer> logger,
            ILoggerFactory loggerFactory,
            IOptions<CanalOptions> options,
            IServiceScopeFactory serviceScopeFactory,
            CanalConsumeRegister register)
            : base(logger, options, serviceScopeFactory, register)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// 是否异步操作
        /// </summary>
        public override bool Async => false;

        /// <summary>
        /// 模式
        /// </summary>
        /// <remarks>
        /// 单点：Standalone <br />
        /// 集群：Cluster
        /// </remarks>
        public override string Mode => "Standalone";

        /// <summary>
        /// 连接
        /// </summary>
        protected override async Task ConnectAsync()
        {
            _canalConnection = new SimpleCanalConnection(Options.Standalone, _loggerFactory.CreateLogger<SimpleCanalConnection>());
            await _canalConnection.ConnectAsync();
            await _canalConnection.SubscribeAsync(Options.Filter);
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
                _logger.LogInformation("canal worker reconnect...");
                await _canalConnection.DisposeAsync();
                await _canalConnection.ConnectAsync();
                await _canalConnection.SubscribeAsync(Options.Filter);
                await _canalConnection.RollbackAsync(0);
            }
            catch (Exception e)
            {
                //ignore
                _logger.LogError(e, "canal worker reconnect error...");
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
        /// 释放资源
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            if(!Flag)
                return;
            Flag = false;
            Cts.Cancel();
            try
            {
                await _canalConnection.UnSubscribeAsync(Options.Filter);
                await _canalConnection.DisConnectAsync();
                _logger.LogInformation($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] canal worker stop success...");
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] canal worker stop error...");
            }
            finally
            {
                _canalConnection = null;
                Scope.Dispose();
            }
        }
    }
}
