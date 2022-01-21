using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bing.Canal.Server.Internal;
using Bing.Canal.Server.Models;
using CanalSharp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Type = System.Type;

namespace Bing.Canal.Server.Servers
{
    public abstract class CanalProcessingServerBase : ICanalProcessingServer
    {
        /// <summary>
        /// 日志
        /// </summary>
        private readonly ILogger<CanalProcessingServerBase> _logger;

        /// <summary>
        /// 注册类型列表
        /// </summary>
        private readonly List<Type> _registerTypeList;

        protected CanalProcessingServerBase(
            ILogger<CanalProcessingServerBase> logger,
            IOptions<CanalOptions> options,
            IServiceScopeFactory serviceScopeFactory,
            CanalConsumeRegister register)
        {
            _logger = logger;
            _registerTypeList = new List<Type>();
            Helper.InitRegisterTypes(_registerTypeList, register);
            Options = options.Value;
            Scope = serviceScopeFactory.CreateScope();
            Cts = new CancellationTokenSource();

        }

        /// <summary>
        /// 是否异步操作
        /// </summary>
        public abstract bool Async { get; }

        /// <summary>
        /// 模式
        /// </summary>
        /// <remarks>
        /// 单点：Standalone <br />
        /// 集群：Cluster
        /// </remarks>
        public abstract string Mode { get; }

        /// <summary>
        /// Canal选项配置
        /// </summary>
        protected CanalOptions Options { get; }

        /// <summary>
        /// 取消令牌源
        /// </summary>
        protected CancellationTokenSource Cts { get; }

        /// <summary>
        /// 作用域
        /// </summary>
        protected IServiceScope Scope { get; }

        /// <summary>
        /// 启动
        /// </summary>
        public virtual async Task StartAsync()
        {
            _logger.LogInformation($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] start canal client");
            await ConnectAsync();
            _logger.LogInformation($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] canal client start...");
            await Task.Factory.StartNew(ProcessAsync, Cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// 连接
        /// </summary>
        protected abstract Task ConnectAsync();

        /// <summary>
        /// 重新连接
        /// </summary>
        protected abstract Task ReConnectAsync();

        /// <summary>
        /// 获取数据
        /// </summary>
        /// <param name="fetchSize">获取数据大小</param>
        protected abstract Task<Message> GetMessageAsync(int fetchSize);

        /// <summary>
        /// 确认消息
        /// </summary>
        /// <param name="batchId">批次标识</param>
        protected abstract Task AckAsync(long batchId);

        /// <summary>
        /// 处理
        /// </summary>
        protected abstract Task ProcessAsync();

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="body">正文</param>
        protected virtual async Task SendAsync(CanalBody body)
        {
            try
            {
                foreach (var type in _registerTypeList)
                {
                    if (Scope.ServiceProvider.GetRequiredService(type) is INotificationHandler<CanalBody> service)
                        await service.HandleAsync(body);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "canal produce error, end process!");
                await DisposeAsync();
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public abstract ValueTask DisposeAsync();

    }
}
