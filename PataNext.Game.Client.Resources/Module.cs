﻿using System;
using DefaultEcs;
using GameHost.Core.IO;
using GameHost.Core.Modules;
using GameHost.Injection;
using GameHost.IO;
using GameHost.Simulation.Application;
using GameHost.Threading;
using GameHost.Worlds;
using PataNext.Game.Abilities;
using PataNext.Game.BGM;
using PataNext.Game.Client.Resources;
using PataNext.Game.GameItems;

[assembly: RegisterAvailableModule("PataNext.Game.Client.Resources", "guerro", typeof(Module))]

namespace PataNext.Game.Client.Resources
{
	public class Module : GameHostModule, IModuleHasAbilityDescStorage
	{
		private AbilityDescStorage abilityDescStorage;

		public Module(Entity source, Context ctxParent, GameHostModuleDescription description) : base(source, ctxParent, description)
		{
			Console.WriteLine("Resources module loaded");

			var global = new ContextBindingStrategy(ctxParent, true).Resolve<GlobalWorld>();
			Storage.Subscribe((_, exteriorStorage) =>
			{
				var storage = exteriorStorage switch
				{
					{} => new StorageCollection {exteriorStorage, DllStorage},
					null => new StorageCollection {DllStorage}
				};

				abilityDescStorage = new AbilityDescStorage(storage.GetOrCreateDirectoryAsync("Abilities").Result);

				global.Context.BindExisting(new BgmContainerStorage(storage.GetOrCreateDirectoryAsync("Bgm").Result));
				foreach (ref readonly var listener in global.World.Get<IListener>())
				{
					if (listener is SimulationApplication simulationApplication)
					{
						simulationApplication.Schedule(onAppBind, (simulationApplication, storage), default);
					}
				}
			}, true);
		}

		private void onAppBind((SimulationApplication app, StorageCollection storage) args)
		{
			var (app, storage) = args;
			
			app.Data.Context.BindExisting(new BgmContainerStorage(storage.GetOrCreateDirectoryAsync("Bgm").Result));

			var gameItemsMgr = app.Data.Collection.GetOrCreate(wc => new GameItemsManager(wc));
			
			var itemStorage  = storage.GetOrCreateDirectoryAsync("Items").Result;
			gameItemsMgr.RegisterEquipmentsAsync(new(itemStorage.GetOrCreateDirectoryAsync("Equipments").Result));
		}

		AbilityDescStorage IModuleHasAbilityDescStorage.Value => abilityDescStorage;
	}
}