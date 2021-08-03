using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bing.Canal.Model;
using Bing.Canal.Server.Internal;
using Bing.Canal.Server.Models;
using CanalSharp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Type = System.Type;

namespace Bing.Canal.Server
{
    /// <summary>
    /// Canal客户端 后台服务基类
    /// </summary>
    internal abstract class CanalClientHostedServiceBase : IHostedService
    {
        /// <summary>
        /// 日志
        /// </summary>
        private readonly ILogger<CanalClientHostedServiceBase> _logger;

        /// <summary>
        /// Canal选项配置
        /// </summary>
        private readonly CanalOptions _options;

        /// <summary>
        /// 注册类型列表
        /// </summary>
        private readonly List<Type> _registerTypeList;

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
        /// 是否已释放
        /// </summary>
        protected bool IsDispose { get; set; } = false;

        /// <summary>
        /// 作用域
        /// </summary>
        protected IServiceScope Scope { get; }

        /// <summary>
        /// 取消令牌源
        /// </summary>
        protected CancellationTokenSource Cts { get; }

        /// <summary>
        /// 初始化一个<see cref="CanalClientHostedServiceBase"/>类型的实例
        /// </summary>
        /// <param name="logger">日志</param>
        /// <param name="options">选项配置</param>
        /// <param name="serviceScopeFactory">服务作用域工厂</param>
        /// <param name="register">消费者注册器</param>
        protected CanalClientHostedServiceBase(ILogger<CanalClientHostedServiceBase> logger
            , IOptions<CanalOptions> options
            , IServiceScopeFactory serviceScopeFactory
            , CanalConsumeRegister register)
        {
            _logger = logger;
            _registerTypeList = new List<Type>();
            if (register.SingletonConsumeList != null && register.SingletonConsumeList.Any())
                _registerTypeList.AddRange(register.SingletonConsumeList);
            if (register.ConsumeList != null && register.ConsumeList.Any())
                _registerTypeList.AddRange(register.ConsumeList);
            if (!_registerTypeList.Any())
                throw new ArgumentNullException(nameof(_registerTypeList));
            _options = options.Value;
            Scope = serviceScopeFactory.CreateScope();
            Cts = new CancellationTokenSource();
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="canalBody">Canal内容体</param>
        private async Task SendAsync(CanalBody canalBody)
        {
            try
            {
                foreach (var type in _registerTypeList)
                {
                    if (Scope.ServiceProvider.GetRequiredService(type) is INotificationHandler<CanalBody> service)
                        await service.HandleAsync(canalBody);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "canal produce error, end process!");
            }
        }

        /// <summary>
        /// 获取Canal内容
        /// </summary>
        /// <param name="entries">变更入口列表</param>
        /// <param name="batchId">批次标识</param>
        private CanalBody GetCanalBody(List<Entry> entries, long batchId)
        {
            var result = new List<DataChange>();
            foreach (var entry in entries)
            {
                // 忽略事务
                if (entry.EntryType == EntryType.Transactionbegin || entry.EntryType == EntryType.Transactionend)
                    continue;
                // 没有拿到数据库名称或数据表名称的直接排除
                if (string.IsNullOrEmpty(entry.Header.SchemaName) || string.IsNullOrEmpty(entry.Header.TableName))
                    continue;
                RowChange rowChange = null;
                try
                {
                    // 获取行变更
                    rowChange = RowChange.Parser.ParseFrom(entry.StoreValue);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"DbName:{entry.Header.SchemaName},TbName:{entry.Header.TableName} RowChange.Parser.ParseFrom error");
                    continue;
                }

                if (rowChange != null)
                {
                    // 变更类型 insert/update/delete 等等
                    var eventType = rowChange.EventType;
                    // 输出binlog信息 表名 数据库名 变更类型
                    _logger.LogInformation($"================> binlog[{entry.Header.LogfileName}:{entry.Header.LogfileOffset}] , name[{entry.Header.SchemaName},{entry.Header.TableName}] , eventType :{eventType}");
                    // 输出 insert/update/delete 变更类型列数据
                    foreach (var rowData in rowChange.RowDatas)
                    {
                        var dataChange = new DataChange
                        {
                            DbName = entry.Header.SchemaName,
                            TableName = entry.Header.TableName,
                            CanalDestination = _options.Destination
                        };
                        if (eventType == EventType.Delete)
                        {
                            dataChange.EventType = DataChange.EventConst.Delete;
                            dataChange.BeforeColumnList = rowData.BeforeColumns.ToList();
                        }
                        else if (eventType == EventType.Insert)
                        {
                            dataChange.EventType = DataChange.EventConst.Insert;
                            dataChange.AfterColumnList = rowData.AfterColumns.ToList();
                        }
                        else if (eventType == EventType.Update)
                        {
                            dataChange.EventType = DataChange.EventConst.Update;
                            dataChange.BeforeColumnList = rowData.BeforeColumns.ToList();
                            dataChange.AfterColumnList = rowData.AfterColumns.ToList();
                        }
                        else
                        {
                            continue;
                        }

                        var primaryKey = Helper.GetPrimaryKeyColumn(dataChange);
                        if (primaryKey == null || string.IsNullOrEmpty(primaryKey.Value))
                        {
                            // 没有主键
                            _logger.LogError($"DbName:{dataChange.DbName},TbName:{dataChange.TableName} without primaryKey :{JsonConvert.SerializeObject(dataChange)}");
                            continue;
                        }

                        result.Add(dataChange);
                    }
                }
            }

            return new CanalBody(result, batchId);
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
        /// 校验
        /// </summary>
        protected abstract bool Valid();

        /// <summary>
        /// 释放资源
        /// </summary>
        protected abstract Task DisposeAsync();

        /// <summary>
        /// 启动
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await ConnectAsync();
                _logger.LogInformation($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] canal client start...");
                ReceiveData();
                Execute();
                AckMessage();
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] canal client start error...");
            }
        }

        /// <summary>
        /// 停止
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task StopAsync(CancellationToken cancellationToken) => await DisposeAsync();

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
                    catch (IOException io)
                    {
                        _logger.LogError(io, "canal receive data error...");
                        await ReConnectAsync();
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, $"canal receive data error...");
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

            var messageList = await GetMessageAsync(_options.BatchSize);
            var batchId = messageList.Id;
            if (batchId < 1)
                return false;

            var body = new CanalBody(null, batchId);
            if (messageList.Entries.Count <= 0)
            {
                _canalQueue.Enqueue(new CanalQueueData { CanalBody = body });
                return false;
            }

            var canalBody = GetCanalBody(messageList.Entries, batchId);
            if (canalBody.Message == null || canalBody.Message.Count < 1)
            {
                _canalQueue.Enqueue(new CanalQueueData { CanalBody = body });
                return false;
            }
            stopwatch.Stop();

            var dotime = (int)stopwatch.Elapsed.TotalSeconds;
            var time = dotime > 1 ? Helper.ParseTime(dotime) : $"{stopwatch.Elapsed.TotalMilliseconds}ms";
            var canalQueueData = new CanalQueueData()
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
    }
}
