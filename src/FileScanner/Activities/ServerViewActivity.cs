using Android.App;
using Android.OS;
using Android.Views;

namespace FileSync.Android.Activities
{
    [Activity(Label = "ServerViewActivity")]
    public class ServerViewActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            //SetContentView(Resource.Layout.DiscoverServer);

            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            //var btn = FindViewById<Button>(Resource.Id.startDiscoveryBtn);
            //btn.Click += Btn_Click;
        }
    }
}