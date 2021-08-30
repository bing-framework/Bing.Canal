using System;
using System.Collections.Generic;
using System.Linq;
using Bing.Canal.Model;
using CanalSharp.Protocol;

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
    }
}
