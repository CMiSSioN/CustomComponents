﻿using System;
using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.UI;
using Localize;

namespace CustomComponents
{
    internal class TagRestrictionsHandler
    {
        internal static TagRestrictionsHandler Shared = new TagRestrictionsHandler();

        private Dictionary<string, TagRestrictions> Restrictions { get; } = new Dictionary<string, TagRestrictions>();

        internal void Add(TagRestrictions restrictions)
        {
            Restrictions.Add(restrictions.Tag, restrictions);
        }

        internal bool ValidateMechCanBeFielded(MechDef mechDef)
        {
            return ValidateMech(out var error, mechDef);
        }

        internal void ValidateMech(Dictionary<MechValidationType, List<Text>> errors, MechValidationLevel validationLevel, MechDef mechDef)
        {
            ValidateMech(out var error, mechDef, errors: errors);
        }

        internal string ValidateDrop(MechLabItemSlotElement drop_item, MechDef mech, List<InvItem> new_inventory, List<IChange> changes)
        {
            ValidateMech(out var error, mech, drop_item.ComponentRef.Def);
            return error;
        }

        internal bool ValidateMech(
            out string error,
            MechDef mechDef,
            MechComponentDef droppedComponent = null,
            Dictionary<MechValidationType, List<Text>> errors = null)
        {
            error = null;

            var tagsOnMech = new HashSet<string>();

            // chassis
            {
                var chassis = mechDef.Chassis;
                // tags
                if (chassis.ChassisTags != null)
                {
                    tagsOnMech.UnionWith(chassis.ChassisTags);
                }

                // id
                var identifier = chassis.Description.Id;
                tagsOnMech.Add(identifier);
            }

            void ProcessComponent(MechComponentDef def, HashSet<string> tagsForComponent)
            {
                // tags
                if (def.ComponentTags != null)
                {
                    tagsForComponent.UnionWith(def.ComponentTags);
                }

                // id
                var identifier = def.Description.Id;
                tagsForComponent.Add(identifier);

                // category for component
                foreach (var component in def.GetComponents<Category>())
                {
                    tagsForComponent.Add(component.CategoryID);
                    if (!string.IsNullOrEmpty(component.Tag))
                        tagsForComponent.Add(component.Tag);
                }
            }

            // components
            foreach (var def in mechDef.Inventory.Select(r => r.Def))
            {
                ProcessComponent(def, tagsOnMech);
            }

            HashSet<string> tagsForDropped = null;
            if (droppedComponent != null)
            {
                tagsForDropped = new HashSet<string>();
                ProcessComponent(droppedComponent, tagsForDropped);
                tagsOnMech.UnionWith(tagsForDropped); // used for incompatible check
            }

            var checkRequiresForTags = tagsOnMech;
            if (tagsForDropped != null)
            {
                checkRequiresForTags = tagsForDropped;

                if (!Control.Settings.TagRestrictionDropValidateRequiredTags)
                {
                    checkRequiresForTags = new HashSet<string>();
                }
            }

            foreach (var tag in checkRequiresForTags)
            {
                var requiredAnyTags = RequiredAnyTags(tag);
                var hasMetAnyRequiredAnyTags = true; // no required any tags = ok
                foreach (var requiredAnyTag in requiredAnyTags)
                {
                    hasMetAnyRequiredAnyTags = false; // at least one required any tag check = nok
                    if (tagsOnMech.Contains(requiredAnyTag))
                    {
                        hasMetAnyRequiredAnyTags = true; // at least on required any tag found = ok
                        break;
                    }
                }

                if (hasMetAnyRequiredAnyTags)
                {
                    continue;
                }

                var tagName = NameForTag(tag);
                error = $"{tagName} requirements are not met";

                if (errors == null)
                {
                    return false;
                }

                errors[MechValidationType.InvalidInventorySlots].Add(new Text(error));
            }


            foreach (var tag in checkRequiresForTags)
            {
                var requiredTags = RequiredTags(tag);
                foreach (var requiredTag in requiredTags)
                {
                    if (tagsOnMech.Contains(requiredTag))
                    {
                        continue;
                    }

                    var tagName = NameForTag(tag);
                    var requiredTagName = NameForTag(requiredTag);
                    error = $"{tagName} requires {requiredTagName}";

                    if (errors == null)
                    {
                        return false;
                    }

                    errors[MechValidationType.InvalidInventorySlots].Add(new Text(error));
                }
            }


            var checkIncompatiblesForTags = tagsOnMech;
            if (tagsForDropped != null)
            {
                if (!Control.Settings.TagRestrictionDropValidateIncompatibleTags)
                {
                    checkIncompatiblesForTags = new HashSet<string>();
                }
            }

            foreach (var tag in checkIncompatiblesForTags)
            {
                var tagsPool = tagsOnMech;
                // if dropping we either want only to check either:
                // - the "dropped tags incompatibles" with mech+items
                // - each "mech+items incompatibles" with dropped tags
                if (tagsForDropped != null && !tagsForDropped.Contains(tag))
                {
                    tagsPool = tagsForDropped;
                }

                var incompatibleTags = IncompatibleTags(tag);
                foreach (var incompatibleTag in incompatibleTags)
                {
                    if (!tagsPool.Contains(incompatibleTag))
                    {
                        continue;
                    }
                    
                    var tagName = NameForTag(tag);
                    var incompatibleTagName = NameForTag(incompatibleTag);
                    error = $"{tagName} can't be used with {incompatibleTagName}";

                    if (errors == null)
                    {
                        return false;
                    }
                    
                    errors[MechValidationType.InvalidInventorySlots].Add(new Text(error));
                }
            }

            return error == null;
        }

        private IEnumerable<string> RequiredTags(string tag)
        {
            if (!Restrictions.TryGetValue(tag, out var restriction))
            {
                yield break;
            }

            if (restriction.RequiredTags == null)
            {
                yield break;
            }

            foreach (var requiredTag in restriction.RequiredTags)
            {
                yield return requiredTag;
            }
        }

        private IEnumerable<string> RequiredAnyTags(string tag)
        {
            if (!Restrictions.TryGetValue(tag, out var restriction))
            {
                yield break;
            }

            if (restriction.RequiredAnyTags == null)
            {
                yield break;
            }

            foreach (var requiredTag in restriction.RequiredAnyTags)
            {
                yield return requiredTag;
            }
        }

        private IEnumerable<string> IncompatibleTags(string tag)
        {
            if (!Restrictions.TryGetValue(tag, out var restriction))
            {
                yield break;
            }

            if (restriction.IncompatibleTags == null)
            {
                yield break;
            }

            foreach (var incompatibleTag in restriction.IncompatibleTags)
            {
                yield return incompatibleTag;
            }
        }

        private static string NameForTag(string tag)
        {
            {
                var categoryDescriptor = Control.GetCategory(tag);
                if (categoryDescriptor != null)
                {
                    return categoryDescriptor.DisplayName;
                }
            }

            {
                var dataManager = UnityGameInstance.BattleTechGame.DataManager;

                // ChassisDef

                if (dataManager.ChassisDefs.TryGet(tag, out var chassis))
                {
                    return chassis.Description.UIName;
                }

                // MechComponentDef

                if (dataManager.AmmoBoxDefs.TryGet(tag, out var ammoBox))
                {
                    return ammoBox.Description.UIName;
                }

                if (dataManager.HeatSinkDefs.TryGet(tag, out var heatSink))
                {
                    return heatSink.Description.UIName;
                }

                if (dataManager.JumpJetDefs.TryGet(tag, out var jumpJet))
                {
                    return jumpJet.Description.UIName;
                }

                if (dataManager.UpgradeDefs.TryGet(tag, out var upgrade))
                {
                    return upgrade.Description.UIName;
                }

                if (dataManager.WeaponDefs.TryGet(tag, out var weapon))
                {
                    return weapon.Description.UIName;
                }
            }

            return tag;
        }
    }
}