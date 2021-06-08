using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityModManagerNet;
using HarmonyLib;
using I2.Loc;
using SolastaModApi;
using System.Collections.Generic;
using System.Linq;

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
            string assembly = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string text = System.IO.File.ReadAllText(assembly + "\\CustomMulticlassCombos.txt");
            IEnumerable<string> multiclasses = text.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach(string multiclass in multiclasses)
            {
                bool isFirstClass = true;
                string customName = null;
                CharacterClassDefinition firstClass = null;
                CharacterSubclassDefinition firstSubclass = null;
                IEnumerable<string> classesSubclassesAndLevels = multiclass.Split(new string[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
                List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>> subsequentClasses = new List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>>();
                foreach (string classSubclassAndLevel in classesSubclassesAndLevels)
                {
                    Dictionary<string, string> keyValuePairs = classSubclassAndLevel.Split(',')
                           .Select(value => value.Split('='))
                           .ToDictionary(pair => pair[0], pair => pair[1]);

                    if(isFirstClass)
                    {
                        customName = keyValuePairs.GetValueSafe("CustomName");
                        firstClass = GetClassDefinitionFromName(keyValuePairs.GetValueSafe("Class"));
                        firstSubclass = GetSubclassDefinitionFromName(keyValuePairs.GetValueSafe("Subclass"));
                        int levels = int.Parse(keyValuePairs.GetValueSafe("Levels"));
                        if(levels > 1)
                            subsequentClasses.Add(new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(firstClass, firstSubclass, levels - 1));
                    }
                    else 
                        subsequentClasses.Add(CreateMulticlassTuple(keyValuePairs));

                    isFirstClass = false;
                }

                MultiClassBuilder.BuildAndAddNewMultiClassToDB(firstClass, firstSubclass, subsequentClasses, customName);
            }
        }




        internal static Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int> CreateMulticlassTuple(Dictionary<string, string> keyValuePairs)
        {
            return new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(GetClassDefinitionFromName(keyValuePairs.GetValueSafe("Class")), GetSubclassDefinitionFromName(keyValuePairs.GetValueSafe("Subclass")), int.Parse(keyValuePairs.GetValueSafe("Levels")));
        }

        public static CharacterClassDefinition GetClassDefinitionFromName(string className)
        {
            CharacterClassDefinition characterClass = DatabaseRepository.GetDatabase<CharacterClassDefinition>().GetAllElements()?.FirstOrDefault(c => string.Equals(c.Name, className));
            if (characterClass == null)
                Error("Class " + className + " not found");

            return characterClass;
        }

        public static CharacterSubclassDefinition GetSubclassDefinitionFromName(string subclassName)
        {
            if (string.Equals(subclassName, "AHTactician"))
                subclassName = "GambitResourcePool"; //Regret this incorrect name now :(

            CharacterSubclassDefinition characterSubclass = DatabaseRepository.GetDatabase<CharacterSubclassDefinition>().GetAllElements()?.FirstOrDefault(c => string.Equals(c.Name, subclassName));
            if (characterSubclass == null)
                Error("Subclass " + subclassName + " not found");

            return characterSubclass;
        }



        //Example of code to add multiclass:
        //MultiClassBuilder.BuildAndAddNewMultiClassToDB(DatabaseHelper.CharacterClassDefinitions.Fighter, DatabaseHelper.CharacterSubclassDefinitions.MartialMountaineer,
        //           new List<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>>()
        //           { new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Fighter, DatabaseHelper.CharacterSubclassDefinitions.MartialMountaineer, 4),
        //            new Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>(DatabaseHelper.CharacterClassDefinitions.Ranger, DatabaseHelper.CharacterSubclassDefinitions.RangerHunter, 5)},
        //           "Mountaineer5/Hunter5");


    }
}

