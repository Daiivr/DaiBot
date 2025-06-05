using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using static PKHeX.Core.RibbonIndex;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Helpers.ShowdownHelpers
{
    public class MarkHelper<T> where T : PKM, new()
    {
        public static Task<(string? MarkLine, List<string> CorrectionMessages)> CorrectMarks(PKM pk, IEncounterTemplate encounter, string[] lines, BattleTemplateLocalization inputLocalization, BattleTemplateLocalization targetLocalization)
        {
            List<string> correctionMessages = [];

            if (pk is not IRibbonIndex m)
            {
                correctionMessages.Add("- PKM no implementa IRibbonIndex Corrigiendo marcas.");
                return Task.FromResult<(string? MarkLine, List<string> CorrectionMessages)>((".Ribbons=$SuggestAll", correctionMessages));
            }

            string? existingMarkLine = lines.FirstOrDefault(line => line.StartsWith(".RibbonMark"));
            if (!string.IsNullOrEmpty(existingMarkLine))
            {
                string markName = existingMarkLine.Split('=')[0].Replace(".RibbonMark", string.Empty);

                var markIndex = TryParseMarkName(markName, inputLocalization);
                if (markIndex.HasValue)
                {
                    if (MarkRules.IsEncounterMarkValid(markIndex.Value, pk, encounter))
                    {
                        m.SetRibbon((int)markIndex.Value, true);
                        string localizedMarkLine = $".RibbonMark{GetLocalizedRibbonName(markIndex.Value, targetLocalization)}=True";
                        return Task.FromResult<(string? MarkLine, List<string> CorrectionMessages)>((localizedMarkLine, correctionMessages));
                    }
                }
            }

            if (MarkRules.IsEncounterMarkAllowed(encounter, pk))
            {
                for (var mark = MarkLunchtime; mark <= MarkSlump; mark++)
                {
                    if (MarkRules.IsEncounterMarkValid(mark, pk, encounter))
                    {
                        m.SetRibbon((int)mark, true);
                        string markLine = $".RibbonMark{GetLocalizedRibbonName(mark, targetLocalization)}=True";
                        return Task.FromResult<(string? MarkLine, List<string> CorrectionMessages)>((markLine, correctionMessages));
                    }
                }
            }

            correctionMessages.Add("- Corrigiendo marcas/cintas. Cambiando a **.Ribbons=$SuggestAll**");
            return Task.FromResult<(string? MarkLine, List<string> CorrectionMessages)>((".Ribbons=$SuggestAll", correctionMessages));
        }

        private static RibbonIndex? TryParseMarkName(string markName, BattleTemplateLocalization localization)
        {
            for (var mark = MarkLunchtime; mark <= MarkSlump; mark++)
            {
                var localizedName = GetLocalizedRibbonName(mark, localization);
                if (string.Equals(markName, localizedName, StringComparison.OrdinalIgnoreCase))
                {
                    return mark;
                }
            }

            if (Enum.TryParse($"Mark{markName}", out RibbonIndex markIndex))
            {
                return markIndex;
            }

            return null;
        }

        private static string GetLocalizedRibbonName(RibbonIndex index, BattleTemplateLocalization localization)
        {
            if (index >= MAX_COUNT)
                return index.ToString();

            var ribbonNames = GetRibbonNames(localization);
            var ribbonId = (int)index;

            if (ribbonId < ribbonNames.Length && !string.IsNullOrEmpty(ribbonNames[ribbonId]))
            {
                var ribbonName = ribbonNames[ribbonId];

                if (ribbonName.StartsWith("Ribbon"))
                    return ribbonName["Ribbon".Length..];
                if (ribbonName.StartsWith("Mark"))
                    return ribbonName["Mark".Length..];

                return ribbonName;
            }

            return GetRibbonNameFallback(index);
        }

        private static string[] GetRibbonNames(BattleTemplateLocalization localization)
        {
            var strings = localization.Strings;

            if (strings.ribbons?.Length > 0)
                return strings.ribbons;

            return GetDefaultRibbonNames();
        }

        private static string[] GetDefaultRibbonNames()
        {
            var defaultNames = new string[(int)MAX_COUNT];

            for (int i = 0; i < defaultNames.Length; i++)
            {
                var ribbonIndex = (RibbonIndex)i;
                defaultNames[i] = GetRibbonNameFallback(ribbonIndex);
            }

            return defaultNames;
        }

        private static string GetRibbonNameFallback(RibbonIndex index)
        {
            return index switch
            {
                MarkLunchtime => "Almuerzo",
                MarkSleepyTime => "Noche",
                MarkDusk => "Ocaso",
                MarkDawn => "Alba",
                MarkCloudy => "Nube",
                MarkRainy => "Lluvia",
                MarkStormy => "Tormenta",
                MarkSnowy => "Nieve",
                MarkBlizzard => "Nevasca",
                MarkDry => "Sequedad",
                MarkSandstorm => "Polvareda",
                MarkMisty => "Niebla",
                MarkDestiny => "Destino",
                MarkFishing => "Pesca",
                MarkCurry => "Curri",
                MarkUncommon => "Familiaridad",
                MarkRare => "Rareza",
                MarkRowdy => "Travesura",
                MarkAbsentMinded => "Despreocupación",
                MarkJittery => "Nerviosismo",
                MarkExcited => "Ilusión",
                MarkCharismatic => "Carisma",
                MarkCalmness => "Compostura",
                MarkIntense => "Pasión",
                MarkZonedOut => "Distracción",
                MarkJoyful => "Felicidad",
                MarkAngry => "Cólera",
                MarkSmiley => "Sonrisa",
                MarkTeary => "Llanto",
                MarkUpbeat => "Humor",
                MarkPeeved => "Mal Humor",
                MarkIntellectual => "Intelecto",
                MarkFerocious => "Impulsividad",
                MarkCrafty => "Astucia",
                MarkScowling => "Intimidación",
                MarkKindly => "Amabilidad",
                MarkFlustered => "Desconcierto",
                MarkPumpedUp => "Motivación",
                MarkZeroEnergy => "Desgana",
                MarkPrideful => "Confianza",
                MarkUnsure => "Inseguridad",
                MarkHumble => "Sencillez",
                MarkThorny => "Altanería",
                MarkVigor => "Vigor",
                MarkSlump => "Extenuación",
                _ => index.ToString().Replace("Mark", "").Replace("Ribbon", "")
            };
        }
    }
}
