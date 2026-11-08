using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using VanillaPsycastsExpanded;
using VanillaPsycastsExpanded.UI;
using Verse;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace Militarmagier.VPE;

[StaticConstructorOnStartup]
public static class Patch_MagierPathLayout
{
    static Patch_MagierPathLayout()
    {
        new Harmony("Militarmagier.VPE.PathLayout").Patch(
            AccessTools.Method(typeof(PsycastsUIUtility), nameof(PsycastsUIUtility.DoPathAbilities)),
            prefix: new HarmonyMethod(typeof(Patch_MagierPathLayout), nameof(Prefix)));
    }

    private static bool Prefix(Rect inRect, PsycasterPathDef path,
        Dictionary<AbilityDef, Vector2> abilityPos, Action<Rect, AbilityDef> doAbility)
    {
        if (path.defName != "DMST_VPE_MagierPath"
            || !path.abilityLevelsInOrder.Any(row => row.Length > 3)) return true;
        if (PsycastsUIUtility.Hediff == null || PsycastsUIUtility.CompAbilities == null) return false;

        foreach (AbilityDef ability in path.abilities)
        {
            if (!abilityPos.TryGetValue(ability, out Vector2 position)) continue;
            foreach (AbilityDef prerequisite in ability.Psycast().prerequisites)
                if (abilityPos.TryGetValue(prerequisite, out Vector2 prerequisitePosition))
                    Widgets.DrawLine(position, prerequisitePosition,
                        PsycastsUIUtility.CompAbilities.HasAbility(prerequisite) ? Color.white : Color.grey, 2f);
        }

        for (int level = 0; level < path.abilityLevelsInOrder.Length; level++)
        {
            AbilityDef[] row = path.abilityLevelsInOrder[level];
            float spacing = Mathf.Min(51f, (inRect.width - 36f) / Mathf.Max(1, row.Length - 1));
            float y = inRect.y + (path.MaxLevel - 1 - level) * inRect.height / path.MaxLevel + 10f;
            for (int column = 0; column < row.Length; column++)
            {
                AbilityDef ability = row[column];
                if (ability == PsycasterPathDef.Blank) continue;
                float x = inRect.center.x - 18f + (column - (row.Length - 1) / 2f) * spacing;
                Rect rect = new(x, y, 36f, 36f);
                abilityPos[ability] = rect.center;
                doAbility(rect, ability);
            }
        }
        return false;
    }
}
