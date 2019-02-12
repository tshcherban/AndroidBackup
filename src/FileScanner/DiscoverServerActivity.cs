using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using FileSync.Android.Helpers;

namespace FileSync.Android
{
    [Activity(Label = "DiscoverServerActivity")]
    public class DiscoverServerActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.DiscoverServer);

            var btn = FindViewById<Button>(Resource.Id.startDiscoveryBtn1);
            btn.Click += Btn_Click;
        }

        private async void Btn_Click(object sender, EventArgs e)
        {
            var ctrl = new ServerDiscoveryController();
            var server = await ctrl.ClientDiscover();
            var myIntent = new Intent(this, typeof(MainActivity));
            myIntent.PutExtra("server", server?.ToString());

            SetResult(Result.Ok, myIntent);
            Finish();
        }
    }
}