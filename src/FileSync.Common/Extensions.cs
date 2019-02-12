using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public static class Extensions
    {
        public static string ToHashString(this byte[] array)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < array.Length; ++i)
            {
                sb.Append(array[i].ToString("x2"));
            }

            return sb.ToString();
        }

        public static async Task<Tuple<bool, T>> WhenOrTimeout<T>(this Task<T> task, int milliseconds)
        {
            var timeoutTask = Task.Delay(milliseconds);
            var t = await Task.WhenAny(task, timeoutTask);
            if (t == timeoutTask)
                return Tuple.Create(false, default(T));

            return Tuple.Create(true, task.Result);
        }

        public static async Task<bool> WhenOrTimeout(this Task task, int milliseconds)
        {
            var timeoutTask = Task.Delay(milliseconds);
            var t = await Task.WhenAny(task, timeoutTask);
            return t != timeoutTask;
        }

        public static IPEndPoint ParseEndpoint(string ep)
        {
            var parts = ep.Split(':');
            var ip = IPAddress.Parse(parts[0]);
            var port = int.Parse(parts[1]);
            return new IPEndPoint(ip, port);
        }
    }
}