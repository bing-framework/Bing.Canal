using System.Collections.Generic;
using Bing.Canal.Model;
using Bing.Canal.Server.Internal;
using CanalSharp.Protocol;

namespace Bing.Canal.Server
{
    /// <summary>
    /// <see cref="DataChange"/> 扩展
    /// </summary>
    public static class DataChangeExtensions
    {
        /// <summary>
        /// 获取有效数据列集合
        /// </summary>
        /// <param name="dataChange">数据变更实体</param>
        public static List<Column> GetColumns( this DataChange dataChange) => Helper.GetColumns(dataChange);

        /// <summary>
        /// 获取主键列
        /// </summary>
        /// <param name="dataChange">数据变更实体</param>
        public static Column GetPrimaryKeyColumn(this DataChange dataChange) => Helper.GetPrimaryKeyColumn(dataChange);
    }
}
