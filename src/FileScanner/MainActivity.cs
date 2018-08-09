using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Widget;
using Android.OS;
using Android.Text.Method;
using Android.Util;
using Android.Views;
using FileSync.Common;

namespace FileSync.Android
{
    [Activity(Label = "FileScanner", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private const int PermissionsTimeout = 10000;

        private readonly string[] _permissions =
        {
            Manifest.Permission.ReadExternalStorage,
            Manifest.Permission.WriteExternalStorage,
        };

        private TextView _text;

        private Task<IPEndPoint> _discoverTask;

        public MainActivity()
        {
        }


        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            _text = FindViewById<TextView>(Resource.Id.editText1);
            _text.TextAlignment = TextAlignment.Gravity;
            _text.MovementMethod = new ScrollingMovementMethod();

            var btn = FindViewById<Button>(Resource.Id.button1);
            btn.Click += BtnOnClick;

            var btn2 = FindViewById<Button>(Resource.Id.button2);
            btn2.Click += Btn2OnClick;

            var btn3 = FindViewById<Button>(Resource.Id.button3);
            btn3.Click += Btn3OnClick;

            Task.Run(async () => { await Action(); });
        }

        private async Task Action()
        {
            await Task.Delay(2000);

            await CLientDiscover();
            //TestAlgos();
        }

        private async Task CLientDiscover()
        {
            var cts = new TaskCompletionSource<IPEndPoint>();
            _discoverTask = cts.Task;

            using (var client = new UdpClient())
            {
                var requestData = Encoding.ASCII.GetBytes("SomeRequestData");

                client.EnableBroadcast = true;
                await client.SendAsync(requestData, requestData.Length, new IPEndPoint(IPAddress.Broadcast, 8888));

                var serverResponseData = await client.ReceiveAsync();
                var serverResponse = Encoding.ASCII.GetString(serverResponseData.Buffer);

                var port = int.Parse(serverResponse.Replace("port:", null));

                var ss = $"Discovered on {serverResponseData.RemoteEndPoint.Address}:{port}";

                AppendLog(ss);

                cts.SetResult(new IPEndPoint(serverResponseData.RemoteEndPoint.Address, port));

                client.Close();
            }
        }


        private async void Btn3OnClick(object sender, EventArgs e)
        {
            var res = await TryGetpermissionsAsync();
            if (!res)
            {
                return;
            }

            TestAlgos();
        }

        private async void TestAlgos()
        {
            const int minSize = 112000000;

            var ff = Directory.GetFiles("/storage/emulated/0/");
            var fs1 = ff.Select(x => new FileInfo(x))
                .OrderByDescending(x => x.Length)
                .Where(x => x.Length > minSize)
                .ToList();

            var fs = fs1[0];

            if (fs == null)
            {
                return;
            }

            var fname = "/storage/emulated/0/ghh.mp4";

            using (var client = new TcpClient())
            {
                await client.ConnectAsync(_discoverTask.Result.Address, _discoverTask.Result.Port + 1);

                using (var networkStream = client.GetStream())
                {
                    await NetworkHelper.WriteFromFile(networkStream, fname);
                }
            }

            //TestHash(new MurmurHash3UnsafeProvider(), fs);
        }

        private void TestHash(HashAlgorithm alg, FileInfo fname, int bsize = 4096)
        {
            var sw = Stopwatch.StartNew();

            using (alg)
            {
                using (var file = new FileStream(fname.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bsize))
                {
                    var hash = alg.ComputeHash(file);

                    var hh = hash.ToHashString();

                    if (hh == null)
                    {
                        throw new InvalidOperationException();
                    }
                }
            }

            sw.Stop();

            System.Diagnostics.Debug.WriteLine($"************** {alg.GetType().Name} {sw.Elapsed.TotalMilliseconds:F2} ms (buffer - {bsize} bytes, speed - {(fname.Length / 1024m / 1024m) / (decimal) sw.Elapsed.TotalSeconds:F2} mb/s)");
        }

        private void CommunicatorOnEv(string s)
        {
            AppendLog(s);
        }

        private void AppendLog(string msg)
        {
            RunOnUiThread(() => { _text.Text = $"{msg}\r\n{_text.Text}"; });
        }

        private void Btn2OnClick(object sender, EventArgs e)
        {
            return;
            var i = new Intent(this, typeof(DemoService));
            i.PutExtra("data", "stop");
            var cn = StopService(i);
        }


        private const int RequestId = 1;

        private TaskCompletionSource<bool> _requestPermissionsTaskCompletionSource;

        private async Task<bool> TryGetpermissionsAsync()
        {
            if ((int) Build.VERSION.SdkInt < 23)
            {
                return false;
            }

            if (CheckSelfPermission(_permissions[0]) == (int) Permission.Granted)
            {
                return true;
            }

            _requestPermissionsTaskCompletionSource = new TaskCompletionSource<bool>();

            var requestTask = Task.Run(() => { RequestPermissions(_permissions, RequestId); });

            var requestPermissionsTask = _requestPermissionsTaskCompletionSource.Task;

            var timedOut = await Task.WhenAny(requestPermissionsTask, Task.Delay(PermissionsTimeout)) ==
                           requestPermissionsTask;

            await requestTask;

            return timedOut;
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode == RequestId)
            {
                _requestPermissionsTaskCompletionSource.SetResult(grantResults[0] == Permission.Granted);
            }
        }

        private async void BtnOnClick(object sender, EventArgs e)
        {
            var res = await TryGetpermissionsAsync();
            if (!res)
            {
                Toast.MakeText(this, "Storage permission denied.", ToastLength.Short).Show();

                return;
            }

            if (_discoverTask == null)
            {
                Toast.MakeText(this, "Wait for service discovery", ToastLength.Short).Show();

                return;
            }

            AppendLog("Storage permission allowed. Waiting fow service discovery");

            var endpoint = await _discoverTask;

            AppendLog($"Discovered on {endpoint}. Starting sync...");

            //const string dir = @"/mnt/sdcard";
            //const string dir = @"/storage/emulated/0/stest/";
            //const string dir = @"/storage/emulated/0/music/";
            const string dir = @"/storage/emulated/0/DCIM/";

            var dbDir = Path.Combine(dir, @".sync/");

            var client = SyncClientFactory.GetTwoWay(endpoint.Address.ToString(), endpoint.Port, dir, dbDir);

            client.Log += AppendLog;

            await client.Sync();

            return;
            var i = new Intent(this, typeof(DemoService));
            i.PutExtra("data", DateTime.Now.ToString("G"));
            var cn = StartService(i);
        }

        private static uint To32BitFnv1aHash(byte[] bytes)
        {
            var hash = FnvConstants.FnvOffset32;

            foreach (var chunk in bytes)
            {
                hash ^= chunk;
                hash *= FnvConstants.FnvPrime32;
            }

            return hash;
        }

        public static class FnvConstants
        {
            public static readonly uint FnvPrime32 = 16777619;
            public static readonly ulong FnvPrime64 = 1099511628211;
            public static readonly uint FnvOffset32 = 2166136261;
            public static readonly ulong FnvOffset64 = 14695981039346656037;
        }
    }

    static class Communicator
    {
        public static event Action<string> Ev;

        public static void Log(string msg)
        {
            Ev?.Invoke(msg);
        }
    }

    [Service]
    public class DemoService : IntentService
    {
        public DemoService() : base("DemoService")
        {
        }

        protected override void OnHandleIntent(Intent intent)
        {
            var data = intent.GetStringExtra("data");

            Log.Debug(GetType().Name, $"Service: {data}");

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        Communicator.Log($"work {DateTime.Now:G}");
                        await Task.Delay(1000);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }).Wait();
        }

        public override ComponentName StartService(Intent service)
        {
            Log.Debug(GetType().Name, $"StartService");

            return base.StartService(service);
        }

        public override bool StopService(Intent name)
        {
            Log.Debug(GetType().Name, $"StopService");

            return base.StopService(name);
        }
    }
}