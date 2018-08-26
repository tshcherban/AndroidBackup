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
using Java.Util.Zip;

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

            Task.Run(ClientDiscoverDelay);
        }

        private async Task ClientDiscoverDelay()
        {
            await Task.Delay(2000);

            await CLientDiscover();
            //TestAlgos();
        }

        private async Task CLientDiscover()
        {
            var cts = new TaskCompletionSource<IPEndPoint>();
            _discoverTask = cts.Task;

            try
            {
                AppendLog("Discovering...");

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

                AppendLog("Discover done");
            }
            catch (Exception e)
            {
                AppendLog($"Error while discovering\r\n{e}");
            }
        }

        private static string CalcSpeed(long length, Stopwatch sw)
        {
            return $"{length / 1024d / 1024d / sw.Elapsed.TotalSeconds:F2} mb/s" ;
        }

        private async void Btn3OnClick(object sender, EventArgs e)
        {
            return;
            var res = await TryGetpermissionsAsync();
            if (!res)
            {
                return;
            }

            try
            {
                var fs = new FileInfo("/storage/emulated/0/stest/ghh.mp4");
                //var fs = new FileInfo("/storage/emulated/0/shcherban.7z");

                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(_discoverTask.Result.Address, _discoverTask.Result.Port + 1);

                    using (var networkStream = client.GetStream())
                    {
                        var sw = Stopwatch.StartNew();

                        var initReadHash = await NetworkHelperSequential.HashFileAsync(fs);

                        sw.Stop();

                        AppendLog($"Local read hash speed: {CalcSpeed(fs.Length, sw)}");

                        var fileLengthBytes = BitConverter.GetBytes(fs.Length);
                        var fileLengthBytesCount = sizeof(long);

                        await networkStream.WriteAsync(fileLengthBytes, 0, fileLengthBytesCount);

                        sw.Restart();

                        var sendHash = await NetworkHelperSequential.WriteFromFileAndHashAsync(networkStream, fs.FullName, (int)fs.Length);

                        sw.Stop();

                        AppendLog($"Send and hash speed: {CalcSpeed(fs.Length, sw)}");

                        await networkStream.WriteAsync(sendHash, 0, sizeof(ulong));

                        fileLengthBytesCount = sizeof(long);
                        fileLengthBytes = new byte[fileLengthBytesCount];

                        var read = await networkStream.ReadAsync(fileLengthBytes, 0, fileLengthBytesCount);
                        if (read != fileLengthBytesCount)
                        {
                            throw new InvalidOperationException("Invalid data length read");
                        }

                        var fileLength = BitConverter.ToInt64(fileLengthBytes, 0);

                        if (File.Exists("/storage/emulated/0/stest/ghh1.mp4"))
                        {
                            File.Delete("/storage/emulated/0/stest/ghh1.mp4");
                        }

                        sw.Restart();

                        var receiveHash = await NetworkHelperSequential.ReadToFileAndHashAsync(networkStream, "/storage/emulated/0/stest/ghh1.mp4", (int)fileLength);

                        sw.Stop();

                        AppendLog($"Receive and hash speed: {CalcSpeed(fileLength, sw)}");

                        var hashBuffer = new byte[sizeof(ulong)];

                        await networkStream.ReadAsync(hashBuffer, 0, sizeof(ulong));

                        sw.Restart();

                        var receivedFileHash = await NetworkHelperSequential.HashFileAsync("/storage/emulated/0/stest/ghh1.mp4", (int)fileLength);

                        sw.Stop();

                        AppendLog($"Local read hash speed: {CalcSpeed(fileLength, sw)}");

                        var initReadHashStr = initReadHash.ToHashString();
                        var sendHashStr = sendHash.ToHashString();
                        var receiveHashStr = receiveHash.ToHashString();
                        var receivedHashStr = hashBuffer.ToHashString();
                        var receivedFileHashStr = receivedFileHash.ToHashString();

                        AppendLog($"initReadHashStr     {initReadHashStr}\r\n" +
                                  $"sendHashStr         {sendHashStr}\r\n" +
                                  $"receiveHashStr      {receiveHashStr}\r\n" +
                                  $"receivedHashStr     {receivedHashStr}\r\n" +
                                  $"receivedFileHashStr {receivedFileHashStr}");
                    }
                }
            }
            catch (Exception exception)
            {
                AppendLog($"Error:\r\n{exception}");
            }

            

            return;

            /*var sww = Stopwatch.StartNew();
            const FileOptions fileFlagNoBuffering = (FileOptions)0x20000000;
            const FileOptions fileOptions = fileFlagNoBuffering | FileOptions.SequentialScan;

            const int chunkSize = BufferSizeMib * 1024 * 1024;

            var readBufferSize = chunkSize;
            readBufferSize += ((readBufferSize + 1023) & ~1023) - readBufferSize;

            byte[] hash1, hash2;

            using (var ff = new FileStream(fs.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, readBufferSize, fileOptions))
            {
                hash1 = await XxHash64Callback.ComputeHash(ff, 133479, (int) fs.Length, (buffer, length) => Task.CompletedTask);
            }
            sww.Stop();

            var speed1 = fs.Length / 1024d / 1024d / sww.Elapsed.TotalSeconds;
            
            AppendLog($"{speed1:F2} mb/s ({133219/1024d:F2} kb)");

            sww = Stopwatch.StartNew();

            using (var ff = new FileStream(fs.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, readBufferSize, fileOptions))
            {
                //hash2 = XxHash64Unsafe.ComputeHash(ff, BufferSizeMib * 1024 * 1024);
            }

            sww.Stop();

            var speed2 = fs.Length / 1024d / 1024d / sww.Elapsed.TotalSeconds;

            AppendLog($"{speed2:F2} mb/s ({BufferSizeMib*1024d:F2} kb)");

            //await TestHash(xxHash64Algo.Create(), fs.FullName, (int) fs.Length, BufferSizeMib*1024*1024);
            //var h1 = await TestHash(xxHash64Algo.Create(), fs.FullName, (int) fs.Length, 124959);
            //var h2 = await TestHash(xxHash64Algo.Create(), fs.FullName, (int) fs.Length, BufferSizeMib*1024*1024);

            //AppendLog($"hashes equals {hash1 == hash2}");*/
        }

        private async Task TestAlgos()
        {
            var fname = "";
            var fs = new FileInfo(fname);
        }

        private const int BufferSizeMib = 1;

        private async Task<string> TestHash(HashAlgorithm alg, string fname, int length, int chunkSize)
        {
            var sw = Stopwatch.StartNew();

            using (alg)
            {
                using (var fileStream = File.OpenRead(fname))
                {
                    var readSize = Math.Min(length, chunkSize);
                    var buffer = new byte[readSize];
                    var bytesLeft = length;

                    do
                    {
                        var bytesRead = await fileStream.ReadAsync(buffer, 0, readSize);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        alg.TransformBlock(buffer, 0, bytesRead, null, 0);

                        bytesLeft -= bytesRead;
                        readSize = (int) Math.Min(bytesLeft, chunkSize);
                    } while (bytesLeft > 0);

                    fileStream.Close();

                    alg.TransformFinalBlock(buffer, 0, 0);

                    sw.Stop();

                    var speed = length / 1024d / 1024d / sw.Elapsed.TotalSeconds;

                    var log = $"{speed:F2} mb/s ({chunkSize/1024d:F2} kb)";

                    Toast.MakeText(this, log, ToastLength.Short).Show();
                    
                    AppendLog(log);

                    //System.Diagnostics.Debug.WriteLine($"***** {alg.GetType().Name} {sw.Elapsed.TotalMilliseconds:F2} ms (buffer - {chunkSize/1024m:F2} kbytes, speed - {:F2} mb/s)");

                    return alg.Hash.ToHashString();
                }
            }
        }

        private void TestHash(HashAlgorithm alg, FileInfo fname, int bsize = 4096)
        {
            var sw = Stopwatch.StartNew();

            using (alg)
            {
                using (var file = new FileStream(fname.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                    bsize))
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

            System.Diagnostics.Debug.WriteLine($"***** {alg.GetType().Name} {sw.Elapsed.TotalMilliseconds:F2} ms (buffer - {bsize} bytes, speed - {(fname.Length / 1024m / 1024m) / (decimal) sw.Elapsed.TotalSeconds:F2} mb/s)");
        }

        private void AppendLog(string msg)
        {
            Task.Run(() =>
            {
                RunOnUiThread(() => { _text.Text = $"{msg}\r\n{_text.Text}"; });
            });            
        }

        private void Btn2OnClick(object sender, EventArgs e)
        {
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

            Task.Run(() => { RequestPermissions(_permissions, RequestId); });

            var requestPermissionsTask = _requestPermissionsTaskCompletionSource.Task;

            var timedOut = await Task.WhenAny(requestPermissionsTask, Task.Delay(PermissionsTimeout)) == requestPermissionsTask;

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
            const string dir = @"/storage/emulated/0/stest/";
            //const string dir = @"/storage/emulated/0/music/";
            //const string dir = @"/storage/emulated/0/DCIM/";

            var dbDir = Path.Combine(dir, @".sync/");

            var client = SyncClientFactory.GetTwoWay(endpoint.Address.ToString(), endpoint.Port, dir, dbDir);

            client.Log += AppendLog;

            await client.Sync();
        }
    }
}