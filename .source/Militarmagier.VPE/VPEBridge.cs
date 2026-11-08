using System.Collections.Generic;
using System.Linq;
using Militarmagier;
using RimWorld;
using VanillaPsycastsExpanded;
using VEF.Abilities;
using Verse;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace Militarmagier.VPE;

public static class VPEBridge
{
    public static bool ApplyPawnKit(Pawn pawn, MagierKindExtension kind)
    {
        CompAbilities abilities = pawn.GetComp<CompAbilities>();
        Hediff_PsycastAbilities psycasts = pawn.Psycasts();
        PsycasterPathDef path = DefDatabase<PsycasterPathDef>.GetNamed("DMST_VPE_MagierPath");
        if (abilities == null || psycasts == null) return false;

        if (!psycasts.unlockedPaths.Contains(path)) psycasts.UnlockPath(path);
        List<AbilityDef> candidates = DefDatabase<AbilityDef>.AllDefsListForReading
            .Where(def => def.GetModExtension<AbilityExtension_MagierPsycast>() != null
                && def.Psycast().level >= kind.spellLevelRange.min
                && def.Psycast().level <= kind.spellLevelRange.max
                && def.Psycast().level <= kind.psylinkLevel
                && !abilities.LearnedAbilities.Any(a => a.def == def))
            .ToList();
        foreach (AbilityDef ability in candidates.InRandomOrder().Take(kind.spellCountRange.RandomInRange))
            abilities.GiveAbility(ability);

        if (kind.manualChance > 0f && Rand.Chance(kind.manualChance))
            GiveManual(pawn, abilities);
        return true;
    }

    private static void GiveManual(Pawn pawn, CompAbilities abilities)
    {
        AbilityDef spell = abilities.LearnedAbilities.Select(a => a.def)
            .Where(def => def.GetModExtension<AbilityExtension_MagierPsycast>() != null)
            .RandomElementWithFallback();
        if (spell == null || pawn.inventory == null) return;

        Thing thing = ThingMaker.MakeThing(MilitarmagierDefOf.DMST_FieldManual);
        BookOutcomeDoer_MagierManual doer = (thing as Book)?.BookComp?.Doers
            .OfType<BookOutcomeDoer_MagierManual>().FirstOrDefault();
        CompQuality quality = thing.TryGetComp<CompQuality>();
        if (doer == null || quality == null)
        {
            thing.Destroy();
            return;
        }
        doer.SetSpell(spell);
        quality.SetQuality(QualityUtility.GenerateQualityTraderItem(), ArtGenerationContext.Outsider);
        pawn.inventory.innerContainer.TryAdd(thing);
    }
}

public class BookOutcomeProperties_MagierManual : BookOutcomeProperties
{
    public float readingToLearn = 20000f;
    public List<float> qualitySpeedFactors = new() { 0.6f, 0.8f, 1f, 1.15f, 1.3f, 1.45f, 1.6f };
    public override System.Type DoerClass => typeof(BookOutcomeDoer_MagierManual);
}

public class BookOutcomeDoer_MagierManual : BookOutcomeDoer
{
    private AbilityDef spell;
    private Dictionary<Pawn, float> progress = new();
    private List<Pawn> progressKeys;
    private List<float> progressValues;
    private AbilityDef fixedSpell;
    public new BookOutcomeProperties_MagierManual Props => (BookOutcomeProperties_MagierManual)props;

    public void SetSpell(AbilityDef ability)
    {
        spell = fixedSpell = ability;
        Reset();
    }

    public override bool DoesProvidesOutcome(Pawn reader) => CanLearn(reader);

    private bool CanLearn(Pawn reader)
    {
        CompAbilities abilities = reader?.GetComp<CompAbilities>();
        return spell != null && abilities != null && reader.Psycasts() != null
            && !abilities.LearnedAbilities.Any(a => a.def == spell)
            && reader.Psycasts().level >= spell.Psycast().level;
    }

    public override void OnBookGenerated(Pawn author = null)
    {
        spell ??= fixedSpell ?? DefDatabase<AbilityDef>.AllDefsListForReading
            .Where(def => def.GetModExtension<AbilityExtension_MagierPsycast>() != null).RandomElementWithFallback();
    }

    public override void OnReadingTick(Pawn reader, float factor)
    {
        if (!CanLearn(reader)) return;
        progress.TryGetValue(reader, out float done);
        done += factor * QualitySpeedFactor;
        if (done < Props.readingToLearn)
        {
            progress[reader] = done;
            return;
        }
        progress.Remove(reader);
        CompAbilities abilities = reader.GetComp<CompAbilities>();
        PsycasterPathDef path = spell.Psycast().path;
        if (!reader.Psycasts().unlockedPaths.Contains(path)) reader.Psycasts().UnlockPath(path);
        abilities.GiveAbility(spell);
        Messages.Message("DMST_ManualLearned".Translate(reader.Named("PAWN"), spell.label.Named("SPELL")),
            reader, MessageTypeDefOf.PositiveEvent);
    }

    private float QualitySpeedFactor
    {
        get
        {
            int index = (int)Quality;
            return index >= 0 && index < Props.qualitySpeedFactors.Count ? Props.qualitySpeedFactors[index] : 1f;
        }
    }

    public override string GetBenefitsString(Pawn reader = null)
    {
        if (spell == null) return null;
        float hours = Props.readingToLearn / QualitySpeedFactor / GenDate.TicksPerHour;
        return " - " + "DMST_ManualTeaches".Translate(spell.LabelCap.Named("SPELL"), spell.Psycast().level.Named("LEVEL"),
            hours.ToString("0.#").Named("HOURS"));
    }

    public override IEnumerable<Dialog_InfoCard.Hyperlink> GetHyperlinks()
    {
        if (spell != null) yield return new Dialog_InfoCard.Hyperlink(spell);
    }

    public override IEnumerable<Verse.Grammar.Rule_String> GetTopicRuleStrings()
    {
        if (spell != null)
        {
            yield return new Verse.Grammar.Rule_String("spell_label", spell.label);
            yield return new Verse.Grammar.Rule_String("spell_level", spell.Psycast().level.ToString());
        }
    }

    public override void Reset()
    {
        progress.Clear();
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Defs.Look(ref spell, "spell");
        Scribe_Defs.Look(ref fixedSpell, "fixedSpell");
        Scribe_Collections.Look(ref progress, "progress", LookMode.Reference, LookMode.Value, ref progressKeys, ref progressValues);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            progress ??= new Dictionary<Pawn, float>();
            progress.RemoveAll(pair => pair.Key == null);
        }
    }
}
