#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector.Editor;
using UnityEngine;

namespace Moonlight.Localization
{
    public class L10nString_BigMultilineProcessor : OdinAttributeProcessor<L10nString>
    {
        public override void ProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member, List<Attribute> attributes)
        {
            // Only act if the parent field has [BigMultiline]
            var big = parentProperty.GetAttribute<BigMultilineAttribute>();
            if (big == null) return;

            // Only target the localized value field
            if (member.Name == "_localizedValue")
            {
                // Remove existing multi-line style so we can replace it
                attributes.RemoveAll(a => a is MultilineAttribute || a is TextAreaAttribute);

                // Use Unity's resizable TextArea with the requested line bounds
                attributes.Add(new TextAreaAttribute(big.MinLines, big.MaxLines));
            }
        }
    }
}
#endif