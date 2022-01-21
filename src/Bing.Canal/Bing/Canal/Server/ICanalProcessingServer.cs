using System;
using System.Threading.Tasks;

namespace Bing.Canal.Server
{
    /// <summary>
    /// Canal处理服务器
    /// </summary>
    public interface ICanalProcessingServer : IAsyncDisposable
    {
        /// <summary>
        /// 是否异步操作
        /// </summary>
        bool Async { get; }

        /// <summary>
        /// 模式
        /// </summary>
        /// <remarks>
        /// 单点：Standalone <br />
        /// 集群：Cluster
        /// </remarks>
        string Mode { get; }

        /// <summary>
        /// 启动
        /// </summary>
        Task StartAsync();
    }
}
