using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using VanillaPsycastsExpanded;
using VEF.Abilities;
using Verse;
using Ability = VEF.Abilities.Ability;

namespace Militarmagier.VPE;

/// <summary>VPE-native implementation of the Taktikmagier spell effects.</summary>
public class AbilityExtension_MagierPsycast : AbilityExtension_AbilityMod
{
    public string effect;
    public float radius;
    public float amount;
    public float factor;
    public int ticks;
    public int points;
    public HediffDef hediff;
    public DamageDef damage;
    public ThingDef thingDef;
    public ThingDef costDef;
    public PawnKindDef pawnKindDef;
    public ThoughtDef thought;
    public float tendQualityMin = 0.4f;
    public float tendQualityMax = 0.8f;

    public override void Cast(GlobalTargetInfo[] targets, Ability ability)
    {
        base.Cast(targets, ability);
        Pawn caster = ability.pawn;
        GlobalTargetInfo target = targets.FirstOrDefault();
        Map map = caster.Map;

        switch (effect)
        {
            case "Flash": Flash(caster, map, target.Cell, ability.def.range); break;
            case "Repel": Repel(caster, map); break;
            case "Mark": Mark(caster, map, target.Cell); break;
            case "Swap": Swap(caster, target.Thing as Pawn); break;
            case "Stance": GiveHediff(caster, hediff); break;
            case "Smoke": Smoke(caster, map, target.Cell); break;
            case "ConstructTurret": ConstructTurret(caster, target); break;
            case "ConstructPawn": ConstructPawn(caster, target); break;
            case "FieldRepair": FieldRepair(caster, target.Thing); break;
            case "EmpBurst": Explosion(caster, damage, radius, amount); break;
            case "FieldAid": FieldAid(target.Thing as Pawn); break;
            case "Ward": GiveHediff(target.Thing as Pawn ?? caster, hediff); break;
            case "ForceWake": ForceWake(caster); break;
            case "Demolish": Demolish(DemolitionTarget(target, map)); break;
        }
    }

    public override bool Valid(GlobalTargetInfo[] targets, Ability ability, bool throwMessages = false)
    {
        Thing target = targets.Select(t => t.Thing).FirstOrDefault(t => t != null);
        IntVec3 cell = targets.Select(t => t.Cell).FirstOrDefault(c => c.IsValid);
        if (target == null && cell.IsValid && ability.pawn.Map != null
            && (effect == "ConstructTurret" || effect == "ConstructPawn"))
            target = cell.GetThingList(ability.pawn.Map).FirstOrDefault(t => t.def == costDef);
        switch (effect)
        {
            case "Swap":
                if (target is not Pawn pawn || pawn == ability.pawn) return false;
                break;
            case "ConstructTurret":
            case "ConstructPawn":
                if (target == null || target.Destroyed || target.def != costDef) return false;
                ThingDef constructDef = effect == "ConstructTurret" ? thingDef : pawnKindDef?.race;
                Pawn_PsychicEntropyTracker tracker = ability.pawn.psychicEntropy;
                int upkeep = Militarmagier.ConstructHeatExtension.HeatFor(constructDef);
                float castEntropy = ability.def.GetModExtension<AbilityExtension_Psycast>()
                    .GetEntropyUsedByPawn(ability.pawn);
                if (tracker != null && tracker.limitEntropyAmount && upkeep > 0
                    && tracker.EntropyValue + castEntropy + upkeep > tracker.MaxEntropy)
                    return false;
                break;
            case "FieldRepair":
                if (!NeedsRepair(target, ability.pawn)) return false;
                break;
            case "FieldAid":
                if (target is not Pawn patient || patient.GetStatValue(StatDefOf.PsychicSensitivity) < float.Epsilon) return false;
                break;
            case "Demolish":
                if (DemolitionTarget(targets.FirstOrDefault(), ability.pawn.Map) == null) return false;
                break;
        }
        return base.Valid(targets, ability, throwMessages);
    }

    private void Flash(Pawn caster, Map map, IntVec3 aim, float range)
    {
        if (map == null || caster.Position == aim) return;
        Vector3 origin = caster.Position.ToVector3Shifted().Yto0();
        Vector3 direction = (aim - caster.Position).ToVector3().Yto0().normalized;
        float halfWidth = Mathf.Atan((radius * 0.5f) / range) * Mathf.Rad2Deg;
        float stunDamage = ticks / 30f;
        int count = GenRadial.NumCellsInRadius(range);
        for (int i = 0; i < count; i++)
        {
            IntVec3 cell = caster.Position + GenRadial.RadialPattern[i];
            if (!cell.InBounds(map) || cell == caster.Position || !cell.InHorDistOf(caster.Position, range)) continue;
            Vector3 offset = cell.ToVector3Shifted().Yto0() - origin;
            if (Vector3.Angle(direction, offset) > halfWidth) continue;
            if (!GenSight.LineOfSight(caster.Position, cell, map)) continue;
            FleckMaker.ThrowMicroSparks(cell.ToVector3Shifted(), map);
            foreach (Pawn victim in cell.GetThingList(map).OfType<Pawn>().ToList())
                victim.TakeDamage(new DamageInfo(DamageDefOf.Stun, stunDamage, instigator: caster));
        }
    }

    private void Repel(Pawn caster, Map map)
    {
        if (map == null) return;
        if (damage != null)
            GenExplosion.DoExplosion(caster.Position, map, radius, damage, caster, damAmount: 0,
                ignoredThings: new List<Thing> { caster });
        List<Pawn> victims = GenRadial.RadialDistinctThingsAround(caster.Position, map, radius, true)
            .OfType<Pawn>().Where(p => p != caster && p.Spawned).ToList();
        foreach (Pawn victim in victims)
        {
            IntVec3 targetPos = victim.Position;
            if (targetPos != caster.Position)
            {
                Vector3 direction = (targetPos - caster.Position).ToVector3().normalized;
                IntVec3 destination = caster.Position + (direction * radius).ToIntVec3();
                IntVec3 end = targetPos;
                foreach (IntVec3 cell in GenSight.PointsOnLineOfSight(targetPos, destination))
                {
                    if (!cell.InBounds(map) || cell.Filled(map)) break;
                    end = cell;
                }
                if (end != targetPos)
                {
                    victim.Position = end;
                    victim.Notify_Teleported(true, false);
                }
            }
            victim.TakeDamage(new DamageInfo(DamageDefOf.Stun, ticks / 30f, instigator: caster));
        }
    }

    private void Mark(Pawn caster, Map map, IntVec3 cell)
    {
        if (map == null || hediff == null) return;
        FleckMaker.Static(cell, map, FleckDefOf.PsycastAreaEffect, radius);
        foreach (Pawn pawn in GenRadial.RadialDistinctThingsAround(cell, map, radius, true)
                     .OfType<Pawn>().Where(p => !p.Dead && p.HostileTo(caster)).ToList())
        {
            Hediff old = pawn.health.hediffSet.GetFirstHediffOfDef(hediff);
            if (old != null) pawn.health.RemoveHediff(old);
            pawn.health.AddHediff(hediff);
        }
    }

    private static void Swap(Pawn caster, Pawn other)
    {
        if (other == null || !other.Spawned || caster.Map == null || other == caster) return;
        IntVec3 destination = other.Position;
        IntVec3 origin = caster.Position;
        SkipUtility.SkipTo(caster, destination, caster.Map);
        other.Position = origin;
        other.Notify_Teleported(true, false);
    }

    private void GiveHediff(Pawn pawn, HediffDef def)
    {
        if (pawn?.health == null || def == null) return;
        HediffDef opposite = def == DefDatabase<HediffDef>.GetNamedSilentFail("DMST_QuickDraw")
            ? DefDatabase<HediffDef>.GetNamedSilentFail("DMST_SteadyAim")
            : def == DefDatabase<HediffDef>.GetNamedSilentFail("DMST_SteadyAim")
                ? DefDatabase<HediffDef>.GetNamedSilentFail("DMST_QuickDraw")
                : null;
        Hediff oldOpposite = opposite == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(opposite);
        if (oldOpposite != null) pawn.health.RemoveHediff(oldOpposite);
        Hediff old = pawn.health.hediffSet.GetFirstHediffOfDef(def);
        if (old != null) pawn.health.RemoveHediff(old);
        Hediff created = HediffMaker.MakeHediff(def, pawn);
        if (created.TryGetComp<HediffComp_Disappears>() is { } disappears) disappears.ticksToDisappear = ticks;
        pawn.health.AddHediff(created);
    }

    private void Smoke(Pawn caster, Map map, IntVec3 cell)
    {
        if (map != null && damage != null)
            GenExplosion.DoExplosion(cell, map, radius, damage, caster, postExplosionGasType: GasType.BlindSmoke);
    }

    private bool TryConsumeCost(GlobalTargetInfo targetInfo, Pawn caster, out IntVec3 position, out Map map)
    {
        position = IntVec3.Invalid;
        map = null;
        Thing target = targetInfo.Thing;
        if (target == null && targetInfo.Cell.IsValid && targetInfo.Map != null)
            target = targetInfo.Cell.GetThingList(targetInfo.Map).FirstOrDefault(t => t.def == costDef);
        if (target == null || target.Destroyed || target.def != costDef || target.Map == null) return false;
        position = target.Position;
        map = target.Map;
        if (!position.Standable(map)) return false;
        target.SplitOff(1).Destroy();
        return true;
    }

    private void ConstructTurret(Pawn caster, GlobalTargetInfo target)
    {
        if (thingDef == null || !TryConsumeCost(target, caster, out IntVec3 position, out Map map)) return;
        ThingDef stuff = thingDef.MadeFromStuff ? GenStuff.DefaultStuffFor(thingDef) : null;
        Thing construct = ThingMaker.MakeThing(thingDef, stuff);
        construct.SetFactionDirect(caster.Faction);
        CompMagierLink.LinkTo(construct, caster);
        GenSpawn.Spawn(construct, position, map);
        Militarmagier.Hediff_Focus.GetOrAdd(caster).AddHeatGiver(construct);
    }

    private void ConstructPawn(Pawn caster, GlobalTargetInfo target)
    {
        if (pawnKindDef == null || !TryConsumeCost(target, caster, out IntVec3 position, out Map map)) return;
        Pawn construct = PawnGenerator.GeneratePawn(pawnKindDef, caster.Faction);
        Type droneType = Type.GetType("Fortified.CompDrone, Fortified");
        object drone = droneType == null ? null : construct.AllComps.FirstOrDefault(droneType.IsInstanceOfType);
        MethodInfo setPlatform = droneType?.GetMethod("SetPlatform");
        if (drone != null && setPlatform != null) setPlatform.Invoke(drone, new object[] { caster });
        else Log.ErrorOnce("[Taktikmagier] " + construct.def.defName + " has no Fortified.CompProperties_Drone.", construct.def.shortHash ^ 0x5A17);
        CompMagierLink.LinkTo(construct, caster);
        GenSpawn.Spawn(construct, position, map);
        Militarmagier.Hediff_Focus.GetOrAdd(caster).AddHeatGiver(construct);
    }

    private bool NeedsRepair(Thing target, Pawn caster)
    {
        if (target == null || target.HostileTo(caster)) return false;
        if (target is Pawn pawn)
        {
            if (!pawn.RaceProps.IsMechanoid) return false;
            List<Hediff_Injury> injuries = new();
            pawn.health.hediffSet.GetHediffs(ref injuries);
            return injuries.Count > 0;
        }
        return target is Building && target.def.useHitPoints && target.HitPoints < target.MaxHitPoints;
    }

    private void FieldRepair(Pawn caster, Thing target)
    {
        if (target == null || !target.Spawned) return;
        if (target is Pawn pawn)
        {
            List<Hediff_Injury> injuries = new();
            pawn.health.hediffSet.GetHediffs(ref injuries);
            float remaining = amount;
            foreach (Hediff_Injury injury in injuries.OrderByDescending(h => h.Severity))
            {
                if (remaining <= 0f) break;
                float healed = Mathf.Min(remaining, injury.Severity);
                injury.Heal(healed);
                remaining -= healed;
            }
        }
        else
        {
            target.HitPoints = Mathf.Min(target.MaxHitPoints, target.HitPoints + Mathf.RoundToInt(amount));
            if (target is Building building) target.Map.listerBuildingsRepairable.Notify_BuildingRepaired(building);
        }
        FleckMaker.ThrowMicroSparks(target.DrawPos, target.Map);
    }

    private static void Explosion(Pawn caster, DamageDef damage, float radius, float amount)
    {
        if (caster.Map != null && damage != null)
            GenExplosion.DoExplosion(caster.Position, caster.Map, radius, damage, caster,
                damAmount: Mathf.RoundToInt(amount), ignoredThings: new List<Thing> { caster });
    }

    private void FieldAid(Pawn pawn)
    {
        if (pawn?.health == null) return;
        int tended = TendUtility.TendAllInjuries(pawn, new FloatRange(tendQualityMin, tendQualityMax));
        if (pawn.Map == null) return;
        if (tended > 0) MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "NumWoundsTended".Translate(tended), 3.65f);
        FleckMaker.AttachedOverlay(pawn, FleckDefOf.FlashHollow, Vector3.zero, 1.5f);
    }

    private void ForceWake(Pawn caster)
    {
        if (caster.needs?.rest != null) caster.needs.rest.CurLevel = caster.needs.rest.MaxLevel;
        if (thought != null) caster.needs?.mood?.thoughts?.memories?.TryGainMemory(thought);
    }

    private static Building DemolitionTarget(GlobalTargetInfo target, Map map)
    {
        if (target.Thing is Building building)
            return building.Spawned && building.def.destroyable ? building : null;
        return map != null && target.Cell.InBounds(map)
            ? target.Cell.GetThingList(map).OfType<Building>().FirstOrDefault(b => b.def.destroyable)
            : null;
    }

    private void Demolish(Building building)
    {
        if (building == null || !building.def.destroyable) return;
        if (building.HitPoints <= points) building.Destroy(DestroyMode.KillFinalize);
        else building.HitPoints = Mathf.Max(1, Mathf.RoundToInt(building.HitPoints * factor));
    }
}
