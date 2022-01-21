using System;
using System.Threading.Tasks;
using Bing.Canal.Server.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bing.Canal.Server.Servers
{
    /// <summary>
    /// 同步Canal处理服务器基类
    /// </summary>
    public abstract class SyncCanalProcessingServerBase : CanalProcessingServerBase
    {
        /// <summary>
        /// 日志
        /// </summary>
        private readonly ILogger<SyncCanalProcessingServerBase> _logger;

        /// <summary>
        /// 标识
        /// </summary>
        protected bool Flag;

        /// <summary>
        /// 初始化一个<see cref="SyncCanalProcessingServerBase"/>类型的实例
        /// </summary>
        protected SyncCanalProcessingServerBase(
            ILogger<SyncCanalProcessingServerBase> logger,
            IOptions<CanalOptions> options,
            IServiceScopeFactory serviceScopeFactory,
            CanalConsumeRegister register)
            : base(logger, options, serviceScopeFactory, register)
        {
            _logger = logger;
            Flag = true;
        }

        /// <summary>
        /// 是否异步操作
        /// </summary>
        public override bool Async => false;

        /// <summary>
        /// 处理
        /// </summary>
        protected override async Task ProcessAsync()
        {
            while (!Cts.Token.IsCancellationRequested)
            {
                try
                {
                    while (Flag)
                    {
                        var message = await GetMessageAsync(Options.BatchSize);
                        if (message.Id != -1 && message.Entries.Count != 0)
                        {
                            _logger.LogInformation($"【batchId:{message.Id}】batchCount:{message.Entries.Count}");
                            await SendAsync(Helper.BuildCanalBody(message.Id, message.Entries, Options.Destination, _logger));
                        }
                        await AckAsync(message.Id);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "canal worker error...");
                }
                finally
                {
                    await ReConnectAsync();
                }
            }
        }

    }
}
