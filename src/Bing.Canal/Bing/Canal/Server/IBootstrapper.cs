using System.Threading;
using System.Threading.Tasks;

namespace Bing.Canal.Server
{
    /// <summary>
    /// 启动服务
    /// </summary>
    public interface IBootstrapper
    {
        /// <summary>
        /// 启动
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        Task BootstrapAsync(CancellationToken stoppingToken);
    }
}
