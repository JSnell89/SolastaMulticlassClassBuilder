using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityModManagerNet;
using HarmonyLib;
using I2.Loc;
using SolastaModApi;
using System.Collections.Generic;

namespace SolastaMulticlassClassBuilder
{
    public class Main
    {
        [Conditional("DEBUG")]
        internal static void Log(string msg) => Logger.Log(msg);
        internal static void Error(Exception ex) => Logger?.Error(ex.ToString());
        internal static void Error(string msg) => Logger?.Error(msg);
        internal static UnityModManager.ModEntry.ModLogger Logger { get; private set; }

        internal static void LoadTranslations()
        {
            DirectoryInfo directoryInfo = new DirectoryInfo($@"{UnityModManager.modsPath}/SolastaMulticlassClassBuilder");
            FileInfo[] files = directoryInfo.GetFiles($"Translations-??.txt");

            foreach (var file in files)
            {
                var filename = $@"{UnityModManager.modsPath}/SolastaMulticlassClassBuilder/{file.Name}";
                var code = file.Name.Substring(13, 2);
                var languageSourceData = LocalizationManager.Sources[0];
                var languageIndex = languageSourceData.GetLanguageIndexFromCode(code);

                if (languageIndex < 0)
                    Main.Error($"language {code} not currently loaded.");
                else
                    using (var sr = new StreamReader(filename))
                    {
                        String line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            var splitted = line.Split(new[] { '\t', ' ' }, 2);
                            var term = splitted[0];
                            var text = splitted[1];
                            languageSourceData.AddTerm(term).Languages[languageIndex] = text;
                        }
                    }
            }
        }

        internal static bool Load(UnityModManager.ModEntry modEntry)
        {
            try
            {
                Logger = modEntry.Logger;

                LoadTranslations();

                var harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                Error(ex);
                throw;
            }

            return true;
        }

        internal static void ModEntryPoint()
        {
            //Wiz2/Cler3/Wiz3/Cler2 - Spellcasters combos don't work properly yet :(
            //MultiClassBuilder.BuildAndAddNewMultiClassToDB(DatabaseHelper.CharacterClassDefinitions.Wizard, DatabaseHelper.CharacterSubclassDefinitions.TraditionShockArcanist,
            //    new List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>>()
            //    { new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Wizard, DatabaseHelper.CharacterSubclassDefinitions.TraditionShockArcanist, 1),
            //        new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Cleric, DatabaseHelper.CharacterSubclassDefinitions.DomainBattle, 2),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Wizard, DatabaseHelper.CharacterSubclassDefinitions.TraditionShockArcanist, 2),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Cleric, DatabaseHelper.CharacterSubclassDefinitions.DomainBattle, 3),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Wizard, DatabaseHelper.CharacterSubclassDefinitions.TraditionShockArcanist, 3),
            //    });

            ////Cler2/Wiz2/Cler3/Wiz3 - Spellcasters combos don't work properly yet :(
            //MultiClassBuilder.BuildAndAddNewMultiClassToDB(DatabaseHelper.CharacterClassDefinitions.Cleric, DatabaseHelper.CharacterSubclassDefinitions.DomainBattle,
            //    new List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>>()
            //    { new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Cleric, DatabaseHelper.CharacterSubclassDefinitions.DomainBattle, 1),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Wizard, DatabaseHelper.CharacterSubclassDefinitions.TraditionShockArcanist, 2),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Cleric, DatabaseHelper.CharacterSubclassDefinitions.DomainBattle, 3),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Wizard, DatabaseHelper.CharacterSubclassDefinitions.TraditionShockArcanist, 3),
            //    });

            //Fighter1/Rogue9
            //MultiClassBuilder.BuildAndAddNewMultiClassToDB(DatabaseHelper.CharacterClassDefinitions.Fighter, DatabaseHelper.CharacterSubclassDefinitions.MartialMountaineer,
            //       new List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>>()
            //    { new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Rogue, DatabaseHelper.CharacterSubclassDefinitions.RoguishDarkweaver, 9) });

            ////Fighter5/Rogue1/Ranger4
            //MultiClassBuilder.BuildAndAddNewMultiClassToDB(DatabaseHelper.CharacterClassDefinitions.Fighter, DatabaseHelper.CharacterSubclassDefinitions.MartialMountaineer,
            //    new List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>>()
            //    { new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Fighter, DatabaseHelper.CharacterSubclassDefinitions.MartialMountaineer, 4),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Rogue, DatabaseHelper.CharacterSubclassDefinitions.RoguishDarkweaver, 1),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Ranger, DatabaseHelper.CharacterSubclassDefinitions.RangerHunter, 4) });

            //Fighter1/Rogue1/Fighter4/Rogue4
            //MultiClassBuilder.BuildAndAddNewMultiClassToDB(DatabaseHelper.CharacterClassDefinitions.Fighter, DatabaseHelper.CharacterSubclassDefinitions.MartialMountaineer,
            //    new List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>>()
            //    {
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Rogue, DatabaseHelper.CharacterSubclassDefinitions.RoguishThief, 1),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Fighter, DatabaseHelper.CharacterSubclassDefinitions.MartialMountaineer, 4),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Rogue, DatabaseHelper.CharacterSubclassDefinitions.RoguishThief, 4) });

            //Fighter1/Rogue1/Fighter4/Wizard4
            //MultiClassBuilder.BuildAndAddNewMultiClassToDB(DatabaseHelper.CharacterClassDefinitions.Fighter, DatabaseHelper.CharacterSubclassDefinitions.MartialMountaineer,
            //    new List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>>()
            //    {
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Rogue, DatabaseHelper.CharacterSubclassDefinitions.RoguishThief, 1),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Fighter, DatabaseHelper.CharacterSubclassDefinitions.MartialMountaineer, 4),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Wizard, DatabaseHelper.CharacterSubclassDefinitions.TraditionShockArcanist, 4) });

            ////Fighter1/Rogue1/Cler4/Fight2/Cler2
            //MultiClassBuilder.BuildAndAddNewMultiClassToDB(DatabaseHelper.CharacterClassDefinitions.Fighter, DatabaseHelper.CharacterSubclassDefinitions.MartialMountaineer,
            //    new List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>>()
            //    {
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Rogue, DatabaseHelper.CharacterSubclassDefinitions.RoguishThief, 1),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Cleric, DatabaseHelper.CharacterSubclassDefinitions.DomainBattle, 4),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Fighter, DatabaseHelper.CharacterSubclassDefinitions.MartialMountaineer, 2),
            //    new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Cleric, DatabaseHelper.CharacterSubclassDefinitions.DomainBattle, 2) });

            //Cler8/Pal2 - Kind of works, Doesn't add Paladin spells to know spells atm but you can smite (though level 5 smites do no damage!)
            //MultiClassBuilder.BuildAndAddNewMultiClassToDB(DatabaseHelper.CharacterClassDefinitions.Cleric, DatabaseHelper.CharacterSubclassDefinitions.DomainBattle,
            //    new List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>>()
            //    {
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Cleric, DatabaseHelper.CharacterSubclassDefinitions.DomainBattle, 7),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Paladin, DatabaseHelper.CharacterSubclassDefinitions.OathOfTirmar, 2),
            //    });

            ////Cler1/Wiz5/Pal4 - Spellcasters combos don't work properly yet :(
            //MultiClassBuilder.BuildAndAddNewMultiClassToDB(DatabaseHelper.CharacterClassDefinitions.Cleric, DatabaseHelper.CharacterSubclassDefinitions.DomainBattle,
            //    new List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>>()
            //    { new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Wizard, DatabaseHelper.CharacterSubclassDefinitions.TraditionShockArcanist, 5),
            //      new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Paladin, DatabaseHelper.CharacterSubclassDefinitions.OathOfTirmar, 4) });
        }
    }
}

