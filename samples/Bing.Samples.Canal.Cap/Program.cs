using System;
using System.IO;
using System.Threading.Tasks;
using Bing.Canal.Server;
using Bing.Canal.Server.Models;
using DotNetCore.CAP;
using FreeSql;
using FreeSql.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetEscapades.Extensions.Logging.RollingFile;

namespace Bing.Samples.Canal.Cap
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json"))
                .AddEnvironmentVariables()
                .Build();
            var builder = new HostBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddCanalService(x =>
                    {
                        x.RegisterSingleton<CapHandler>();
                    });
                    services.AddLogging(factory =>
                    {
                        factory
                            .AddFilter("Microsoft", LogLevel.Debug)
                            .AddFilter("System", LogLevel.Information)
                            .AddFilter("Bing.Canal", LogLevel.Warning)
                            .AddFile(options =>
                            {
                                options.FileName = "diagnostics-";
                                options.LogDirectory = "LogFiles";
                                options.FileSizeLimit = 5 * 1024 * 1024;
                                options.FilesPerPeriodicityLimit = 10000;
                                options.Extension = "txt";
                                options.Periodicity = PeriodicityOptions.Hourly;
                                options.RetainedFileCountLimit = null;
                            })
                            .AddConsole();
                    });
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddCap(x =>
                    {
                        x.Version = "canal_test";
                        x.UseDashboard();
                        x.UseInMemoryStorage();
                        x.UseRabbitMQ(o =>
                        {
                            o.HostName = "10.186.132.84";
                            o.UserName = "admin";
                            o.Password = "bing2019.00";
                        });
                    });
                    services.AddTransient<CanalEventHandler>();

                    //var fsql=new FreeSql.FreeSqlBuilder()
                    //    .UseConnectionString(DataType.Sqlite, "Data Source=./cap-event.db")
                    //    .UseAutoSyncStructure(true)
                    //    .Build();
                    //services.AddSingleton<IFreeSql>(fsql);
                });
            builder.Build().Run();
        }
    }

    public class CapHandler : INotificationHandler<CanalBody>, IDisposable
    {
        private readonly ICapPublisher _publisher;

        public CapHandler(ICapPublisher publisher)
        {
            _publisher = publisher;
        }

        /// <summary>
        /// 处理
        /// </summary>
        /// <param name="notification">对象</param>
        public async Task HandleAsync(CanalBody notification)
        {
            foreach (var dataChange in notification.Message)
            {
                if(dataChange.TableName.Contains("cap"))
                    continue;
                await _publisher.PublishAsync("canal.common", new CanalEvent
                {
                    BatchId = notification.BatchId,
                    DbName = dataChange.DbName,
                    TableName = dataChange.TableName,
                    EventType = dataChange.EventType,
                    Id = dataChange.GetPrimaryKeyColumn()?.Value,
                });
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Console.WriteLine($"{nameof(CapHandler)} 已释放!");
        }
    }

    public class CanalEventHandler : ICapSubscribe
    {
        private readonly ILogger<CanalEventHandler> _logger;

        public CanalEventHandler(ILogger<CanalEventHandler> logger)
        {
            _logger = logger;
        }

        [CapSubscribe(("canal.common"))]
        public Task WriteLogAsync(CanalEvent @event)
        {
            _logger.LogInformation($"[{@event.BatchId}] DbName:{@event.DbName},TbName:{@event.TableName},EventType:{@event.EventType},Id:{@event.Id}");
            return Task.CompletedTask;
        }
    }

    public class CanalEvent
    {
        public long BatchId { get; set; }

        public string DbName { get; set; }

        public string TableName { get; set; }

        public string EventType { get; set; }

        public string Id { get; set; }
    }
}
