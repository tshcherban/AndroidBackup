using System;
using Android.App;
using Android.Runtime;

namespace FileSync.Android
{
    [Application]
    public sealed class FileSyncApp : Application
    {
        public FileSyncApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();
        }
    }
}