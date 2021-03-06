﻿using System;
using System.Collections;
using System.Collections.Generic;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Core.Ecs;
using GameHost.Core.IO;
using GameHost.Simulation.TabEcs.Interfaces;
using PataNext.Game.GameItems;
using PataNext.Module.Simulation.Components.Units;
using PataNext.Module.Simulation.Resources;
using StormiumTeam.GameBase;
using StormiumTeam.GameBase.SystemBase;

namespace PataNext.Module.Simulation.Components
{
	public struct PlayerInventoryTarget : IComponentData
	{
		public Entity Value;

		public PlayerInventoryTarget(Entity value)
		{
			Value = value;
		}
	}

	public struct ItemInventory
	{
		public Entity AssetEntity;
	}

	public struct ItemStackInventory
	{
		public int Count;
	}

	public abstract class PlayerInventoryBase : IDisposable
	{
		public void Read<TFill>(TFill list)
			where TFill : IList<Entity>
		{
			Read<TFill, string[]>(list);
		}

		public abstract void Read<TFill, TRestrict>(TFill list, TRestrict restrictTypes = default)
			where TFill : IList<Entity>
			where TRestrict : IList<string>;

		public abstract void GetItemsOfAsset<TFill>(TFill list, Entity assetEntity)
			where TFill : IList<Entity>;

		public abstract Entity GetStack(Entity assetEntity);
		public abstract bool   Contains(Entity itemEntity);

		public abstract void Dispose();
	}

	public interface IWritablePlayerInventory
	{
		World ActionWorld { get; set; }

		/// <summary>
		/// Create an item based on an asset
		/// </summary>
		/// <param name="assetEntity">The asset</param>
		/// <remarks>
		///	If the item is stackable, it will add one to the current stack or create the stack if it doesn't exist
		/// </remarks>
		/// <returns></returns>
		Entity Create(Entity assetEntity);

		/// <summary>
		/// Add to an item stack
		/// </summary>
		/// <param name="assetEntity">The asset</param>
		/// <param name="count">How much item will be added to the stack. Can be negative</param>
		/// <returns>A perhaps null entity. It's not null if the stack is superior to 0</returns>
		Entity AddToStack(Entity assetEntity, int count);

		bool Destroy(Entity itemEntity);

		void Clear();
		void Clear(Entity assetEntity);
	}

	/// <summary>
	/// An inventory that support equipments that can be swapped on units
	/// </summary>
	public interface ISwapEquipmentInventory
	{
		void RequestSwap(SafeEntityFocus origin, string attachment, Entity newEquipmentEntity);
	}

	public class PlayerInventoryNoRead : PlayerInventoryBase
	{
		public override void Read<TFill, TRestrict>(TFill list, TRestrict restrictTypes = default)
		{
		}

		public override void GetItemsOfAsset<TFill>(TFill list, Entity assetEntity)
		{
		}

		public override Entity GetStack(Entity assetEntity)
		{
			return default;
		}

		public override bool Contains(Entity itemEntity)
		{
			return false;
		}

		public override void Dispose()
		{
		}
	}

	public class LocalPlayerInventory : PlayerInventoryBase,
	                                    IWritablePlayerInventory,
	                                    ISwapEquipmentInventory
	{
		private PooledDictionary<string, PooledList<Entity>> itemsByType = new();

		public override void Read<TFill, TRestrict>(TFill list, TRestrict restrictTypes = default)
		{
			if (restrictTypes?.Count > 0)
			{
				foreach (var type in restrictTypes)
					if (itemsByType.TryGetValue(type, out var items))
						foreach (var item in items)
							list.Add(item);

				return;
			}

			foreach (var (_, items) in itemsByType)
			foreach (var item in items)
				list.Add(item);
		}

		public override void GetItemsOfAsset<TFill>(TFill list, Entity assetEntity)
		{
			if (!itemsByType.TryGetValue(assetEntity.Get<GameItemDescription>().Type, out var itemList))
				return;

			foreach (var item in itemList)
				if (item.Get<ItemInventory>().AssetEntity == assetEntity)
					list.Add(item);
		}

		public override Entity GetStack(Entity assetEntity)
		{
			var desc = assetEntity.Get<GameItemDescription>();
			return getStackIndex(desc.Type, desc.Id);
		}

		public override bool Contains(Entity itemEntity)
		{
			var assetEntity = itemEntity.Get<ItemInventory>().AssetEntity;

			if (!itemsByType.TryGetValue(assetEntity.Get<GameItemDescription>().Type, out var itemList))
				return false;

			foreach (var item in itemList)
				if (item == itemEntity)
					return true;

			return false;
		}

		public override void Dispose()
		{
			Clear();
			itemsByType.Dispose();
		}

		private Entity getStackIndex(string typeId, ResPath assetId)
		{
			var items = itemsByType[typeId];
			foreach (var item in items)
			{
				if (item.Get<GameItemDescription>().Id.Equals(assetId))
					return item;
			}

			return default;
		}

		public World ActionWorld { get; set; }

		public Entity Create(Entity assetEntity)
		{
			if (assetEntity.Has<GameItemIsStackable>())
				return AddToStack(assetEntity, 1);

			var desc = assetEntity.Get<GameItemDescription>();
			if (!itemsByType.TryGetValue(desc.Type, out var list))
				itemsByType[desc.Type] = list = new();

			var entity = ActionWorld.CreateEntity();
			entity.Set(new ItemInventory { AssetEntity = assetEntity });

			list.Add(entity);
			return entity;
		}

		public Entity AddToStack(Entity assetEntity, int count)
		{
			var desc = assetEntity.Get<GameItemDescription>();
			if (!itemsByType.TryGetValue(desc.Type, out var list))
				itemsByType[desc.Type] = list = new();

			var stackIndex = getStackIndex(desc.Type, desc.Id);
			if (!stackIndex.IsAlive)
				if (count <= 0)
					return default;
				else
				{
					stackIndex = ActionWorld.CreateEntity();
					stackIndex.Set(new ItemInventory { AssetEntity = assetEntity });
					stackIndex.Set(new ItemStackInventory());

					list.Add(stackIndex);
				}

			stackIndex.Get<ItemStackInventory>().Count += count;
			if (stackIndex.Get<ItemStackInventory>().Count > 0)
				return stackIndex;

			stackIndex.Dispose();
			stackIndex = default;

			return stackIndex;
		}

		public bool Destroy(Entity itemEntity)
		{
			return itemEntity.TryGet(out ItemInventory itemInventory)
			       && itemInventory.AssetEntity.TryGet(out GameItemDescription desc)
			       && itemsByType.TryGetValue(desc.Type, out var list)
			       && list.Remove(itemEntity);
		}

		public void Clear()
		{
			foreach (var (_, list) in itemsByType)
				list.Dispose();

			itemsByType.Clear();
		}

		public void Clear(Entity assetEntity)
		{
			if (itemsByType.TryGetValue(assetEntity.Get<GameItemDescription>().Type, out var list))
				list.Clear();
		}

		public void RequestSwap(SafeEntityFocus origin, string attachment, Entity newEquipmentEntity)
		{
			var gameWorld = origin.GameWorld;
			foreach (ref var equipment in origin.GetBuffer<UnitDefinedEquipments>())
			{
				if (gameWorld.GetComponentData<UnitAttachmentResource>(equipment.Attachment.Handle).Value.Span.SequenceEqual(attachment))
					equipment.Item = newEquipmentEntity;
			}
		}
	}
}