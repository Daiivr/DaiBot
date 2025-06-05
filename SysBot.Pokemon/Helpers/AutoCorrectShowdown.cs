using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SysBot.Pokemon.Helpers.ShowdownHelpers;
using SysBot.Base;
using static AbilityTranslationDictionary;
using static NatureTranslations;
using static MovesTranslationDictionary;

namespace SysBot.Pokemon;

public static class AutoCorrectShowdown<T> where T : PKM, new()
{
    private static readonly char[] separator = ['\r', '\n'];

    public static async Task<(string CorrectedContent, List<string> CorrectionMessages)> PerformAutoCorrect(string content, TradeSettings.AutoCorrectShowdownCategory autoCorrectConfig, PKM? originalPk = null, LegalityAnalysis? originalLa = null, string? targetLanguage = null)
    {
        return await Task.Run(async () =>
        {
            LogUtil.LogInfo("[AutoCorrect] Iniciando proceso de autocorrección", "AutoCorrect.Debug");
            LogUtil.LogInfo($"[AutoCorrect] Contenido de entrada:\n{content}", "AutoCorrect.Debug");

            if (!autoCorrectConfig.EnableAutoCorrect)
            {
                LogUtil.LogInfo("[AutoCorrect] AutoCorrect está deshabilitado en la configuración", "AutoCorrect.Debug");
                return (content, new List<string>());
            }

            LogUtil.LogInfo("[AutoCorrect] Detectando el idioma de entrada...", "AutoCorrect.Debug");
            var (inputLocalization, detectedLanguage) = DetectInputLanguage(content);
            if (inputLocalization == null)
            {
                LogUtil.LogInfo("[AutoCorrect] No se detectó ningún idioma, usando el predeterminado", "AutoCorrect.Debug");
                inputLocalization = BattleTemplateLocalization.Default;
                detectedLanguage = BattleTemplateLocalization.DefaultLanguage;
            }
            else
            {
                LogUtil.LogInfo($"[AutoCorrect] Idioma detectado: {detectedLanguage}", "AutoCorrect.Debug");
            }

            var targetLocalization = string.IsNullOrEmpty(targetLanguage) || targetLanguage == detectedLanguage
                ? inputLocalization
                : BattleTemplateLocalization.GetLocalization(targetLanguage);

            LogUtil.LogInfo($"[AutoCorrect] Idioma objetivo: {targetLanguage ?? detectedLanguage}", "AutoCorrect.Debug");

            PKM pk;
            LegalityAnalysis la;

            if (originalPk == null || originalLa == null)
            {
                LogUtil.LogInfo("[AutoCorrect] No se proporcionó PKM/LA original, creando a partir del contenido", "AutoCorrect.Debug");
                try
                {
                    var set = new ShowdownSet(content, inputLocalization);
                    LogUtil.LogInfo($"[AutoCorrect] ShowdownSet inicial creado - Especie: {set.Species}, Forma: {set.Form}", "AutoCorrect.Debug");

                    var template = AutoLegalityWrapper.GetTemplate(set);
                    var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                    pk = sav.GetLegal(template, out var result);

                    if (pk == null)
                    {
                        LogUtil.LogInfo("[AutoCorrect] GetLegal devolvió null, creando PKM vacío", "AutoCorrect.Debug");
                        pk = (PKM)EntityBlank.GetBlank(typeof(T));
                    }
                    else
                    {
                        LogUtil.LogInfo($"[AutoCorrect] PKM creado - Especie: {pk.Species}, Forma: {pk.Form}", "AutoCorrect.Debug");
                    }
                    la = new LegalityAnalysis(pk);
                    LogUtil.LogInfo($"[AutoCorrect] Legalidad inicial: {la.Valid}", "AutoCorrect.Debug");
                }
                catch (Exception ex)
                {
                    LogUtil.LogInfo($"[AutoCorrect] Falló el análisis inicial: {ex.Message}", "AutoCorrect.Debug");
                    LogUtil.LogInfo("[AutoCorrect] Intentando pre-corrección...", "AutoCorrect.Debug");

                    var preCorrectedContent = await PreCorrectShowdownText(content, inputLocalization, targetLocalization);
                    LogUtil.LogInfo($"[AutoCorrect] Contenido precorregido:\n{preCorrectedContent}", "AutoCorrect.Debug");

                    try
                    {
                        var correctedSet = new ShowdownSet(preCorrectedContent, inputLocalization);
                        var correctedTemplate = AutoLegalityWrapper.GetTemplate(correctedSet);
                        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                        pk = sav.GetLegal(correctedTemplate, out var correctedResult);

                        if (pk == null)
                        {
                            pk = (PKM)EntityBlank.GetBlank(typeof(T));
                        }
                        la = new LegalityAnalysis(pk);
                        LogUtil.LogInfo($"[AutoCorrect] Pre-corrección exitosa - Especie: {pk.Species}, Válido: {la.Valid}", "AutoCorrect.Debug");
                    }
                    catch (Exception ex2)
                    {
                        LogUtil.LogInfo($"[AutoCorrect] La pre-corrección también falló: {ex2.Message}", "AutoCorrect.Debug");
                        pk = (PKM)EntityBlank.GetBlank(typeof(T));
                        la = new LegalityAnalysis(pk);

                        var contentLines = content.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                        if (contentLines.Length > 0)
                        {
                            var (detectedSpeciesName, _, _, _, _) = ParseSpeciesLine(contentLines[0], inputLocalization);
                            LogUtil.LogInfo($"[AutoCorrect] Intentando detectar especie desde la primera línea: {detectedSpeciesName}", "AutoCorrect.Debug");

                            var detectedSpeciesIndex = Array.FindIndex(inputLocalization.Strings.specieslist, s => s.Equals(detectedSpeciesName, StringComparison.OrdinalIgnoreCase));
                            if (detectedSpeciesIndex > 0)
                            {
                                pk.Species = (ushort)detectedSpeciesIndex;
                                la = new LegalityAnalysis(pk);
                                LogUtil.LogInfo($"[AutoCorrect] Especie detectada: {detectedSpeciesIndex}", "AutoCorrect.Debug");
                            }
                        }
                    }
                }
            }
            else
            {
                pk = originalPk;
                la = originalLa;
                LogUtil.LogInfo($"[AutoCorrect] Usando PKM proporcionado - Especie: {pk.Species}, Forma: {pk.Form}, Válido: {la.Valid}", "AutoCorrect.Debug");
            }

            string[] lines = content.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            var generation = GameInfoHelpers<T>.GetGeneration();
            LogUtil.LogInfo($"[AutoCorrect] Generación: {generation}", "AutoCorrect.Debug");

            var (speciesName, formName, gender, heldItem, nickname) = ParseSpeciesLine(lines[0], inputLocalization);
            LogUtil.LogInfo($"[AutoCorrect] Primera línea analizada - Especie: {speciesName}, Forma: {formName}, Género: {gender}, Objeto: {heldItem}, Apodo: {nickname}", "AutoCorrect.Debug");

            string originalSpeciesName = speciesName;
            string originalFormName = formName;

            string correctedSpeciesName = speciesName;
            if (autoCorrectConfig.AutoCorrectSpeciesAndForm)
            {
                LogUtil.LogInfo("[AutoCorrect] Autocorrigiendo especie y forma...", "AutoCorrect.Debug");
                var closestSpecies = await FirstLine<T>.GetClosestSpecies(speciesName, inputLocalization, targetLocalization);

                if (closestSpecies != null && closestSpecies != speciesName)
                {
                    LogUtil.LogInfo($"[AutoCorrect] Corrección de especie encontrada: {speciesName} -> {closestSpecies}", "AutoCorrect.Debug");
                }
                correctedSpeciesName = closestSpecies ?? speciesName;
            }

            ushort speciesIndex = (ushort)Array.IndexOf(targetLocalization.Strings.specieslist, correctedSpeciesName);
            LogUtil.LogInfo($"[AutoCorrect] Índice de especie: {speciesIndex}", "AutoCorrect.Debug");

            if (speciesIndex == 0 && correctedSpeciesName != targetLocalization.Strings.specieslist[0])
            {
                LogUtil.LogInfo("[AutoCorrect] Especie no encontrada en el idioma objetivo, intentando índice en idioma de entrada", "AutoCorrect.Debug");
                var inputSpeciesIndex = (ushort)Array.IndexOf(inputLocalization.Strings.specieslist, correctedSpeciesName);
                if (inputSpeciesIndex > 0 && inputSpeciesIndex < targetLocalization.Strings.specieslist.Length)
                {
                    correctedSpeciesName = targetLocalization.Strings.specieslist[inputSpeciesIndex];
                    speciesIndex = inputSpeciesIndex;
                    LogUtil.LogInfo($"[AutoCorrect] Especie encontrada mediante idioma de entrada: {correctedSpeciesName} (índice: {speciesIndex})", "AutoCorrect.Debug");
                }
            }

            string[] formNames = Array.Empty<string>();
            string correctedFormName = formName;
            if (!string.IsNullOrEmpty(formName))
            {
                formNames = FormConverter.GetFormList(speciesIndex, targetLocalization.Strings.Types, targetLocalization.Strings.forms, new List<string>(), generation);
                LogUtil.LogInfo($"[AutoCorrect] Formas disponibles para la especie {speciesIndex}: {string.Join(", ", formNames)}", "AutoCorrect.Debug");

                var closestFormName = await FirstLine<T>.GetClosestFormName(formName, formNames, inputLocalization, targetLocalization);
                if (closestFormName != null && closestFormName != formName)
                {
                    LogUtil.LogInfo($"[AutoCorrect] Corrección de forma encontrada: {formName} -> {closestFormName}", "AutoCorrect.Debug");
                }
                correctedFormName = closestFormName ?? formName;
            }

            string finalCorrectedName = string.IsNullOrEmpty(correctedFormName) ? correctedSpeciesName : $"{correctedSpeciesName}-{correctedFormName}";
            speciesName = finalCorrectedName;

            PKM workingPk = pk.Clone();
            LegalityAnalysis workingLa = la;

            var correctionMessages = new List<string>();

            bool speciesOrFormCorrected = finalCorrectedName != $"{originalSpeciesName}-{originalFormName}".Trim('-');
            if (speciesOrFormCorrected)
            {
                LogUtil.LogInfo($"[AutoCorrect] Especie/Forma fue corregida: {originalSpeciesName}-{originalFormName} -> {finalCorrectedName}", "AutoCorrect.Debug");
                workingPk.Species = speciesIndex;
                var personalFormInfo = GameInfoHelpers<T>.GetPersonalFormInfo(speciesIndex);
                workingPk.Form = !string.IsNullOrEmpty(correctedFormName) ? (byte)personalFormInfo.FormIndex(speciesIndex, (byte)Array.IndexOf(formNames, correctedFormName)) : (byte)0;
                workingLa = new LegalityAnalysis(workingPk);
                LogUtil.LogInfo($"[AutoCorrect] PKM en proceso actualizado - Especie: {workingPk.Species}, Forma: {workingPk.Form}, Válido: {workingLa.Valid}", "AutoCorrect.Debug");

                if (correctedSpeciesName != originalSpeciesName)
                {
                    correctionMessages.Add($"- La especie era incorrecta. Ajustado desde **{originalSpeciesName}** a **{correctedSpeciesName}**.");
                }
                if (correctedFormName != originalFormName)
                {
                    correctionMessages.Add($"- La forma era incorrecta. Ajustado desde **{originalFormName}** a **{correctedFormName}**.");
                }
                if (correctedSpeciesName == originalSpeciesName && correctedFormName == originalFormName)
                {
                    correctionMessages.Add($"- La especie o la forma eran incorrectas. Ajustado a **{finalCorrectedName}**.");
                }
            }

            var (abilityName, natureName, ballName) = ParseLines(lines, inputLocalization);
            LogUtil.LogInfo($"[AutoCorrect] Líneas analizadas - Habilidad: {abilityName}, Naturaleza: {natureName}, Bola: {ballName}", "AutoCorrect.Debug");

            string correctedAbilityName = string.Empty;
            if (!string.IsNullOrEmpty(abilityName) && autoCorrectConfig.AutoCorrectAbility)
            {
                LogUtil.LogInfo("[AutoCorrect] Autocorrigiendo habilidad...", "AutoCorrect.Debug");
                var personalAbilityInfo = GameInfoHelpers<T>.GetPersonalInfo(speciesIndex);
                var (closestAbility, _) = await AbilityHelper<T>.GetClosestAbility(abilityName, speciesIndex, inputLocalization, targetLocalization, personalAbilityInfo);
                correctedAbilityName = closestAbility ?? abilityName;

                if (!string.IsNullOrEmpty(correctedAbilityName) && correctedAbilityName != abilityName)
                {
                    string translatedAbilityName = AbilityTranslation.ContainsKey(correctedAbilityName) ? AbilityTranslation[correctedAbilityName] : correctedAbilityName;
                    string translatedAbilityOriginal = AbilityTranslation.ContainsKey(abilityName) ? AbilityTranslation[abilityName] : abilityName;
                    LogUtil.LogInfo($"[AutoCorrect] Corrección de habilidad: {abilityName} -> {correctedAbilityName}", "AutoCorrect.Debug");
                    correctionMessages.Add($"- {speciesName} no puede tener la habilidad {translatedAbilityOriginal}. Se ha ajustado a **{translatedAbilityName}**.");
                }
            }

            string correctedNatureName = string.Empty;
            if (!string.IsNullOrEmpty(natureName) && autoCorrectConfig.AutoCorrectNature)
            {
                LogUtil.LogInfo("[AutoCorrect] Autocorrigiendo naturaleza...", "AutoCorrect.Debug");
                var (closestNature, _) = await NatureHelper<T>.GetClosestNature(natureName, inputLocalization, targetLocalization);
                correctedNatureName = closestNature ?? natureName;

                if (!string.IsNullOrEmpty(correctedNatureName) && correctedNatureName != natureName)
                {
                    string translatedNatureName = TraduccionesNaturalezas.ContainsKey(correctedNatureName) ? TraduccionesNaturalezas[correctedNatureName] : correctedNatureName;
                    LogUtil.LogInfo($"[AutoCorrect] Corrección de naturaleza: {natureName} -> {correctedNatureName}", "AutoCorrect.Debug");
                    correctionMessages.Add($"- La naturaleza estaba incorrecto. Se ha ajustado a naturaleza **{translatedNatureName}**.");
                }
            }

            string formNameForBallVerification = correctedSpeciesName == speciesName ? formName : correctedFormName;
            string correctedBallName = string.Empty;
            if (!string.IsNullOrEmpty(ballName) && autoCorrectConfig.AutoCorrectBall)
            {
                LogUtil.LogInfo("[AutoCorrect] Autocorrigiendo bola...", "AutoCorrect.Debug");
                var legalBall = await BallHelper<T>.GetLegalBall(speciesIndex, formNameForBallVerification, ballName, inputLocalization, targetLocalization, workingPk);
                correctedBallName = legalBall;

                if (!string.IsNullOrEmpty(correctedBallName) && correctedBallName != ballName)
                {
                    LogUtil.LogInfo($"[AutoCorrect] Corrección de bola: {ballName} -> {correctedBallName}", "AutoCorrect.Debug");
                    correctionMessages.Add($"- {speciesName} no puede estar en la ball: {ballName}. Se ha ajustado a: **{correctedBallName}**.");
                }
            }

            if (autoCorrectConfig.AutoCorrectMovesLearnset && lines.Any(line => line.StartsWith("- ")))
            {
                LogUtil.LogInfo("[AutoCorrect] Validando movimientos...", "AutoCorrect.Debug");
                var movesBeforeValidation = lines.Where(l => l.StartsWith("- ")).Select(l => l[2..].Trim()).ToList();
                LogUtil.LogInfo($"[AutoCorrect] Movimientos antes de la validación: {string.Join(", ", movesBeforeValidation)}", "AutoCorrect.Debug");

                await MoveHelper<T>.ValidateMovesAsync(lines, workingPk, workingLa, inputLocalization, targetLocalization, speciesName, correctedFormName ?? formName, correctionMessages);

                var movesAfterValidation = lines.Where(l => l.StartsWith("- ")).Select(l => l[2..].Trim()).ToList();
                LogUtil.LogInfo($"[AutoCorrect] Movimientos después de la validación: {string.Join(", ", movesAfterValidation)}", "AutoCorrect.Debug");
            }

            if (autoCorrectConfig.AutoCorrectIVs && lines.Any(line => inputLocalization.Config.TryParse(line, out _) == BattleTemplateToken.IVs))
            {
                LogUtil.LogInfo("[AutoCorrect] Validando IVs...", "AutoCorrect.Debug");
                if (!IVEVHelper<T>.ValidateAndTranslateIVs(workingPk, workingLa, lines, inputLocalization, targetLocalization))
                {
                    LogUtil.LogInfo("[AutoCorrect] Validación de IV fallida, eliminando línea de IV", "AutoCorrect.Debug");
                    IVEVHelper<T>.RemoveIVLine(lines, inputLocalization);
                }
            }

            if (autoCorrectConfig.AutoCorrectEVs && lines.Any(line => inputLocalization.Config.TryParse(line, out _) == BattleTemplateToken.EVs))
            {
                LogUtil.LogInfo("[AutoCorrect] Validando EVs...", "AutoCorrect.Debug");
                if (!IVEVHelper<T>.ValidateAndTranslateEVs(lines, inputLocalization, targetLocalization))
                {
                    LogUtil.LogInfo("[AutoCorrect] Validación de EV fallida, eliminando línea de EV", "AutoCorrect.Debug");
                    IVEVHelper<T>.RemoveEVLine(lines, inputLocalization);
                }
            }

            string? markLine = null;
            List<string> markCorrectionMessages = new List<string>();
            if (autoCorrectConfig.AutoCorrectMarks && lines.Any(line => line.StartsWith(".RibbonMark")))
            {
                LogUtil.LogInfo("[AutoCorrect] Validando marcas...", "AutoCorrect.Debug");
                var markVerifier = new MarkVerifier();
                markVerifier.Verify(workingLa);
                if (!workingLa.Valid)
                {
                    LogUtil.LogInfo("[AutoCorrect] Validación de marcas fallida, corrigiendo marcas", "AutoCorrect.Debug");
                    (markLine, markCorrectionMessages) = await MarkHelper<T>.CorrectMarks(workingPk, workingLa.EncounterOriginal, lines, inputLocalization, targetLocalization);
                }
            }

            correctionMessages.AddRange(markCorrectionMessages);

            var (correctedHeldItem, heldItemCorrectionMessage) = FirstLine<T>.ValidateHeldItem(lines, workingPk, inputLocalization, targetLocalization, heldItem);

            if (!string.IsNullOrEmpty(heldItemCorrectionMessage))
            {
                LogUtil.LogInfo($"[AutoCorrect] Corrección de objeto equipado: {heldItemCorrectionMessage}", "AutoCorrect.Debug");
                correctionMessages.Add(heldItemCorrectionMessage);
            }

            string correctedGender = gender;
            string genderCorrectionMessage = string.Empty;
            if (autoCorrectConfig.AutoCorrectGender && !string.IsNullOrEmpty(gender))
            {
                LogUtil.LogInfo("[AutoCorrect] Validando género...", "AutoCorrect.Debug");
                (correctedGender, genderCorrectionMessage) = FirstLine<T>.ValidateGender(workingPk, gender, speciesName, inputLocalization, targetLocalization);
                if (!string.IsNullOrEmpty(genderCorrectionMessage))
                {
                    LogUtil.LogInfo($"[AutoCorrect] Corrección de género: {genderCorrectionMessage}", "AutoCorrect.Debug");
                    correctionMessages.Add(genderCorrectionMessage);
                }
            }

            string[] correctedLines = [.. lines.Select((line, i) => CorrectLine(line, i, speciesName, correctedSpeciesName, correctedFormName ?? formName, formName, correctedGender, gender, correctedHeldItem, heldItem, correctedAbilityName, correctedNatureName, correctedBallName, workingLa, nickname, targetLocalization))];

            int moveSetIndex = Array.FindIndex(correctedLines, line => line.StartsWith("- "));

            if (!string.IsNullOrEmpty(markLine))
            {
                if (moveSetIndex != -1)
                {
                    var updatedLines = new List<string>(correctedLines);
                    updatedLines.Insert(moveSetIndex, markLine);
                    correctedLines = [.. updatedLines];
                }
                else
                {
                    int invalidMarkIndex = Array.FindIndex(correctedLines, line => line.StartsWith(".RibbonMark"));
                    if (invalidMarkIndex != -1)
                        correctedLines[invalidMarkIndex] = markLine;
                    else
                        correctedLines = [.. correctedLines, markLine];
                }
            }

            string finalShowdownSet = string.Join(Environment.NewLine, correctedLines.Where(line => !string.IsNullOrWhiteSpace(line)));

            LogUtil.LogInfo($"[AutoCorrect] Set final de Showdown:\n{finalShowdownSet}", "AutoCorrect.Debug");
            LogUtil.LogInfo($"[AutoCorrect] Total de correcciones realizadas: {correctionMessages.Count}", "AutoCorrect.Debug");
            if (correctionMessages.Count > 0)
            {
                LogUtil.LogInfo($"[AutoCorrect] Correcciones:\n{string.Join("\n", correctionMessages)}", "AutoCorrect.Debug");
            }

            return (finalShowdownSet, correctionMessages);
        });
    }

    public static async Task<(string CorrectedContent, List<string> CorrectionMessages)> PerformAutoCorrect(string content, TradeSettings.AutoCorrectShowdownCategory autoCorrectConfig)
    {
        return await PerformAutoCorrect(content, autoCorrectConfig, null, null, null);
    }

    public static async Task<(string CorrectedContent, List<string> CorrectionMessages)> PerformAutoCorrect(string content, TradeSettings.AutoCorrectShowdownCategory autoCorrectConfig, string? targetLanguage)
    {
        return await PerformAutoCorrect(content, autoCorrectConfig, null, null, targetLanguage);
    }

    private static async Task<string> PreCorrectShowdownText(string content, BattleTemplateLocalization inputLocalization, BattleTemplateLocalization targetLocalization)
    {
        LogUtil.LogInfo("[AutoCorrect.PreCorrect] Iniciando proceso de pre-corrección", "AutoCorrect.Debug");
        var contentLines = content.Split(separator, StringSplitOptions.RemoveEmptyEntries).ToList();

        for (int i = 0; i < contentLines.Count; i++)
        {
            var currentLine = contentLines[i];
            if (currentLine.StartsWith("- "))
            {
                var moveNameToCorrect = currentLine[2..].Trim();
                LogUtil.LogInfo($"[AutoCorrect.PreCorrect] Verificando movimiento: {moveNameToCorrect}", "AutoCorrect.Debug");

                var correctedMoveResult = await PreCorrectMove(moveNameToCorrect, inputLocalization, targetLocalization);
                if (correctedMoveResult != null && correctedMoveResult != moveNameToCorrect)
                {
                    LogUtil.LogInfo($"[AutoCorrect.PreCorrect] Movimiento precorregido: {moveNameToCorrect} -> {correctedMoveResult}", "AutoCorrect.Debug");
                    contentLines[i] = $"- {correctedMoveResult}";
                }
            }
        }

        return string.Join(Environment.NewLine, contentLines);
    }

    private static async Task<string?> PreCorrectMove(string moveNameInput, BattleTemplateLocalization inputLocalization, BattleTemplateLocalization targetLocalization)
    {
        return await Task.Run(() =>
        {
            LogUtil.LogInfo($"[AutoCorrect.PreCorrect.Move] Intentando corregir movimiento: {moveNameInput}", "AutoCorrect.Debug");

            var inputMoveIndex = Array.FindIndex(inputLocalization.Strings.movelist, moveStr => moveStr.Equals(moveNameInput, StringComparison.OrdinalIgnoreCase));
            if (inputMoveIndex >= 0 && inputMoveIndex < targetLocalization.Strings.movelist.Length)
            {
                var translatedMoveResult = targetLocalization.Strings.movelist[inputMoveIndex];
                if (!string.IsNullOrEmpty(translatedMoveResult))
                {
                    LogUtil.LogInfo($"[AutoCorrect.PreCorrect.Move] Coincidencia exacta encontrada: {moveNameInput} -> {translatedMoveResult}", "AutoCorrect.Debug");
                    return translatedMoveResult;
                }
            }

            LogUtil.LogInfo("[AutoCorrect.PreCorrect.Move] No se encontró coincidencia exacta, intentando coincidencia difusa", "AutoCorrect.Debug");

            var inputFuzzyMoveResult = inputLocalization.Strings.movelist
                .Select((moveEntry, index) => new { Move = moveEntry, Index = index, Distance = string.IsNullOrEmpty(moveEntry) ? 0 : FuzzySharp.Fuzz.Ratio(moveNameInput.ToLowerInvariant(), moveEntry.ToLowerInvariant()) })
                .Where(moveData => !string.IsNullOrEmpty(moveData.Move))
                .OrderByDescending(moveData => moveData.Distance)
                .FirstOrDefault();

            if (inputFuzzyMoveResult != null)
            {
                LogUtil.LogInfo($"[AutoCorrect.PreCorrect.Move] Mejor coincidencia difusa en el idioma de entrada: {inputFuzzyMoveResult.Move} (puntuación: {inputFuzzyMoveResult.Distance})", "AutoCorrect.Debug");

                if (inputFuzzyMoveResult.Distance >= 70)
                {
                    if (inputFuzzyMoveResult.Index < targetLocalization.Strings.movelist.Length)
                    {
                        var translatedFuzzyMove = targetLocalization.Strings.movelist[inputFuzzyMoveResult.Index];
                        if (!string.IsNullOrEmpty(translatedFuzzyMove))
                        {
                            LogUtil.LogInfo($"[AutoCorrect.PreCorrect.Move] Coincidencia difusa aceptada: {moveNameInput} -> {translatedFuzzyMove}", "AutoCorrect.Debug");
                            return translatedFuzzyMove;
                        }
                    }
                }
            }

            var targetFuzzyMoveResult = targetLocalization.Strings.movelist
                .Where(targetMoveEntry => !string.IsNullOrEmpty(targetMoveEntry))
                .Select(targetMoveEntry => new { Move = targetMoveEntry, Distance = FuzzySharp.Fuzz.Ratio(moveNameInput.ToLowerInvariant(), targetMoveEntry.ToLowerInvariant()) })
                .OrderByDescending(targetMoveData => targetMoveData.Distance)
                .FirstOrDefault();

            if (targetFuzzyMoveResult != null)
            {
                LogUtil.LogInfo($"[AutoCorrect.PreCorrect.Move] Mejor coincidencia difusa en el idioma objetivo: {targetFuzzyMoveResult.Move} (puntuación: {targetFuzzyMoveResult.Distance})", "AutoCorrect.Debug");

                if (targetFuzzyMoveResult.Distance >= 70)
                {
                    LogUtil.LogInfo($"[AutoCorrect.PreCorrect.Move] Coincidencia difusa en el idioma objetivo aceptada: {moveNameInput} -> {targetFuzzyMoveResult.Move}", "AutoCorrect.Debug");
                    return targetFuzzyMoveResult.Move;
                }
            }

            LogUtil.LogInfo($"[AutoCorrect.PreCorrect.Move] No se encontró una corrección adecuada para: {moveNameInput}", "AutoCorrect.Debug");
            return null;
        });
    }

    private static (BattleTemplateLocalization? localization, string language) DetectInputLanguage(string content)
    {
        LogUtil.LogInfo("[AutoCorrect.DetectLanguage] Iniciando detección de idioma", "AutoCorrect.Debug");

        if (string.IsNullOrEmpty(content))
        {
            LogUtil.LogInfo("[AutoCorrect.DetectLanguage] El contenido está vacío", "AutoCorrect.Debug");
            return (null, string.Empty);
        }

        var invalid = int.MaxValue;
        BattleTemplateLocalization? bestLocalization = null;
        string bestLanguage = string.Empty;

        var all = BattleTemplateLocalization.GetAll();
        LogUtil.LogInfo($"[AutoCorrect.DetectLanguage] Verificando {all.Count} idiomas", "AutoCorrect.Debug");

        foreach (var lang in all)
        {
            var local = lang.Value;
            try
            {
                var tmp = new ShowdownSet(content, local);
                var bad = tmp.InvalidLines.Count;
                LogUtil.LogInfo($"[AutoCorrect.DetectLanguage] Idioma {lang.Key} - Líneas inválidas: {bad}", "AutoCorrect.Debug");

                if (bad == 0)
                {
                    LogUtil.LogInfo($"[AutoCorrect.DetectLanguage] Coincidencia perfecta encontrada: {lang.Key}", "AutoCorrect.Debug");
                    return (local, lang.Key);
                }

                if (bad < invalid)
                {
                    invalid = bad;
                    bestLocalization = local;
                    bestLanguage = lang.Key;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo($"[AutoCorrect.DetectLanguage] Error al verificar el idioma {lang.Key}: {ex.Message}", "AutoCorrect.Debug");
                continue;
            }
        }

        LogUtil.LogInfo($"[AutoCorrect.DetectLanguage] Mejor coincidencia: {bestLanguage} con {invalid} líneas inválidas", "AutoCorrect.Debug");
        return (bestLocalization, bestLanguage);
    }

    private static (string speciesName, string formName, string gender, string heldItem, string nickname) ParseSpeciesLine(string speciesLine, BattleTemplateLocalization localization)
    {
        string formName = string.Empty;
        string gender = string.Empty;
        string heldItem = string.Empty;
        string nickname = string.Empty;
        string speciesName = string.Empty;

        // Step 1: Extract held item (@ symbol)
        int heldItemIndex = speciesLine.IndexOf(" @ ");
        if (heldItemIndex != -1)
        {
            heldItem = speciesLine[(heldItemIndex + 3)..].Trim();
            speciesLine = speciesLine[..heldItemIndex].Trim();
        }

        // Step 2: Extract gender at the end "(M)" or "(F)"
        // PKHeX only accepts (M) and (F) on the first line, not (Male) or (Female)
        if (speciesLine.EndsWith("(M)", StringComparison.Ordinal))
        {
            gender = "M";
            speciesLine = speciesLine[..^3].TrimEnd();
        }
        else if (speciesLine.EndsWith("(F)", StringComparison.Ordinal))
        {
            gender = "F";
            speciesLine = speciesLine[..^3].TrimEnd();
        }

        // Step 3: Handle nickname and species
        if (speciesLine.Contains('(') && speciesLine.Contains(')'))
        {
            // Parse nickname and species
            int lastOpenParen = speciesLine.LastIndexOf('(');
            int lastCloseParen = speciesLine.LastIndexOf(')');

            if (lastOpenParen < lastCloseParen)
            {
                // Check if it's "Nickname (Species)" format (correct)
                if (lastOpenParen > 0)
                {
                    string beforeParen = speciesLine[..lastOpenParen].TrimEnd();
                    string insideParen = speciesLine[(lastOpenParen + 1)..lastCloseParen].Trim();

                    // Try to parse inside parentheses as species first
                    if (IsLikelySpecies(insideParen, localization))
                    {
                        nickname = beforeParen;
                        ParseSpeciesForm(insideParen, out speciesName, out formName);
                    }
                    // If that fails, try the other way around
                    else if (IsLikelySpecies(beforeParen, localization))
                    {
                        ParseSpeciesForm(beforeParen, out speciesName, out formName);
                        nickname = insideParen;
                    }
                    else
                    {
                        // Default to PKHeX behavior: assume Nickname (Species)
                        nickname = beforeParen;
                        ParseSpeciesForm(insideParen, out speciesName, out formName);
                    }
                }
                else
                {
                    // "(Species)" or similar at the start
                    string insideParen = speciesLine[(lastOpenParen + 1)..lastCloseParen].Trim();
                    string afterParen = lastCloseParen < speciesLine.Length - 1
                        ? speciesLine[(lastCloseParen + 1)..].Trim()
                        : string.Empty;

                    if (!string.IsNullOrEmpty(afterParen))
                    {
                        // "(Species) Nickname" format (incorrect but supported)
                        ParseSpeciesForm(insideParen, out speciesName, out formName);
                        nickname = afterParen;
                    }
                    else
                    {
                        // Just "(Species)"
                        ParseSpeciesForm(insideParen, out speciesName, out formName);
                    }
                }
            }
        }
        else
        {
            // No parentheses, just parse species and form
            ParseSpeciesForm(speciesLine, out speciesName, out formName);
        }

        return (speciesName, formName, gender, heldItem, nickname);
    }

    private static bool IsLikelySpecies(string text, BattleTemplateLocalization localization)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Direct species match
        var speciesIndex = Array.FindIndex(localization.Strings.specieslist,
            s => s.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (speciesIndex > 0)
            return true;

        // Check if it contains a hyphen (likely has a form)
        if (text.Contains('-'))
        {
            var parts = text.Split('-', 2);
            if (parts.Length > 0)
            {
                speciesIndex = Array.FindIndex(localization.Strings.specieslist,
                    s => s.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                if (speciesIndex > 0)
                    return true;
            }
        }

        return false;
    }

    private static void ParseSpeciesForm(string text, out string speciesName, out string formName)
    {
        speciesName = string.Empty;
        formName = string.Empty;

        if (string.IsNullOrEmpty(text))
            return;

        // Remove -Gmax suffix if present
        if (text.EndsWith("-Gmax", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^5];
        }

        // Check for form separator
        int formSeparatorIndex = text.IndexOf('-');
        if (formSeparatorIndex > 0)
        {
            speciesName = text[..formSeparatorIndex].Trim();
            formName = text[(formSeparatorIndex + 1)..].Trim();
        }
        else
        {
            speciesName = text.Trim();
        }
    }

    private static (string abilityName, string natureName, string ballName) ParseLines(string[] lines, BattleTemplateLocalization localization)
    {
        LogUtil.LogInfo("[AutoCorrect.ParseLines] Analizando habilidad, naturaleza y bola desde líneas", "AutoCorrect.Debug");

        string abilityName = string.Empty;
        string natureName = string.Empty;
        string ballName = string.Empty;

        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();

            var token = localization.Config.TryParse(trimmedLine, out var value);

            switch (token)
            {
                case BattleTemplateToken.Ability:
                    abilityName = value.ToString();
                    LogUtil.LogInfo($"[AutoCorrect.ParseLines] Habilidad encontrada: {abilityName}", "AutoCorrect.Debug");
                    break;
                case BattleTemplateToken.Nature:
                    natureName = value.ToString();
                    LogUtil.LogInfo($"[AutoCorrect.ParseLines] Naturaleza encontrada: {natureName}", "AutoCorrect.Debug");
                    break;
            }

            if (trimmedLine.StartsWith("Ball:"))
            {
                ballName = trimmedLine["Ball:".Length..].Trim();
                LogUtil.LogInfo($"[AutoCorrect.ParseLines] Bola encontrada: {ballName}", "AutoCorrect.Debug");
            }
        }

        return (abilityName, natureName, ballName);
    }

    private static string CorrectLine(string line, int index, string speciesName, string correctedSpeciesName, string correctedFormName, string formName, string correctedGender, string gender, string correctedHeldItem, string heldItem, string correctedAbilityName, string correctedNatureName, string correctedBallName, LegalityAnalysis la, string nickname, BattleTemplateLocalization localization)
    {
        if (index == 0)
        {
            // Reconstruct first line following PKHeX format
            StringBuilder sb = new();

            // Build the species-form string
            string speciesForm = string.IsNullOrEmpty(correctedFormName)
                ? correctedSpeciesName
                : $"{correctedSpeciesName}-{correctedFormName}";

            // Handle nickname
            if (!string.IsNullOrEmpty(nickname))
            {
                sb.Append(nickname);
                sb.Append(" (");
                sb.Append(speciesForm);
                sb.Append(')');
            }
            else
            {
                sb.Append(speciesForm);
            }

            // Append gender if specified - always use (M) or (F) format on first line
            if (!string.IsNullOrEmpty(correctedGender))
            {
                // Convert to M/F format if needed
                string genderChar = correctedGender;
                if (correctedGender.Equals(localization.Config.Male, StringComparison.OrdinalIgnoreCase))
                    genderChar = "M";
                else if (correctedGender.Equals(localization.Config.Female, StringComparison.OrdinalIgnoreCase))
                    genderChar = "F";

                // Only append if it's M or F
                if (genderChar == "M" || genderChar == "F")
                {
                    sb.Append(" (");
                    sb.Append(genderChar);
                    sb.Append(')');
                }
            }

            // Append held item if specified
            if (!string.IsNullOrEmpty(correctedHeldItem))
            {
                sb.Append(" @ ");
                sb.Append(correctedHeldItem);
            }

            return sb.ToString();
        }
        else
        {
            var token = localization.Config.TryParse(line, out _);

            return token switch
            {
                BattleTemplateToken.Ability => !string.IsNullOrEmpty(correctedAbilityName)
                    ? localization.Config.Push(BattleTemplateToken.Ability, correctedAbilityName)
                    : line,
                BattleTemplateToken.Nature => !string.IsNullOrEmpty(correctedNatureName)
                    ? localization.Config.Push(BattleTemplateToken.Nature, correctedNatureName)
                    : line,
                _ when line.StartsWith("Ball:") => !string.IsNullOrEmpty(correctedBallName)
                    ? $"Ball: {correctedBallName}"
                    : line,
                _ => line
            };
        }
    }
}
