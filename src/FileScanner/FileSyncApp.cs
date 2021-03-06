﻿using System;
using System.Collections.Generic;
using System.Threading;
using Android.App;
using Android.Runtime;
using FileSync.Android.Model;
using FileSync.Common;

namespace FileSync.Android
{
    [Application]
    // ReSharper disable once ClassNeverInstantiated.Global
    public sealed class FileSyncApp : Application, IServiceLocator
    {
        internal static IServiceLocator Instance { get; private set; }

        private readonly CancellationTokenSource _appShutdownTokenSrc;

        public FileSyncApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
            if (Instance != null)
                throw new InvalidOperationException("App instance already assigned");

            Instance = this;

            var folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            var filePath = System.IO.Path.Combine(folderPath, "config.json");

            Config = new Config(filePath);

            _appShutdownTokenSrc = new CancellationTokenSource();
        }

        // must be overriden to call ctor(IntPtr, JniHandleOwnership)
        public override void OnCreate()
        {
            base.OnCreate();

            if (Instance == null)
                throw new InvalidOperationException("App instance was not assigned");

            Config.Load();
        }

        protected override void Dispose(bool disposing)
        {
            Stop();

            base.Dispose(disposing);
        }

        public IConfig Config { get; }
        
        public Dictionary<Guid, ISyncClient> ActiveClients { get; } = new Dictionary<Guid, ISyncClient>();

        public void Stop()
        {
            _appShutdownTokenSrc.Cancel(false);
        }
    }
}