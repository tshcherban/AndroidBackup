using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace FileSync.Android.Activities
{
    [Activity(Label = "FolderViewActivity")]
    public class FolderViewActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            //SetContentView(Resource.Layout.DiscoverServer);

            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            //var btn = FindViewById<Button>(Resource.Id.startDiscoveryBtn1);
            //btn.Click += Btn_Click;
        }
    }
}