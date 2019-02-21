using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FileSync.Android.Helpers;
using FileSync.Android.Model;

namespace FileSync.Android.Activities
{
    [Activity(Label = "Discover nearby servers")]
    public class DiscoverServerActivity : Activity
    {
        private ListView _serverListView;
        private ServerCollectionDiscovery _servers;
        private Button _discoveryBtn;
        private Button _manualAddBtn;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.DiscoverServer);

            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            _discoveryBtn = FindViewById<Button>(Resource.Id.startDiscoverBtn);
            _discoveryBtn.Click += StartDiscoveryBtnClick;

            _manualAddBtn = FindViewById<Button>(Resource.Id.manualAddBtn);
            _manualAddBtn.Click += ManualAddBtnOnClick;

            _servers = new ServerCollectionDiscovery();

            _serverListView = FindViewById<ListView>(Resource.Id.discoveredServerList);
            _serverListView.Adapter = new ServerListAdapter(this, _servers, false);
            _serverListView.ItemClick += ServerListViewOnItemClick;
        }

        private void ManualAddBtnOnClick(object sender, EventArgs e)
        {
            StartActivity(typeof(AddServerActivity));
        }

        private void ServerListViewOnItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            var server = _servers.Items[e.Position];
            //StartActivityForResult(typeof(AddServerActivity), 121);
            var myIntent = new Intent(this, typeof(AddServerActivity));
            myIntent.PutExtra("server", server.Address);
            StartActivity(myIntent);
            return;
            //var myIntent = new Intent(this, typeof(int));
            myIntent.PutExtra("server", server.ToString());
            SetResult(Result.Ok, myIntent);
            Finish();
        }

        private async void StartDiscoveryBtnClick(object sender, EventArgs e)
        {
            _discoveryBtn.Enabled = false;

            try
            {
                var ctrl = new ServerDiscoveryController();
                var server = await ctrl.Discover();
                if (server == null)
                {
                    var toast = Toast.MakeText(this, "Failed to discover servers", ToastLength.Short);
                    toast.Show();
                }
                else
                {
                    _servers.AddServer(server);
                }
            }
            finally
            {
                _discoveryBtn.Enabled = true;
            }
        }
    }
}