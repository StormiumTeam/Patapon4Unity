﻿using System;
using System.Collections.Generic;
using System.Linq;
using GameHost.Core;
using GameHost.Core.Ecs;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.Utility.EntityQuery;
using GameHost.Worlds.Components;
using JetBrains.Annotations;
using PataNext.Module.Simulation.Components.GamePlay.Structures.Bastion;
using PataNext.Module.Simulation.Components.Roles;
using StormiumTeam.GameBase;
using StormiumTeam.GameBase.GamePlay.Health;
using StormiumTeam.GameBase.Roles.Components;
using StormiumTeam.GameBase.Roles.Descriptions;
using StormiumTeam.GameBase.SystemBase;
using StormiumTeam.GameBase.Transform.Components;

namespace PataNext.Module.Simulation.Game.GamePlay.Structures.Bastion
{
	[UpdateAfter(typeof(BastionDynamicRecycleDeadEntitiesSystem))]
	public class BastionSpawnAllIfAllDeadSystem : GameAppSystem, IPreUpdateSimulationPass
	{
		private IManagedWorldTime worldTime;

		public BastionSpawnAllIfAllDeadSystem([NotNull] WorldCollection collection) : base(collection)
		{
			DependencyResolver.Add(() => ref worldTime);
		}

		private EntityQuery bastionQuery;
		private EntityQuery aliveQuery;

		protected override void OnDependenciesResolved(IEnumerable<object> dependencies)
		{
			base.OnDependenciesResolved(dependencies);

			bastionQuery = CreateEntityQuery(new[]
			{
				typeof(BastionDescription),
				typeof(SquadEntityContainer),
				typeof(BastionSpawnAllIfAllDead),
				typeof(BastionProvideDynamicEntity)
			}, none: new[]
			{
				typeof(LivableIsDead)
			});

			aliveQuery = CreateEntityQuery(new[]
			{
				typeof(LivableHealth)
			}, none: new[]
			{
				typeof(LivableIsDead)
			});
		}

		public void OnBeforeSimulationUpdate()
		{
			if (World.TryGet(out BastionDynamicRecycleDeadEntitiesSystem recycler) && worldTime.Total != recycler.CurrentSpan)
			{
				var pass = World.DefaultSystemCollection.Passes.First(p => p is IPreUpdateSimulationPass);
				foreach (var type in pass.RegisteredObjects)
					Console.WriteLine(type.GetType());
				
				throw new InvalidOperationException("should have been updated after recycler");
			}

			var dt = worldTime.Delta;

			aliveQuery.CheckForNewArchetypes();

			var entitiesAccessor  = GetBufferAccessor<SquadEntityContainer>();
			var providerAccessor  = GetAccessor<BastionProvideDynamicEntity>();
			var conditionAccessor = GetAccessor<BastionSpawnAllIfAllDead>();
			foreach (var entity in bastionQuery)
			{
				var buffer     = entitiesAccessor[entity];
				var aliveCount = 0;
				foreach (var unit in buffer)
					if (aliveQuery.MatchAgainst(unit.Value.Handle))
						aliveCount++;

				if (aliveCount > 0)
					continue;

				ref var condition = ref conditionAccessor[entity];
				if (condition.Delay > condition.Accumulated)
				{
					condition.Accumulated += dt;
					continue;
				}

				// Respawn Time!
				condition.Accumulated = TimeSpan.Zero;

				var spawnCount                      = providerAccessor[entity].SpawnCount;
				var assureRemoveBastionUnitWhenDead = providerAccessor[entity].AssureRemoveBastionUnitWhenDead;

				var board = BastionProvideDynamicEntity.GetComponentBoard(Focus(Safe(entity)), out var metadata);
				foreach (var dent in board.DentMap[metadata.Id])
				{
					var provider = dent.Get<BaseProvider>();
					if (spawnCount > 0)
					{
						for (; spawnCount > 0; spawnCount--)
						{
							linkEntityToBastion(buffer, entity, provider.SpawnEntity(dent), assureRemoveBastionUnitWhenDead);
						}

						break;
					}

					linkEntityToBastion(buffer, entity, provider.SpawnEntity(dent), assureRemoveBastionUnitWhenDead);
				}
			}
		}

		private void linkEntityToBastion(ComponentBuffer<SquadEntityContainer> entitiesList, GameEntityHandle bastion, GameEntityHandle unit, bool assureRemoveBastionUnitWhenDead)
		{
			GameWorld.Link(unit, bastion, true);
			GameWorld.UpdateOwnedComponent(unit, new Relative<BastionDescription>(Safe(bastion)));

			entitiesList.Add(new() { Value = Safe(unit) });

			if (HasComponent<Relative<TeamDescription>>(bastion))
				GameWorld.AssignComponent(unit, GameWorld.GetComponentReference<Relative<TeamDescription>>(bastion));

			if (HasComponent<UnitTargetDescription>(bastion))
				GameWorld.UpdateOwnedComponent(unit, new Relative<UnitTargetDescription>(Safe(bastion)));

			if (assureRemoveBastionUnitWhenDead && !HasComponent<RemoveBastionUnitWhenDead>(unit))
				AddComponent(unit, new RemoveBastionUnitWhenDead { Delay = TimeSpan.FromSeconds(6) });

			GetComponentData<Position>(unit) = GetComponentData<Position>(bastion);
		}
	}
}