using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
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

        protected override void OnResume()
        {
            base.OnResume();

            
        }

        private async void Btn_Click(object sender, EventArgs e)
        {
            var ctrl = new ServerDiscoveryController();
            var server = await ctrl.ClientDiscover();
            Intent myIntent = new Intent(this, typeof(MainActivity));
            if (server!= null)
            {
                myIntent.PutExtra("server", server.ToString());
            }
            else
            {
                myIntent.PutExtra("server", string.Empty);
            }

            SetResult(Result.Ok, myIntent);
            Finish();
        }
    }
}