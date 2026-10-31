using System;
using System.Collections.Generic;
using System.Linq;
using GVK.Koth.Core;
using GVK.Koth.Core.Config;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace GVK.Koth.Zones
{
    public enum LootDeliveryStatus { Retry, Delivered, ReconciliationRequired }

    public sealed class LootDeliveryResult
    {
        public LootDeliveryStatus Status { get; }
        public LootReconciliationSave Reconciliation { get; }
        public string Reason { get; }

        internal LootDeliveryResult(LootDeliveryStatus status, LootReconciliationSave reconciliation = null, string reason = "")
        {
            Status = status;
            Reconciliation = reconciliation;
            Reason = reason ?? "";
        }
    }

    /// <summary>
    /// Puts loot into a zone's public container, found by name inside the zone. Game thread only.
    /// </summary>
    public static class LootDelivery
    {
        private static readonly List<VRage.Game.Entity.MyEntity> Entities = new List<VRage.Game.Entity.MyEntity>();

        private sealed class BundleItem
        {
            public MyDefinitionId Id;
            public MyObjectBuilder_PhysicalObject Builder;
            public string TypeId;
            public string SubtypeId;
            public MyFixedPoint Amount;
            public MyFixedPoint Before;
        }

        private static LootDeliveryResult Retry(string reason) => new LootDeliveryResult(LootDeliveryStatus.Retry, reason: reason);

        // Presence check only; use a local cache so diagnostics do not alter a runtime's cache.
        internal static bool HasContainer(ZoneProfile zone, Vector3D center)
        {
            long cachedId = 0;
            return FindContainer(zone, center, ref cachedId) != null;
        }

        public static LootDeliveryResult TryDeliver(ZoneProfile zone, Vector3D center, ref long cachedContainerId, out IMyCargoContainer container)
        {
            container = FindContainer(zone, center, ref cachedContainerId);
            return container == null ? Retry("No eligible named NPC-owned loot container was found in the zone.")
                : TryDeliverBundle(zone, container.GetInventory(), container.EntityId);
        }

        // This method and inventory mutations run synchronously on the game thread.
        internal static LootDeliveryResult TryDeliverBundle(ZoneProfile zone, VRage.Game.ModAPI.IMyInventory inventory, long containerId)
        {
            var items = new List<BundleItem>();
            var concrete = inventory as MyInventory;
            try
            {
                if (zone?.Loot == null || zone.Loot.Count == 0)
                    return Retry("The configured loot bundle is missing or empty.");
                if (concrete == null)
                    return Retry("The container inventory is unavailable or unsupported.");
                if (!inventory.CanPutItems)
                    return Retry("The container inventory cannot receive items.");

                foreach (var loot in zone.Loot)
                {
                    if (loot == null || loot.Amount <= 0 || string.IsNullOrWhiteSpace(loot.TypeId)
                        || string.IsNullOrWhiteSpace(loot.SubtypeId)
                        || !MyObjectBuilderType.TryParse("MyObjectBuilder_" + loot.TypeId, out var type))
                        return Retry("A loot entry has an invalid type, subtype or amount; check the zone configuration.");
                    var id = new MyDefinitionId(type, loot.SubtypeId);
                    if (!MyDefinitionManager.Static.TryGetPhysicalItemDefinition(id, out var definition)
                        || definition == null
                        || !(MyObjectBuilderSerializer.CreateNewObject(id) is MyObjectBuilder_PhysicalObject builder)
                        || builder.GetObjectId() != id
                        || (Sandbox.Engine.Utils.MyFakes.ENABLE_COMPONENT_BLOCKS && MyDefinitionManager.Static.TryGetComponentBlockDefinition(id) != null))
                        return Retry("A loot definition is invalid, nonphysical or requires unsupported component-block conversion.");

                    var item = items.FirstOrDefault(i => i.Id == id);
                    if (item == null)
                    {
                        item = new BundleItem { Id = id, Builder = builder, TypeId = loot.TypeId, SubtypeId = loot.SubtypeId };
                        items.Add(item);
                    }
                    long raw = checked(item.Amount.RawValue + checked((long)loot.Amount * 1000000L));
                    item.Amount = new MyFixedPoint { RawValue = raw };
                }

                // Reserve volume, mass and slots for the entire bundle, not each item against
                // the same unchanged inventory. A slot can be reserved only once.
                long volume = concrete.CurrentVolume.RawValue;
                long mass = concrete.CurrentMass.RawValue;
                int freeSlots = checked(concrete.MaxItemCount - concrete.GetItems().Count);
                var reservedStacks = new HashSet<uint>();
                var adapter = new MyInventoryItemAdapter();
                foreach (var item in items)
                {
                    if (!adapter.TryAdapt(item.Id) || !concrete.CanItemsBeAdded(item.Amount, item.Id)
                        || concrete.ComputeAmountThatFits(item.Id) < item.Amount)
                        return Retry("A loot item cannot fit or is rejected by the inventory's capacity or type constraints.");
                    float v = adapter.Volume, m = adapter.Mass;
                    if (float.IsNaN(v) || float.IsInfinity(v) || v < 0 || float.IsNaN(m) || float.IsInfinity(m) || m < 0)
                        return Retry("A loot definition has invalid volume or mass.");
                    var volumePerItem = (MyFixedPoint)v;
                    var massPerItem = (MyFixedPoint)m;
                    // Decimal arithmetic detects fixed-point overflow instead of wrapping.
                    long addedVolume = checked((long)(((decimal)item.Amount.RawValue * volumePerItem.RawValue) / 1000000m));
                    long addedMass = checked((long)(((decimal)item.Amount.RawValue * massPerItem.RawValue) / 1000000m));
                    volume = checked(volume + addedVolume);
                    mass = checked(mass + addedMass);
                    if ((concrete.IsConstrained && volume > concrete.MaxVolume.RawValue) || mass > concrete.MaxMass.RawValue)
                        return Retry("The complete loot bundle exceeds the container's shared volume or mass capacity.");

                    long remaining = item.Amount.RawValue;
                    long stackLimit = item.Builder.CanStack(item.Builder) ? adapter.MaxStackAmount.RawValue : 1000000L;
                    if (stackLimit <= 0 || freeSlots < 0)
                        return Retry("The inventory has invalid stack or slot capacity for the loot bundle.");
                    foreach (var stack in concrete.GetItems())
                    {
                        if (remaining == 0) break;
                        if (reservedStacks.Contains(stack.ItemId) || !stack.Content.CanStack(item.Builder)) continue;
                        long space = Math.Max(0L, stackLimit - stack.Amount.RawValue);
                        if (space == 0) continue;
                        reservedStacks.Add(stack.ItemId);
                        remaining -= Math.Min(remaining, space);
                    }
                    if (remaining > 0)
                    {
                        long needed = 1 + (remaining - 1) / stackLimit;
                        if (needed > freeSlots) return Retry("The complete loot bundle exceeds the container's available slots.");
                        freeSlots -= (int)needed;
                    }
                    item.Before = concrete.GetItemAmount(item.Id);
                    checked { var ignored = item.Before.RawValue + item.Amount.RawValue; }
                }
            }
            catch (Exception)
            {
                // All failures here occur before the first real insertion.
                return Retry("Loot validation or capacity preflight failed before insertion; check the configuration and inventory.");
            }

            // Retain per-stack baselines so rollback removes only increases attributable to
            // this attempt, never pre-existing quantities or another definition.
            var baseline = new Dictionary<uint, (MyDefinitionId Id, long Amount)>();
            try
            {
                foreach (var stack in concrete.GetItems())
                    baseline.Add(stack.ItemId, (stack.Content.GetObjectId(), stack.Amount.RawValue));
            }
            catch (Exception) { return Retry("Could not read the container's pre-delivery inventory snapshot."); }

            string reason = "Insertion was incomplete or raised an exception.";
            try
            {
                foreach (var item in items)
                {
                    bool added = concrete.AddItems(item.Amount, item.Builder);
                    if (!added || concrete.GetItemAmount(item.Id).RawValue != checked(item.Before.RawValue + item.Amount.RawValue))
                        throw new InvalidOperationException("Inventory did not accept the complete item amount.");
                }
                if (items.All(i => concrete.GetItemAmount(i.Id).RawValue == checked(i.Before.RawValue + i.Amount.RawValue)))
                    return new LootDeliveryResult(LootDeliveryStatus.Delivered);
            }
            catch (Exception ex) { reason = "Insertion failed: " + ex.GetType().Name; }

            try
            {
                // Reject anomalous changes rather than removing quantities that cannot be
                // attributed to this attempt. Capture the list before removal shifts it.
                var removals = new List<(uint Id, long Raw)>();
                var totals = new Dictionary<MyDefinitionId, long>();
                var seen = new HashSet<uint>();
                foreach (var stack in concrete.GetItems())
                {
                    var id = stack.Content.GetObjectId();
                    var item = items.FirstOrDefault(i => i.Id == id);
                    if (item == null) continue;
                    long old = 0;
                    if (baseline.TryGetValue(stack.ItemId, out var prior))
                    {
                        seen.Add(stack.ItemId);
                        if (prior.Id != id || stack.Amount.RawValue < prior.Amount)
                            throw new InvalidOperationException("Pre-existing stack changed.");
                        old = prior.Amount;
                    }
                    long added = checked(stack.Amount.RawValue - old);
                    if (added > 0)
                    {
                        totals.TryGetValue(id, out var total);
                        totals[id] = checked(total + added);
                        removals.Add((stack.ItemId, added));
                    }
                }
                foreach (var prior in baseline)
                    if (items.Any(i => i.Id == prior.Value.Id) && !seen.Contains(prior.Key))
                        throw new InvalidOperationException("Pre-existing stack disappeared.");
                foreach (var item in items)
                {
                    totals.TryGetValue(item.Id, out var added);
                    if (added > item.Amount.RawValue || concrete.GetItemAmount(item.Id).RawValue != checked(item.Before.RawValue + added))
                        throw new InvalidOperationException("Inventory changes cannot be attributed to this attempt.");
                }
                foreach (var removal in removals)
                    concrete.RemoveItems(removal.Id, new MyFixedPoint { RawValue = removal.Raw }, true, false);
                if (items.All(i => concrete.GetItemAmount(i.Id) == i.Before))
                    return Retry("Loot insertion failed or was incomplete; rollback restored the pre-delivery amounts.");
                reason += " Rollback did not restore the pre-drop amounts.";
            }
            catch (Exception ex) { reason += " Rollback could not be verified: " + ex.GetType().Name; }

            var marker = new LootReconciliationSave
            {
                AttemptId = Guid.NewGuid().ToString(), ContainerId = containerId,
                CreatedUtc = DateTime.UtcNow, Reason = reason
            };
            foreach (var item in items)
            {
                var entry = new LootReconciliationItemSave
                {
                    TypeId = item.TypeId, SubtypeId = item.SubtypeId,
                    RequestedRaw = item.Amount.RawValue, BeforeRaw = item.Before.RawValue
                };
                try { entry.ObservedRaw = concrete.GetItemAmount(item.Id).RawValue; entry.ObservedKnown = true; }
                catch (Exception) { entry.ObservedKnown = false; }
                marker.Items.Add(entry);
            }
            return new LootDeliveryResult(LootDeliveryStatus.ReconciliationRequired, marker, reason);
        }

        /// <summary>
        /// The one loot container for this zone: named LootContainerName, inside the zone, on a grid an NPC owns
        /// (majority owner), nearest the zone centre. Player-built containers never qualify, even unowned ones.
        /// </summary>
        private static IMyCargoContainer FindContainer(ZoneProfile zone, Vector3D center, ref long cachedId)
        {
            double radiusSq = zone.Radius * zone.Radius;

            if (cachedId != 0 && MyEntities.TryGetEntityById(cachedId, out var cached)
                && cached is IMyCargoContainer known && IsValid(known, zone, center, radiusSq))
                return known;

            cachedId = 0;
            IMyCargoContainer best = null;
            double bestDistSq = double.MaxValue;

            Entities.Clear();
            var sphere = new BoundingSphereD(center, zone.Radius);
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, Entities);
            foreach (var entity in Entities)
            {
                if (!(entity is MyCubeGrid grid) || grid.Closed || !IsNpcGrid(grid))
                    continue;

                foreach (var block in grid.GetFatBlocks())
                {
                    if (!(block is IMyCargoContainer cargo) || !IsValid(cargo, zone, center, radiusSq))
                        continue;

                    double distSq = Vector3D.DistanceSquared(cargo.GetPosition(), center);
                    if (distSq < bestDistSq)
                    {
                        best = cargo;
                        bestDistSq = distSq;
                    }
                }
            }
            Entities.Clear();

            if (best != null)
                cachedId = best.EntityId;
            return best;
        }

        private static bool IsValid(IMyCargoContainer cargo, ZoneProfile zone, Vector3D center, double radiusSq)
        {
            return !cargo.Closed && cargo.IsFunctional && NameMatches(cargo, zone)
                && cargo.CubeGrid is MyCubeGrid grid && IsNpcGrid(grid)
                && Vector3D.DistanceSquared(cargo.GetPosition(), center) <= radiusSq;
        }

        private static bool IsNpcGrid(MyCubeGrid grid)
        {
            return grid.BigOwners.Count > 0 && MySession.Static?.Players?.IdentityIsNpc(grid.BigOwners[0]) == true;
        }

        private static bool NameMatches(IMyTerminalBlock block, ZoneProfile zone)
        {
            return string.Equals(block.CustomName?.Trim(), zone.LootContainerName?.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
