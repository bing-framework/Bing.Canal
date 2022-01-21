using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Bing.Canal.Server.Internal;
using Bing.Canal.Server.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bing.Canal.Server.Servers
{
    /// <summary>
    /// 异步Canal处理服务器基类
    /// </summary>
    public abstract class AsyncCanalProcessingServerBase : CanalProcessingServerBase
    {
        /// <summary>
        /// 日志
        /// </summary>
        private readonly ILogger<AsyncCanalProcessingServerBase> _logger;

        /// <summary>
        /// 队列
        /// </summary>
        private readonly ConcurrentQueue<long> _queue = new ConcurrentQueue<long>();

        /// <summary>
        /// Canal队列
        /// </summary>
        private readonly ConcurrentQueue<CanalQueueData> _canalQueue = new ConcurrentQueue<CanalQueueData>();

        /// <summary>
        /// 重置标识
        /// </summary>
        private volatile bool _resetFlag = false;

        /// <summary>
        /// 自动重置事件
        /// </summary>
        private readonly AutoResetEvent _condition = new AutoResetEvent(false);

        /// <summary>
        /// 初始化一个<see cref="AsyncCanalProcessingServerBase"/>类型的实例
        /// </summary>
        protected AsyncCanalProcessingServerBase(
            ILogger<AsyncCanalProcessingServerBase> logger,
            IOptions<CanalOptions> options,
            IServiceScopeFactory serviceScopeFactory,
            CanalConsumeRegister register)
            : base(logger, options, serviceScopeFactory, register)
        {
            _logger = logger;
        }

        /// <summary>
        /// 是否异步操作
        /// </summary>
        public override bool Async => true;

        /// <summary>
        /// 是否已释放
        /// </summary>
        protected bool IsDispose { get; set; } = false;

        /// <summary>
        /// 启动
        /// </summary>
        public override async Task StartAsync()
        {
            try
            {
                _logger.LogInformation($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] start canal client");
                await ConnectAsync();
                _logger.LogInformation($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] canal client start...");
                await ProcessAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] canal client start error...");
            }
        }

        /// <summary>
        /// 处理
        /// </summary>
        protected override Task ProcessAsync()
        {
            ReceiveData();
            Execute();
            AckMessage();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 接收数据
        /// </summary>
        private void ReceiveData()
        {
            Task.Factory.StartNew(async () =>
            {
                _logger.LogInformation("canal receive worker thread start...");
                while (!IsDispose)
                {
                    try
                    {
                        if (!await PreparedAndEnqueueAsync())
                            continue;
                        // 队列里面先储备5批次，超过5批次的话，就开始消费一个批次再储备
                        if (_canalQueue.Count >= 5)
                        {
                            _logger.LogInformation("canal receive worker waitOne...");
                            _resetFlag = true;
                            _condition.WaitOne();
                            _resetFlag = false;
                            _logger.LogInformation("canal receive worker continue...");
                        }

                        await Task.Delay(300);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "canal receive data error...");
                        await Task.Delay(1000);
                        await ReConnectAsync();
                    }
                }
            }, Cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// 准备数据并入队
        /// </summary>
        private async Task<bool> PreparedAndEnqueueAsync()
        {
            if (!Valid())
                return false;

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var messageList = await GetMessageAsync(Options.BatchSize);
            var batchId = messageList.Id;
            if (batchId < 1)
                return false;

            var body = new CanalBody(null, batchId);
            if (messageList.Entries.Count <= 0)
            {
                _canalQueue.Enqueue(new CanalQueueData { CanalBody = body });
                return false;
            }

            var canalBody = Helper.BuildCanalBody(batchId, messageList.Entries, Options.Destination, _logger);
            if (canalBody.Message == null || canalBody.Message.Count < 1)
            {
                _canalQueue.Enqueue(new CanalQueueData { CanalBody = body });
                return false;
            }
            stopwatch.Stop();

            var dotime = (int)stopwatch.Elapsed.TotalSeconds;
            var time = dotime > 1 ? Helper.ParseTime(dotime) : $"{stopwatch.Elapsed.TotalMilliseconds}ms";
            var canalQueueData = new CanalQueueData
            {
                Time = time,
                CanalBody = canalBody
            };
            _canalQueue.Enqueue(canalQueueData);
            return true;
        }

        /// <summary>
        /// 执行
        /// </summary>
        private void Execute()
        {
            Task.Factory.StartNew(async () =>
            {
                _logger.LogInformation("handler worker thread start...");
                while (!IsDispose)
                {
                    try
                    {
                        if (!Valid())
                            continue;
                        if (!_canalQueue.TryDequeue(out var canalData))
                            continue;
                        if (canalData == null)
                            continue;
                        if (_resetFlag && _canalQueue.Count <= 1)
                        {
                            _condition.Set();
                            _resetFlag = false;
                        }

                        if (canalData.CanalBody.Message == null)
                        {
                            if (canalData.CanalBody.BatchId > 0)
                                _queue.Enqueue(canalData.CanalBody.BatchId);
                            continue;
                        }

                        _logger.LogInformation(
                            $"【batchId:{canalData.CanalBody.BatchId}】batchCount:{canalData.CanalBody.Message.Count},batchGetTime:{canalData.Time}");

                        await SendAsync(canalData.CanalBody);
                        _queue.Enqueue(canalData.CanalBody.BatchId);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "handle worker error...");
                        return;
                    }
                }
            }, Cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// 确认消息
        /// </summary>
        private void AckMessage()
        {
            Task.Factory.StartNew(async () =>
            {
                _logger.LogInformation($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] canal-server ack worker thread start...");
                while (!IsDispose)
                {
                    try
                    {
                        if (!Valid())
                            continue;
                        if (!_queue.TryDequeue(out var batchId))
                            continue;
                        if (batchId > 0)
                            await AckAsync(batchId);// 如果程序突然关闭 canal service会关闭。这里就不会提交，下次重启应用消息会重复推送!
                    }
                    catch (Exception e)
                    {
                        // ignore
                        _logger.LogError(e, "canal-server ack worker error...");
                    }
                }
            }, Cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// 校验
        /// </summary>
        protected abstract bool Valid();
    }
}
