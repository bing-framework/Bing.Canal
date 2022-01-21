using System;
using System.Collections.Generic;
using System.Linq;
using Bing.Canal.Model;
using Bing.Canal.Server.Models;
using CanalSharp.Protocol;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bing.Canal.Server.Internal
{
    /// <summary>
    /// 帮助类
    /// </summary>
    internal static class Helper
    {
        /// <summary>
        /// 时间戳转换为C#格式时间
        /// </summary>
        /// <param name="timeStamp">Unix时间戳格式</param>
        /// <returns>C#时间格式</returns>
        public static DateTime ToDateTime(long timeStamp)
        {
            var dtBegin = TimeZoneInfo.ConvertTime(new DateTime(1970, 1, 1), TimeZoneInfo.Local);
            var time = timeStamp * 10000;
            var toNow = new TimeSpan(time);
            return dtBegin.Add(toNow);
        }

        /// <summary>
        /// 解析时间。将秒数转换为几天几小时
        /// </summary>
        /// <param name="t">秒数</param>
        /// <param name="type">0:转换后带秒, 1: 转换后不带秒</param>
        /// <returns>几天几小时几分几秒</returns>
        public static string ParseTime(int t, int type = 0)
        {
            string result;
            int hour, minute, second;
            if (t >= 86400)//天
            {
                var day = Convert.ToInt16(t / 86400);
                hour = Convert.ToInt16((t % 86400) / 3600);
                minute = Convert.ToInt16((t % 86400 % 3600) / 60);
                second = Convert.ToInt16(t % 86400 % 3600 % 60);
                result = type == 0 ? $"{day}D{hour}H{minute}M{second}S" : $"{day}D{hour}H{minute}M";
            }
            else if (t >= 3600)//时
            {
                hour = Convert.ToInt16(t / 3600);
                minute = Convert.ToInt16((t % 3600) / 60);
                second = Convert.ToInt16(t % 3600 % 60);
                result = type == 0 ? $"{hour}H{minute}M{second}S" : $"{hour}H{minute}M";
            }
            else if (t >= 60)//分
            {
                minute = Convert.ToInt16(t / 60);
                second = Convert.ToInt16(t % 60);
                result = $"{minute}M{second}S";
            }
            else
            {
                second = Convert.ToInt16(t);
                result = $"{second}S";
            }
            return result;
        }

        /// <summary>
        /// 获取有效数据列集合
        /// </summary>
        /// <param name="dataChange">数据变更实体</param>
        public static List<Column> GetColumns(DataChange dataChange)
        {
            var columns = dataChange.AfterColumnList == null || !dataChange.AfterColumnList.Any()
                ? dataChange.BeforeColumnList
                : dataChange.AfterColumnList;
            return columns;
        }

        /// <summary>
        /// 获取主键列
        /// </summary>
        /// <param name="dataChange">数据变更实体</param>
        public static Column GetPrimaryKeyColumn(DataChange dataChange)
        {
            var columns = dataChange.AfterColumnList == null || !dataChange.AfterColumnList.Any()
                ? dataChange.BeforeColumnList
                : dataChange.AfterColumnList;
            var primaryKey = columns.FirstOrDefault(x => x.IsKey);
            return primaryKey;
        }

        /// <summary>
        /// 构建Canal内容
        /// </summary>
        /// <param name="batchId">批次标识</param>
        /// <param name="entries">变更入口列表</param>
        /// <param name="destination">Canal目的地</param>
        /// <param name="logger">日志组件</param>
        public static CanalBody BuildCanalBody(long batchId, List<Entry> entries,string destination, ILogger logger = null)
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
                    logger?.LogError(e, $"DbName:{entry.Header.SchemaName},TbName:{entry.Header.TableName} RowChange.Parser.ParseFrom error");
                    continue;
                }

                if (rowChange != null)
                {
                    // 变更类型 insert/update/delete 等等
                    var eventType = rowChange.EventType;
                    // 输出binlog信息 表名 数据库名 变更类型
                    logger?.LogInformation($"================> binlog[{entry.Header.LogfileName}:{entry.Header.LogfileOffset}] , name[{entry.Header.SchemaName},{entry.Header.TableName}] , eventType :{eventType}");
                    // 输出 insert/update/delete 变更类型列数据
                    foreach (var rowData in rowChange.RowDatas)
                    {
                        var dataChange = new DataChange
                        {
                            DbName = entry.Header.SchemaName,
                            TableName = entry.Header.TableName,
                            CanalDestination = destination,
                            ExecuteTime = Helper.ToDateTime(entry.Header.ExecuteTime)
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
                            logger?.LogError($"DbName:{dataChange.DbName},TbName:{dataChange.TableName} without primaryKey :{JsonConvert.SerializeObject(dataChange)}");
                            continue;
                        }

                        result.Add(dataChange);
                    }
                }
            }

            return new CanalBody(result, batchId);
        }

        /// <summary>
        /// 初始化注册类型列表
        /// </summary>
        /// <param name="registerTypeList">注册类型列表</param>
        /// <param name="register">Canal消费者注册器</param>
        public static void InitRegisterTypes(List<System.Type> registerTypeList, CanalConsumeRegister register)
        {
            if (register.SingletonConsumeList != null && register.SingletonConsumeList.Any())
                registerTypeList.AddRange(register.SingletonConsumeList);
            if (register.ConsumeList != null && register.ConsumeList.Any())
                registerTypeList.AddRange(register.ConsumeList);
            if (!registerTypeList.Any())
                throw new ArgumentNullException(nameof(registerTypeList));
        }
    }
}
