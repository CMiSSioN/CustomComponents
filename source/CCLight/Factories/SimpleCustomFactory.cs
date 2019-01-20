﻿using System.Collections.Generic;
using HBS.Util;

namespace CustomComponents
{
    public class SimpleCustomFactory<TCustom, TDef> : ICustomFactory
        where TCustom : SimpleCustom<TDef>, new()
        where TDef : class
    {
        public SimpleCustomFactory(string customName)
        {
            CustomName = customName;
        }

        public string CustomName { get; }

        public virtual IEnumerable<ICustom> Create(object target, Dictionary<string, object> values)
        {
            if (!(target is TDef def))
            {
                yield break;
            }

            if (!values.TryGetValue(Control.CustomSectionName, out var customSettingsObject))
            {
                yield break;
            }

            if (!(customSettingsObject is Dictionary<string, object> customSettings))
            {
                yield break;
            }

            if (!customSettings.TryGetValue(CustomName, out var componentSettingsObject))
            {
                yield break;
            }

            if (!(componentSettingsObject is Dictionary<string, object> componentSettings))
            {
                yield break;
            }

            var obj = new TCustom();
            JSONSerializationUtility.RehydrateObjectFromDictionary(obj, componentSettings);
            obj.Def = def;

            yield return obj;
        }

        public override string ToString()
        {
            return CustomName + ".SimpleFactory";
        }
    }
}