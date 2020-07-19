﻿using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using GameHost.Applications;
using GameHost.Core;
using GameHost.Core.Applications;
using GameHost.Core.Ecs;
using GameHost.Core.IO;
using GameHost.Core.Threading;
using GameHost.HostSerialization;
using GameHost.UI.Noesis;
using OpenToolkit.Windowing.Common;
using PataNext.Module.Presentation.RhythmEngine;
using PataponGameHost.Applications.MainThread;
using PataponGameHost.Storage;

namespace PataNext.Module.Presentation.Controls
{
	[RestrictToApplication(typeof(GameRenderThreadingHost))]
	[UpdateAfter(typeof(NoesisInitializationSystem))]
	public class MenuEntryControlSystem : AppSystem
	{
		private MainThreadClient client;
		private Entity           entityView;

		private MenuEntryControl.BgmEntry[] files = new MenuEntryControl.BgmEntry[0];
		private XamlFileLoader              xamlFileLoader;
		private IScheduler                  scheduler;
		
		private INativeWindow window;
		private CurrentRhythmEngineSystem currentRhythmEngineSystem;

		public MenuEntryControlSystem(WorldCollection collection) : base(collection)
		{
			DependencyResolver.Add(() => ref window);
			DependencyResolver.Add(() => ref scheduler);
			DependencyResolver.Add(() => ref client);
			DependencyResolver.Add(() => ref xamlFileLoader);
			DependencyResolver.Add(() => ref currentRhythmEngineSystem);
		}

		private void onBgmFileChange()
		{
			var clientWorld = client.Listener.WorldCollection;
			var arrayOfBgm  = clientWorld.Mgr.Get<BgmFile>().ToArray();

			void OnSetFiles()
			{
				files = (from file in arrayOfBgm
				         orderby file.Name
				         select new MenuEntryControl.BgmEntry {Content = file.Description}).ToArray();
			}

			scheduler.Add(OnSetFiles);
		}

		protected override void OnDependenciesResolved(IEnumerable<object> dependencies)
		{
			using (client.SynchronizeThread())
			{
				AddDisposable(client.Listener.WorldCollection.Mgr.SubscribeComponentChanged((in Entity _0, in BgmFile _1, in BgmFile _2) => onBgmFileChange()));
				AddDisposable(client.Listener.WorldCollection.Mgr.SubscribeComponentAdded((in   Entity _0, in BgmFile _1) => onBgmFileChange()));

				client.Listener.WorldCollection.Mgr.CreateEntity().Set<RefreshBgmList>();
			}

			xamlFileLoader.SetTarget("Interfaces", "MenuEntryControl");
			xamlFileLoader.Xaml.Subscribe(OnXamlFound, true);
		}

		private void OnXamlFound(string previous, string next)
		{
			if (next == null)
				return;

			void addXaml()
			{
				var view = new NoesisOpenTkRenderer(window);
				view.ParseXaml(next);

				entityView = entityView.IsAlive ? entityView : World.Mgr.CreateEntity();
				if (entityView.Has<NoesisOpenTkRenderer>())
				{
					var oldRenderer = entityView.Get<NoesisOpenTkRenderer>();
					oldRenderer.Dispose();
				}

				entityView.Set(view);
				entityView.Set((MenuEntryControl) view.View.Content);
			}

			scheduler.Add(addXaml);
		}

		public override bool CanUpdate()
		{
			return base.CanUpdate() && entityView.IsAlive;
		}

		protected override void OnUpdate()
		{
			base.OnUpdate();

			xamlFileLoader.Update();
			switch (entityView.IsEnabled())
			{
				case true when currentRhythmEngineSystem.Information.Entity != default:
					Console.WriteLine("DISABLE ENTITY");
					entityView.Disable();
					break;
				case false when currentRhythmEngineSystem.Information.Entity == default:
					Console.WriteLine("ENABLE ENTITY");
					entityView.Enable();
					break;
			}

			foreach (ref var control in World.Mgr.Get<MenuEntryControl>())
			{
				var view = control.DataContext as MenuEntryControl.ViewModel;
				if (view == null)
					continue;
				view.BgmEntries = files;
			}
		}
	}
}