using System;
using System.Linq;
using Militarmagier;
using RimWorld;
using VanillaPsycastsExpanded;
using VEF.Abilities;
using Verse;
using Verse.AI;
using Ability = VEF.Abilities.Ability;

namespace Militarmagier.VPE;

public class JobGiver_TaktikmagierCombat : ThinkNode_JobGiver
{
    protected override Job TryGiveJob(Pawn pawn)
    {
        if (pawn?.Faction == null || pawn.Faction.IsPlayer || !pawn.Spawned || pawn.Downed
            || !pawn.IsHashIntervalTick(30) || pawn.psychicEntropy == null || pawn.Psycasts() == null)
            return null;

        CompAbilities comp = pawn.GetComp<CompAbilities>();
        Thing enemy = pawn.mindState?.enemyTarget;
        if (comp == null || comp.currentlyCasting != null || pawn.CurJob?.ability != null
            || enemy == null || !enemy.Spawned || enemy.Map != pawn.Map)
            return null;

        Job job;
        if (HostilesWithin(pawn, 4.9f) >= 2
            && (job = CastJob(comp, "Repel", pawn)) != null) return job;

        if (pawn.health.summaryHealth.SummaryHealthPercent < 0.6f
            && Find.TickManager.TicksGame - pawn.mindState.lastHarmTick < 120
            && (job = CastJob(comp, "Smoke", pawn.Position)) != null) return job;

        job = SupportJob(comp, "Ward", p => p.health.summaryHealth.SummaryHealthPercent < 0.5f
            && !p.health.hediffSet.HasHediff(MilitarmagierDefOf.DMST_Ward));
        if (job != null) return job;

        if (HostilesWithin(pawn, 14.9f, true) >= 2
            && (job = CastJob(comp, "EmpBurst", pawn)) != null) return job;

        if ((job = CastJob(comp, "Flash", enemy.Position)) != null) return job;

        job = SupportJob(comp, "FieldRepair", p => p.RaceProps.IsMechanoid
            && p.health.summaryHealth.SummaryHealthPercent < 0.5f);
        if (job != null) return job;

        if (enemy is Pawn enemyPawn && !enemyPawn.health.hediffSet.HasHediff(MilitarmagierDefOf.DMST_Marked)
            && (job = CastJob(comp, "Mark", enemy.Position)) != null) return job;

        Pawn_PsychicEntropyTracker entropy = pawn.psychicEntropy;
        if (pawn.equipment?.Primary == null || pawn.equipment.Primary.def.IsMeleeWeapon
            || entropy.MaxEntropy <= 0f || entropy.EntropyValue / entropy.MaxEntropy >= 0.5f)
            return null;

        if (pawn.Position.InHorDistOf(enemy.Position, 9.9f)
            && !pawn.health.hediffSet.HasHediff(MilitarmagierDefOf.DMST_QuickDraw))
            return CastJob(comp, "QuickDraw", pawn);

        if (!pawn.Position.InHorDistOf(enemy.Position, 17.9f)
            && !pawn.health.hediffSet.HasHediff(MilitarmagierDefOf.DMST_SteadyAim))
            return CastJob(comp, "SteadyAim", pawn);
        return null;
    }

    private static Job SupportJob(CompAbilities comp, string spell, Func<Pawn, bool> wanted)
    {
        Pawn caster = (Pawn)comp.parent;
        foreach (Pawn ally in caster.Map.mapPawns.SpawnedPawnsInFaction(caster.Faction))
        {
            if (ally.Dead || !wanted(ally)) continue;
            Job job = CastJob(comp, spell, ally);
            if (job != null) return job;
        }
        return null;
    }

    private static Job CastJob(CompAbilities comp, string spell, LocalTargetInfo target)
    {
        Ability ability = comp.LearnedAbilities.FirstOrDefault(a => a.def.defName == "DMST_VPE_" + spell);
        if (ability == null || !ability.IsEnabledForPawn(out _)
            || ability.def.Psycast().level > ability.pawn.Psycasts().level
            || !ability.CanHitTarget(target) || !ability.ValidateTarget(target, false))
            return null;

        var targets = new[] { target.ToGlobalTargetInfo(ability.pawn.Map) };
        foreach (AbilityExtension_AbilityMod extension in ability.AbilityModExtensions)
            if (!extension.Valid(targets, ability, false)) return null;

        // Match VEF's StartAbilityJob setup; the think tree starts the returned job.
        Job job = JobMaker.MakeJob(ability.def.jobDef ?? DefDatabase<JobDef>.GetNamed("VFEA_UseAbility"), target);
        ability.ModifyTargets(ref targets);
        comp.currentlyCasting = ability;
        comp.currentlyCastingTargets = targets;
        return job;
    }

    private static int HostilesWithin(Pawn pawn, float radius, bool mechsOnly = false)
    {
        return pawn.Map.mapPawns.AllPawnsSpawned.Count(other => other != pawn && !other.Dead
            && !other.Downed && other.HostileTo(pawn) && (!mechsOnly || other.RaceProps.IsMechanoid)
            && pawn.Position.InHorDistOf(other.Position, radius));
    }
}
