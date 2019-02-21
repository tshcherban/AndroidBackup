using System;
using System.Linq;
using System.Net;
using Android.App;
using Android.OS;
using Android.Widget;
using FileSync.Android.Helpers;

namespace FileSync.Android.Activities
{
    [Activity(Label = "Add server")]
    public sealed class AddServerActivity : Activity
    {
        private Button _addServerBtn;
        private TextView _addressTxtView;
        private TextView _portTxtView;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.AddServer);

            _addServerBtn = FindViewById<Button>(Resource.Id.confirmAddServerBtn);
            _addServerBtn.Click += AddServerBtnOnClick;

            _addressTxtView = FindViewById<EditText>(Resource.Id.serverAddressTxtView);
            _portTxtView = FindViewById<TextView>(Resource.Id.serverPortTxtView);

            if (Intent != null && Intent.HasExtra("server"))
            {
                var server = Intent.GetStringExtra("server");
                var ep = Common.Extensions.ParseEndpoint(server);
                _addressTxtView.Text = ep.Address.ToString();
                _portTxtView.Text = ep.Port.ToString();
            }
        }

        private async void AddServerBtnOnClick(object sender, EventArgs e)
        {
            if (!IPAddress.TryParse(_addressTxtView.Text, out var addr))
            {
                Toast.MakeText(this, "Address invalid", ToastLength.Short).Show();
                return;
            }

            if (!int.TryParse(_portTxtView.Text, out var port))
            {
                Toast.MakeText(this, "Port invalid", ToastLength.Short).Show();
                return;
            }

            var comm = new ServerCommunicator();
            var id = await comm.GetServerId(addr, port);
            if (!id.HasValue)
            {
                Toast.MakeText(this, "Unable to contact server", ToastLength.Short).Show();
                return;
            }

            if (FileSyncApp.Instance.Config.Servers.Any(x => x.Id == id.Value))
            {
                Toast.MakeText(this, "Server already added", ToastLength.Short).Show();
                return;
            }

            var res = await comm.RegisterClient(FileSyncApp.Instance.Config.ClientId, addr, port);
            if (!res)
            {
                Toast.MakeText(this, "Unable to register client", ToastLength.Short).Show();
                return;
            }

            FileSyncApp.Instance.Config.AddServer(id.Value, new IPEndPoint(addr, port).ToString());
            FileSyncApp.Instance.Config.Store();

            Toast.MakeText(this, "Server added successfully", ToastLength.Short).Show();

            Finish();
        }
    }
}