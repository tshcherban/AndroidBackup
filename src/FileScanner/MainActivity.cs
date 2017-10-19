using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Android.App;
using Android.Widget;
using Android.OS;
using Android.Text.Method;
using Android.Views;
using FileSync.Common;
using ServiceWire.TcpIp;
using Environment = System.Environment;

namespace FileScanner
{
    [Activity(Label = "FileScanner", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private readonly Lazy<IFileService> _serviceContainer;

        private TextView _text;
        private IDisposable _client;

        public MainActivity()
        {
            _serviceContainer = new Lazy<IFileService>(InitService);
        }

        private IFileService InitService()
        {
            var serverUrl = "10.0.2.2";
            var client = new TcpClient<IFileService>(new IPEndPoint(IPAddress.Parse(serverUrl), 9211));
            _client = client;
            return client.Proxy;
        }

        protected override void OnDestroy()
        {
            _client?.Dispose();

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
            
            return;
            using (var alg = SHA1.Create())
            {
                var files = Directory.GetFiles("/storage/sdcard/dcim", "*", SearchOption.AllDirectories)
                    .Select(fpath =>
                    {
                        var sha1 = alg.ComputeHash(File.OpenRead(fpath));
                        return $"{fpath}\r\n{string.Concat(sha1.Select(s => s.ToString("x")))}";
                    }).ToList();
                var res = string.Join("\r\n", files);
                _text.Text += "\r\n";
                _text.Text += res;
            }
        }
        private async void BtnOnClick(object sender, EventArgs e)
        {
            /*var paths = string.Join("\r\n", Enum.GetValues(typeof(Environment.SpecialFolder))
                .Cast<Environment.SpecialFolder>().Select(en =>
                {
                    try
                    {
                        return $"{en.ToString()}\r\n{Environment.GetFolderPath(en)}\r\n";
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                })
                .Where(i => i != null).ToList());
            _text.Text = paths;

            return;*/

            try
            {
                var filePath = "/storage/sdcard/dcim/file1";
                var fileBytes = File.ReadAllBytes(filePath);

                
                
                var s = new Stopwatch();
                s.Restart();
                _serviceContainer.Value.SendFile(fileBytes);
                s.Stop();
                var str = $"{fileBytes.Length} bytes in {s.Elapsed.TotalMilliseconds} ms ({fileBytes.Length/ s.Elapsed.TotalSeconds :F2} bytes/s)";
                _text.Text = str;
            }
            catch (Exception exception)
            {
                _text.Text = exception.ToString();
            }
        }
    }
}