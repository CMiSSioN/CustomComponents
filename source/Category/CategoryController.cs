﻿using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.UI;

namespace CustomComponents
{
    public enum CategoryError
    {
        None,
        AreadyEquiped,
        MaximumReached,
        AlreadyEquipedLocation,
        MaximumReachedLocation,
        AllowMix
    }

    internal static class CategoryController
    {
        internal static void ValidateMech(Dictionary<MechValidationType, List<string>> errors,
            MechValidationLevel validationLevel, MechDef mechDef)
        {
            var items_by_category = (from item in mechDef.Inventory
                                     where item.Def is ICategory
                                     let def = item.Def as ICategory
                                     select new
                                     {
                                         category = def.CategoryDescriptor,
                                         itemdef = item.Def,
                                         itemref = item,
                                         mix = def.GetCategoryTag()
                                     }).GroupBy(i => i.category).ToDictionary(i => i.Key, i => i.ToList());

            foreach (var category in Control.GetCategories().Where(i => i.Required))
            {
                if (!items_by_category.ContainsKey(category) || items_by_category[category].Count < category.MinEquiped)
                    if (category.MinEquiped == 1)
                        errors[MechValidationType.InvalidInventorySlots].Add(string.Format(category.ValidateRequred, category.DisplayName.ToUpper(), category.DisplayName));
                    else
                        errors[MechValidationType.InvalidInventorySlots].Add(string.Format(category.ValidateMinimum, category.DisplayName.ToUpper(), category.DisplayName, category.MinEquiped));
            }

            foreach (var pair in items_by_category)
            {
                if (pair.Key.MaxEquiped > 0 && pair.Value.Count > pair.Key.MaxEquiped)
                    if (pair.Key.MaxEquiped == 1)
                        errors[MechValidationType.InvalidInventorySlots].Add(string.Format(pair.Key.ValidateUnique,
                            pair.Key.DisplayName.ToUpper(), pair.Key.DisplayName));
                    else
                        errors[MechValidationType.InvalidInventorySlots].Add(string.Format(pair.Key.ValidateMaximum,
                            pair.Key.DisplayName.ToUpper(), pair.Key.DisplayName, pair.Key.MaxEquiped));

                if (!pair.Key.AllowMixTags)
                {
                    string def = pair.Value[0].mix;

                    bool flag = pair.Value.Any(i => i.mix != def);
                    if (flag)
                    {
                        errors[MechValidationType.InvalidInventorySlots].Add(string.Format(pair.Key.ValidateMixed,
                            pair.Key.DisplayName.ToUpper(), pair.Key.DisplayName));
                    }
                }

                if (pair.Key.MaxEquipedPerLocation > 0)
                {
                    var max = pair.Value.GroupBy(i => i.itemref.MountedLocation).Max(i => i.Count());
                    if (max > pair.Key.MaxEquipedPerLocation)
                        if (pair.Key.MaxEquipedPerLocation == 1)
                            errors[MechValidationType.InvalidInventorySlots].Add(string.Format(pair.Key.ValidateUniqueLocation,
                                pair.Key.DisplayName.ToUpper(), pair.Key.DisplayName));
                        else
                            errors[MechValidationType.InvalidInventorySlots].Add(string.Format(pair.Key.ValidateMaximumLocation,
                                pair.Key.DisplayName.ToUpper(), pair.Key.DisplayName, pair.Key.MaxEquipedPerLocation));
                }
            }
        }

        internal static CategoryError ValidateAdd(ICategory component, LocationHelper location, out int count)
        {
            var category = component.CategoryDescriptor;

            var items = location.mechLab.activeMechDef.Inventory
                .Where(i => (i.Def as ICategory)?.CategoryDescriptor == category).ToList();

            count = 0;

            if (category.MaxEquiped > 0)
            {
                if (items.Count >= category.MaxEquiped)
                {
                    if (category.Unique)
                        return CategoryError.AreadyEquiped;
                    count = items.Count;
                    return CategoryError.MaximumReached;
                }
            }

            if (category.MaxEquipedPerLocation > 0)
            {
                int count_per_location = items.Count(i => i.MountedLocation == location.widget.loadout.Location);
                if (count_per_location >= category.MaxEquipedPerLocation)
                {
                    if (category.UniqueForLocation)
                        return CategoryError.AlreadyEquipedLocation;

                    count = count_per_location;
                    return CategoryError.MaximumReachedLocation;
                }
            }

            if (!category.AllowMixTags)
            {
                if (items.Any(i => (i.Def as ICategory).GetCategoryTag() != component.GetCategoryTag()))
                    return CategoryError.AllowMix;
            }

            return CategoryError.None;
        }

        internal static IValidateDropResult ValidateDrop(MechLabItemSlotElement element, LocationHelper location, IValidateDropResult last_result)
        {
            Control.Logger.LogDebug($"ICategory validation start for {location.widget.loadout?.Location.ToString() ?? "???"}");

            var component = element.ComponentRef.Def;

            if (!(component is ICategory cat_component))
            {
                Control.Logger.LogDebug("Not a category");
                return last_result;
            }


            var error = ValidateAdd(cat_component, location, out var count);


            Control.Logger.LogDebug($"Category: {cat_component.CategoryID}, Error: {error}");


            if (error == CategoryError.None)
                return last_result;

            var category = cat_component.CategoryDescriptor;

            Control.Logger.LogDebug($"Category: Validator state create");

            if (category.AutoReplace && error != CategoryError.AllowMix)
            {
                Control.Logger.LogDebug($"Category: Search for repacement");

                var replacement = location.LocalInventory.FirstOrDefault(e => e?.ComponentRef?.Def is ICategory cat && cat.CategoryID == category.Name);

                if (replacement != null)
                    return new ValidateDropReplaceItem(replacement);
            }

            Control.Logger.LogDebug($"Category: return error message");

            switch (error)
            {
                case CategoryError.AreadyEquiped:
                    return new ValidateDropError(string.Format(category.AddAlreadyEquiped, category.DisplayName));
                case CategoryError.MaximumReached:
                    return new ValidateDropError(string.Format(category.AddMaximumReached, category.DisplayName, count));
                case CategoryError.AlreadyEquipedLocation:
                    return new ValidateDropError(string.Format(category.AddAlreadyEquipedLocation, category.DisplayName, location.LocationName));
                case CategoryError.MaximumReachedLocation:
                    return new ValidateDropError(string.Format(category.AddMaximumLocationReached, category.DisplayName, count, location.LocationName));
                case CategoryError.AllowMix:
                    return new ValidateDropError(string.Format(category.AddMixed, category.DisplayName));
            }

            return last_result;
        }


        internal static bool ValidateMechCanBeFielded(MechDef mechDef)
        {
            var items_by_category = (from item in mechDef.Inventory
                                     where item.Def is ICategory
                                     let def = item.Def as ICategory
                                     select new
                                     {
                                         category = def.CategoryDescriptor,
                                         itemdef = item.Def,
                                         itemref = item,
                                         mix = def.GetCategoryTag()
                                     }).GroupBy(i => i.category).ToDictionary(i => i.Key, i => i.ToList());

            foreach (var category in Control.GetCategories().Where(i => i.Required))
                if (!items_by_category.ContainsKey(category) || items_by_category[category].Count < category.MinEquiped)
                    return false;

            foreach (var pair in items_by_category)
            {
                if (pair.Key.MaxEquiped > 0 && pair.Value.Count > pair.Key.MaxEquiped)
                    return false;

                if (!pair.Key.AllowMixTags)
                {
                    string def = pair.Value[0].mix;

                    bool flag = pair.Value.Any(i => i.mix != def);
                    if (flag) return false;
                }

                if (pair.Key.MaxEquipedPerLocation > 0)
                {
                    var max = pair.Value.GroupBy(i => i.itemref.MountedLocation).Max(i => i.Count());
                    if (max > pair.Key.MaxEquipedPerLocation)
                        return false;
                }
            }

            return true;
        }
    }
}