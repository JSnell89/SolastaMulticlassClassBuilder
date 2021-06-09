using SolastaModApi;
using SolastaModApi.Extensions;
using SolastaModApi.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolastaMulticlassClassBuilder
{
    public static class MultiClassBuilder
    {
        public static Guid MultiClassBuilderGuid = new Guid("83436f15-d9c6-4ef8-a4bc-3a309fc0004b");

        //Multiclass builder builds a 'multiclass' based on the given classes.  The tuple is the class and how many levels to take of it.  Classes are added in the order they are supplied.
        public static void BuildAndAddNewMultiClassToDB(CharacterClassDefinition startingClass, CharacterSubclassDefinition startingSubclass, IEnumerable<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>> subsequentClasses, string classNameOverrideTranslationKey = null)
        {   
            CharacterClassDefinition definition = new CharacterClassDefinition();

            string className = GetMulticlassName(startingClass, startingSubclass, subsequentClasses, classNameOverrideTranslationKey);
            definition.GuiPresentation.Title = className;
            definition.GuiPresentation.Description = "A combination of " + className;
            definition.SetGuid(GuidHelper.Create(MultiClassBuilderGuid, definition.GuiPresentation.Title).ToString()); //Should allow for loading the same multiclass through multiple sessions

            MultiClassBuilderContext context = new MultiClassBuilderContext();

            definition.GuiPresentation.SetSpriteReference(startingClass.GuiPresentation.SpriteReference);
            definition.AbilityScoresPriority.AddRange(startingClass.AbilityScoresPriority);
            definition.SetClassAnimationId(startingClass.ClassAnimationId);
            definition.SetClassPictogramReference(startingClass.ClassPictogramReference);
            definition.SetDefaultBattleDecisions(startingClass.DefaultBattleDecisions);
            definition.EquipmentRows.AddRange(startingClass.EquipmentRows);
            definition.ExpertiseAutolearnPreference.AddRange((subsequentClasses.SelectMany(c => c.Item1.ExpertiseAutolearnPreference)));
            definition.FeatAutolearnPreference.AddRange(startingClass.FeatAutolearnPreference);
            //The multi-class won't be quite correct as certain levels will have the wrong hit die.
            //Best way to likely do this is to use the average over all of the levels, though this does ruin front loaded hit dice multiclasses like Fighter1/Wizard9
            definition.SetHitDice(GetAverageHitDie(subsequentClasses));
            definition.SetIngredientGatheringOdds(startingClass.IngredientGatheringOdds);
            definition.SetCachedName(definition.GuiPresentation.Title);
            definition.PersonalityFlagOccurences.AddRange(startingClass.PersonalityFlagOccurences);
            definition.SetRequiresDeity(startingClass.RequiresDeity);
            definition.SkillAutolearnPreference.AddRange(startingClass.SkillAutolearnPreference);
            definition.ToolAutolearnPreference.AddRange(startingClass.ToolAutolearnPreference);
            definition.FeatureUnlocks.AddRange(startingClass.FeatureUnlocks.Where(fu => fu.Level == 1 && !ExcludedLevel1MulticlassFeatureDefinitions.Contains(fu.FeatureDefinition)));
            definition.FeatureUnlocks.AddRange(startingSubclass.FeatureUnlocks.Where(fu => fu.Level == 1 && !ExcludedLevel1MulticlassFeatureDefinitions.Contains(fu.FeatureDefinition)));
            //TODO handle Paladin lay on hands pool scaling on hero level
            //TODO Level 5 spell slot smites don't do any damage.

            FeatureDefinition firstSpellCastFeature = startingClass.FeatureUnlocks.FirstOrDefault(fu => fu.Level == 1 && fu.FeatureDefinition is FeatureDefinitionCastSpell)?.FeatureDefinition;
            FeatureDefinitionCastSpell secondSpellCastFeature = null;
            SpellcastingFeatureHelper firstSpellCastingFeatureHelper = new SpellcastingFeatureHelper();
            SpellcastingFeatureHelper secondSpellCastingFeatureHelper = new SpellcastingFeatureHelper();
            if (firstSpellCastFeature != null)
            {
                firstSpellCastingFeatureHelper.LevelToAddSpellcastingFeature = 1;
                firstSpellCastingFeatureHelper.FirstTimeSetup(className, startingClass, firstSpellCastFeature as FeatureDefinitionCastSpell, GetCasterTypeForSingleLevelOfClass(startingClass, startingSubclass));
                firstSpellCastingFeatureHelper.AddFeaturesForClassLevel(context);
            }

            context.IncrementExistingClassLevel(startingClass);
            context.CurrentClassLevel = 2;
            //Almost guaranteed this doesn't work for multiple spell-casting classes :)
            foreach (var classAndLevels in subsequentClasses)
            {
                for(int i = 0; i < classAndLevels.Item3; i++)
                {
                    int addFromClassLevel = context.GetExistingClassLevel(classAndLevels.Item1) + 1;

                    AddClassFeatureUnlocksOfLevel(definition, context.CurrentClassLevel, classAndLevels.Item1, addFromClassLevel);
                    //Since subclasses are hero level based we need to not allow the choice and add the subclass features directly onto the main class at the proper level
                    AddSubclassFeatureUnlocksOfLevel(definition, context.CurrentClassLevel, classAndLevels.Item2, addFromClassLevel);
                    AddCustomHandledClassFeatureUnlocksOfLevel(definition, context.CurrentClassLevel, classAndLevels.Item1, addFromClassLevel);

                    if (firstSpellCastFeature == null)
                        firstSpellCastFeature = classAndLevels.Item1.FeatureUnlocks.FirstOrDefault(fu => fu.Level == addFromClassLevel && fu.FeatureDefinition is FeatureDefinitionCastSpell)?.FeatureDefinition ?? classAndLevels.Item2.FeatureUnlocks.FirstOrDefault(fu => fu.Level == addFromClassLevel && fu.FeatureDefinition is FeatureDefinitionCastSpell)?.FeatureDefinition;
                    else if (secondSpellCastFeature == null)
                        secondSpellCastFeature = (classAndLevels.Item1.FeatureUnlocks.FirstOrDefault(fu => fu.Level == addFromClassLevel && fu.FeatureDefinition is FeatureDefinitionCastSpell && !string.Equals(fu.FeatureDefinition.Name, firstSpellCastFeature.Name))?.FeatureDefinition ?? classAndLevels.Item2.FeatureUnlocks.FirstOrDefault(fu => fu.Level == addFromClassLevel && fu.FeatureDefinition is FeatureDefinitionCastSpell && !string.Equals(fu.FeatureDefinition.Name, firstSpellCastFeature.Name))?.FeatureDefinition) as FeatureDefinitionCastSpell;

                    if (firstSpellCastFeature != null)
                    {
                        double casterLevel = GetCasterLevelForGivenLevel(context.CurrentClassLevel, startingClass, startingSubclass, subsequentClasses);

                        if (!firstSpellCastingFeatureHelper.IsSetup)
                        {
                            firstSpellCastingFeatureHelper.FirstTimeSetup(className, classAndLevels.Item1, firstSpellCastFeature as FeatureDefinitionCastSpell, GetCasterTypeForSingleLevelOfClass(classAndLevels.Item1, classAndLevels.Item2));

                            //If we already have a spellcasting feature then don't bother adding empty levels
                            if (firstSpellCastingFeatureHelper.LevelToAddSpellcastingFeature < 1)
                            {
                                //Add empty spell slots for all levels that didn't have caster levels
                                for (int j = 1; j < context.CurrentClassLevel; j++)
                                {
                                    firstSpellCastingFeatureHelper.AddEmptySpellcastingLevel(j);
                                }
                            }
                        }

                        firstSpellCastingFeatureHelper.AddFeaturesForClassLevel(context);

                        if (firstSpellCastingFeatureHelper.LevelToAddSpellcastingFeature < 1)
                            firstSpellCastingFeatureHelper.LevelToAddSpellcastingFeature = context.CurrentClassLevel;

                        if(secondSpellCastFeature != null)
                        {
                            if (!secondSpellCastingFeatureHelper.IsSetup)
                            {
                                secondSpellCastingFeatureHelper.FirstTimeSetup(className, classAndLevels.Item1, secondSpellCastFeature as FeatureDefinitionCastSpell, GetCasterTypeForSingleLevelOfClass(classAndLevels.Item1, classAndLevels.Item2));

                                //If we already have a spellcasting feature then don't bother adding empty levels
                                if (secondSpellCastingFeatureHelper.LevelToAddSpellcastingFeature < 1)
                                {
                                    //Add empty spell slots for all levels that didn't have caster levels
                                    for (int j = 1; j < context.CurrentClassLevel; j++)
                                    {
                                        secondSpellCastingFeatureHelper.AddEmptySpellcastingLevel(j);
                                    }
                                }
                            }

                            secondSpellCastingFeatureHelper.AddFeaturesForClassLevel(context);

                            if (secondSpellCastingFeatureHelper.LevelToAddSpellcastingFeature < 1)
                                secondSpellCastingFeatureHelper.LevelToAddSpellcastingFeature = context.CurrentClassLevel;
                        }
                    }                    

                    context.CurrentClassLevel++;
                    context.IncrementExistingClassLevel(classAndLevels.Item1);
                }
            }
            double endCasterLevel = GetCasterLevelForGivenLevel(context.CurrentClassLevel, startingClass, startingSubclass, subsequentClasses);


            //var castSpellDB = DatabaseRepository.GetDatabase<FeatureDefinitionCastSpell>();
            if (firstSpellCastingFeatureHelper.IsSetup)
            {
                //Spellcasting features seem to need to go to 20 not matter what.
                while (firstSpellCastingFeatureHelper.CastSpellDefinition.SlotsPerLevels.Count < 20)
                    firstSpellCastingFeatureHelper.AddFeaturesForClassLevel(context);

                //If we get a wizard not as the initial class, add a spellbook otherwise their spells added at level up don't get put in anything/don't save
                if (!string.Equals(startingClass.Name, "Wizard") && string.Equals(firstSpellCastingFeatureHelper.SpellcastingClass.Name, DatabaseHelper.CharacterClassDefinitions.Wizard.Name))
                {
                    //Add a spellbook for Wizard Multiclasses, make sure not to add a spellbook to an starting wizard, it messes things up.
                    var list = new List<CharacterClassDefinition.HeroEquipmentOption>();
                    list.Add(EquipmentOptionsBuilder.Option(DatabaseHelper.ItemDefinitions.Spellbook, EquipmentDefinitions.OptionGenericItem, 1));
                    var equipmentColumn = new CharacterClassDefinition.HeroEquipmentColumn();
                    equipmentColumn.EquipmentOptions.AddRange(list);
                    var equipmentRow = new CharacterClassDefinition.HeroEquipmentRow();
                    equipmentRow.EquipmentColumns.Add(equipmentColumn);
                    definition.EquipmentRows.Add(equipmentRow);
                }

                //Put the spellcasting feature on the class for the first one.
                //Perhaps we can get a second one by putting it on the subclass?
                firstSpellCastingFeatureHelper.CastSpellDefinition.SetSpellCastingOrigin(FeatureDefinitionCastSpell.CastingOrigin.Class);
                //castSpellDB.Add(firstSpellCastingFeatureHelper.CastSpellDefinition);
                definition.FeatureUnlocks.Add(new FeatureUnlockByLevel(firstSpellCastingFeatureHelper.CastSpellDefinition, firstSpellCastingFeatureHelper.LevelToAddSpellcastingFeature));
            }

            if (secondSpellCastingFeatureHelper.IsSetup)
            {
                //Spellcasting features seem to need to go to 20 not matter what.
                while (secondSpellCastingFeatureHelper.CastSpellDefinition.SlotsPerLevels.Count < 20)
                    secondSpellCastingFeatureHelper.AddFeaturesForClassLevel(context);

                //If we get a wizard not as the initial class, add a spellbook otherwise their spells added at level up don't get put in anything/don't save
                if (!string.Equals(startingClass.Name, "Wizard") && string.Equals(firstSpellCastingFeatureHelper.SpellcastingClass.Name, DatabaseHelper.CharacterClassDefinitions.Wizard.Name))
                {
                    //Add a spellbook for Wizard Multiclasses, make sure not to add a spellbook to an starting wizard, it messes things up.
                    var list = new List<CharacterClassDefinition.HeroEquipmentOption>();
                    list.Add(EquipmentOptionsBuilder.Option(DatabaseHelper.ItemDefinitions.Spellbook, EquipmentDefinitions.OptionGenericItem, 1));
                    var equipmentColumn = new CharacterClassDefinition.HeroEquipmentColumn();
                    equipmentColumn.EquipmentOptions.AddRange(list);
                    var equipmentRow = new CharacterClassDefinition.HeroEquipmentRow();
                    equipmentRow.EquipmentColumns.Add(equipmentColumn);
                    definition.EquipmentRows.Add(equipmentRow);
                }

                //Second spellcasting feature goes on the subclass
                secondSpellCastingFeatureHelper.CastSpellDefinition.SetSpellCastingOrigin(FeatureDefinitionCastSpell.CastingOrigin.Subclass);
                secondSpellCastingFeatureHelper.CastSpellDefinition.SetSpellReadyness(RuleDefinitions.SpellReadyness.AllKnown);

                string subclassName = "Subclass" + definition.GuiPresentation.Title;
                CharacterSubclassDefinitionBuilder b = new CharacterSubclassDefinitionBuilder(subclassName, GuidHelper.Create(MultiClassBuilderGuid, subclassName).ToString());
                GuiPresentation subclassGUI = new GuiPresentation();
                subclassGUI.Title = subclassName;
                subclassGUI.Description = "Subclass" + definition.GuiPresentation.Description;
                subclassGUI.SetSpriteReference(DatabaseHelper.CharacterSubclassDefinitions.DomainElementalCold.GuiPresentation.SpriteReference);
                b.SetGuiPresentation(subclassGUI);
                b.AddFeatureAtLevel(secondSpellCastingFeatureHelper.CastSpellDefinition, secondSpellCastingFeatureHelper.LevelToAddSpellcastingFeature);
                var subclass = b.AddToDB();

                string subclassChoiceName = "Choice" + subclassName;
                var builder = new FeatureDefinitionSubclassChoiceBuilder(subclassChoiceName, GuidHelper.Create(MultiClassBuilderGuid, subclassChoiceName).ToString());

                var subclassChoice = builder
                    .SetSubclassSuffix("FakeChoice")
                    .SetFilterByDeity(false)
                    .SetGuiPresentation(subclassGUI)
                    .AddToDB();

                subclassChoice.Subclasses.Add(subclass.Name);
                definition.FeatureUnlocks.Add(new FeatureUnlockByLevel(subclassChoice, 2));
            }


            var db = DatabaseRepository.GetDatabase<CharacterClassDefinition>();
            db.Add(definition);
        }

        private static void AddClassFeatureUnlocksOfLevel(CharacterClassDefinition characterClassToAddTo, int levelToAddTo, CharacterClassDefinition characterClassToAddFrom, int levelToAddFrom)
        {
            var featureUnlocksToAdd = characterClassToAddFrom.FeatureUnlocks.Where(fu => !ExcludedLevel2PlusTotalCharacterLevelMulticlassFeatureDefinitions.Contains(fu.FeatureDefinition) && fu.Level == levelToAddFrom).Select(fu => new FeatureUnlockByLevel(fu.FeatureDefinition, levelToAddTo));
            characterClassToAddTo.FeatureUnlocks.AddRange(featureUnlocksToAdd);
        }

        private static void AddSubclassFeatureUnlocksOfLevel(CharacterClassDefinition characterClassToAddTo, int levelToAddTo, CharacterSubclassDefinition characterSubclassToAddFrom, int levelToAddFrom)
        {
            var featureUnlocksToAdd = characterSubclassToAddFrom.FeatureUnlocks.Where(fu => !ExcludedLevel2PlusTotalCharacterLevelMulticlassFeatureDefinitions.Contains(fu.FeatureDefinition) && fu.Level == levelToAddFrom).Select(fu => new FeatureUnlockByLevel(fu.FeatureDefinition, levelToAddTo));
            characterClassToAddTo.FeatureUnlocks.AddRange(featureUnlocksToAdd);
        }

        private static void AddCustomHandledClassFeatureUnlocksOfLevel(CharacterClassDefinition characterClassToAddTo, int levelToAddTo, CharacterClassDefinition characterClassToAddFrom, int levelToAddFrom)
        {
            List<FeatureUnlockByLevel> customHandledFeaturesList = null;
            if (characterClassToAddFrom == DatabaseHelper.CharacterClassDefinitions.Rogue)
                customHandledFeaturesList = RogueMultiClassCustomHandledFeaturesByLevel;
            else if (characterClassToAddFrom == DatabaseHelper.CharacterClassDefinitions.Ranger)
                customHandledFeaturesList = RangerMultiClassCustomHandledFeaturesByLevel;

            if (customHandledFeaturesList == null)
                return;

            var featureUnlocksToAdd = customHandledFeaturesList.Where(fu => fu.Level == levelToAddFrom).Select(fu => new FeatureUnlockByLevel(fu.FeatureDefinition, levelToAddTo)).ToArray();
            characterClassToAddTo.FeatureUnlocks.AddRange(featureUnlocksToAdd);
        }

        private static string GetMulticlassName(CharacterClassDefinition startingClass, CharacterSubclassDefinition startingSubclass, IEnumerable<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>> subsequentClasses, string classNameOverrideTranslationKey)
        {
            if (classNameOverrideTranslationKey != null)
                return classNameOverrideTranslationKey;

            var secondClassAndNumLevels = subsequentClasses.First();
            //If starting class is Champion, and then we want Champion for level 2,3,4,5 we want the class name to be Champion5/XXX
            if(startingSubclass == secondClassAndNumLevels.Item2)
                return secondClassAndNumLevels.Item2.Name + (secondClassAndNumLevels.Item3 + 1).ToString() + "\n" + String.Join("\n", subsequentClasses.Skip(1).Select(c => c.Item2.Name + c.Item3.ToString()));
            
            return startingSubclass.Name + "1\n" + String.Join("\n", subsequentClasses.Select(c => c.Item2.Name + c.Item3.ToString()));
        }

        //Since hit dice is a single property we have to choose one hit die for the entire 'Multiclass'.  In this case we'll be generous and round up.
        //Unfortunately we can't have a d7 or d9 as those don't exist in the current code so the Multiclass chars will more than likely end up with more health than they should.
        private static RuleDefinitions.DieType GetAverageHitDie(IEnumerable<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>> classes)
        {
            double cumlativeHitDieEnumValue = 0;
            double numTotalClassLevels = 0;
            foreach(var classAndLevels in classes)
            {
                cumlativeHitDieEnumValue += classAndLevels.Item3 * (int)classAndLevels.Item1.HitDice;
                numTotalClassLevels += classAndLevels.Item3;
            }

            //Be nice and give the ceiling;
            int avgHitDieEnumValue = (int)Math.Ceiling(cumlativeHitDieEnumValue / numTotalClassLevels);
            return (RuleDefinitions.DieType)avgHitDieEnumValue;
        }

        private static double GetCasterLevelForGivenLevel(int currentLevel, CharacterClassDefinition startingClass, CharacterSubclassDefinition startingSubclass, IEnumerable<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>> subsequentClasses)
        {
            var context = new CasterLevelContext();
            context.IncrementCasterLevel(GetCasterTypeForSingleLevelOfClass(startingClass, startingSubclass));

            int numLevelsRemaining = currentLevel -1;
            foreach(var subseqeuntClass in subsequentClasses)
            {
                int numLevelsToUseFromNextClass = Math.Min(numLevelsRemaining, subseqeuntClass.Item3);
                for (int i = numLevelsToUseFromNextClass; i > 0; i--)
                {
                    context.IncrementCasterLevel(GetCasterTypeForSingleLevelOfClass(subseqeuntClass.Item1, subseqeuntClass.Item2));
                    numLevelsRemaining--;
                }
            }

            return context.GetCasterLevel();
        }

        private static eAHCasterType GetCasterTypeForSingleLevelOfClass(CharacterClassDefinition charClass, CharacterSubclassDefinition subclass)
        {
            if (FullCasterList.Contains(charClass))
                return eAHCasterType.Full;
            else if (HalfCasterList.Contains(charClass))
                return eAHCasterType.Half;
            else if (OneThirdCasterList.Contains(subclass))
                return eAHCasterType.OneThird;

            return eAHCasterType.None;
        }

        public class CasterLevelContext
        {
            public CasterLevelContext()
            {
                NumOneThirdLevels = 0;
                NumHalfLevels = 0;
                NumFullLevels = 0;
            }

            //I think technically this should be split by each OneThird and each Half caster but I can look at that later.
            public void IncrementCasterLevel(eAHCasterType casterLevelType)
            {
                if (casterLevelType == eAHCasterType.OneThird)
                    NumOneThirdLevels++;
                if (casterLevelType == eAHCasterType.Half)
                    NumHalfLevels++;
                if (casterLevelType == eAHCasterType.Full)
                    NumFullLevels++;
            }

            public double GetCasterLevel()
            {
                double casterLevel = 0;
                if (NumOneThirdLevels >= 3)
                    casterLevel += NumOneThirdLevels/3.0;
                if (NumHalfLevels >= 2)
                    casterLevel += NumHalfLevels/2.0;
                casterLevel += NumFullLevels;

                return casterLevel;
            }

            double NumOneThirdLevels = 0;
            double NumHalfLevels = 0;
            double NumFullLevels = 0;
        }

        public enum eAHCasterType
        {
            None,
            OneThird,
            Half,
            Full
        };

        public class SpellcastingFeatureHelper
        {
            public List<FeatureDefinitionCastSpell.SlotsByLevelDuplet> OldSpellSlots = new List<FeatureDefinitionCastSpell.SlotsByLevelDuplet>();
            public List<int> OldKnownCantrips = new List<int>();
            public List<int> OldKnownSpells = new List<int>();
            public List<int> OldScribedSpells = new List<int>();
            public eAHCasterType CasterType = eAHCasterType.None;
            public FeatureDefinitionCastSpell CastSpellDefinition;

            public void FirstTimeSetup(string className, CharacterClassDefinition characterClass, FeatureDefinitionCastSpell castSpellDefinition, eAHCasterType casterType)
            {
                if (!IsSetup)
                {
                    CopySpellDefinition(className, castSpellDefinition);
                    SetupOldData(castSpellDefinition, casterType);
                    ClearCastSpellFeature();
                    SpellcastingClass = characterClass;
                    IsSetup = true;
                }
            }

            //Need to copy so we don't alter the existing spell definition for real classes
            private void CopySpellDefinition(string className, FeatureDefinitionCastSpell copyFrom)
            {
                CastSpellDefinition = CastSpellCopyBuilder.CreateAndAddToDB(className + copyFrom.GuiPresentation.Title, GuidHelper.Create(MultiClassBuilderGuid, className + copyFrom.GuiPresentation.Title).ToString(), copyFrom);
                CastSpellDefinition.GuiPresentation.Title = className + copyFrom.GuiPresentation.Title;
                CastSpellDefinition.GuiPresentation.Description = className + copyFrom.GuiPresentation.Description;
            }

            private void SetupOldData(FeatureDefinitionCastSpell copyFromSpellDefinition, eAHCasterType casterType)
            {
                OldSpellSlots.AddRange(copyFromSpellDefinition.SlotsPerLevels);
                OldKnownCantrips.AddRange(copyFromSpellDefinition.KnownCantrips);
                OldKnownSpells.AddRange(copyFromSpellDefinition.KnownSpells);
                OldScribedSpells.AddRange(copyFromSpellDefinition.ScribedSpells);
                CasterType = casterType;
            }

            private void ClearCastSpellFeature()
            {
                CastSpellDefinition.SlotsPerLevels.Clear();
                CastSpellDefinition.KnownCantrips.Clear();
                CastSpellDefinition.KnownSpells.Clear();
                CastSpellDefinition.ScribedSpells.Clear();
            }

            public void AddFeaturesForClassLevel(MultiClassBuilderContext context)
            {
                AddFeaturesForClassLevel(context.GetExistingClassLevel(SpellcastingClass));
            }

            private void AddFeaturesForClassLevel(int classLevelMinus1)
            {
                CastSpellDefinition.SlotsPerLevels.Add(OldSpellSlots[classLevelMinus1]);
                CastSpellDefinition.KnownCantrips.Add(OldKnownCantrips[classLevelMinus1]);
                CastSpellDefinition.KnownSpells.Add(OldKnownSpells[classLevelMinus1]);
                CastSpellDefinition.ScribedSpells.Add(OldScribedSpells[classLevelMinus1]);
            }

            public void AddFeaturesForCasterLevel(double casterLevel)
            {
                CastSpellDefinition.SlotsPerLevels.Add(OldSpellSlots[(int)(casterLevel * GetCasterLevelMultiplier()) - 1]); //Example Level 2 Paladin - casterLevel (1), Multiplier (2), so 1*2 = 2, -1 since the array starts at 0.  So we get the spell info from level 2 Paladin (index 1).
                CastSpellDefinition.KnownCantrips.Add(OldKnownCantrips[(int)(casterLevel * GetCasterLevelMultiplier()) - 1]); //Example 2 Level 5 Paladin, Level 3 Spellblade - CasterLevel (2.5+1) Multiplier (2) = 3.5*2 = 7, -1 since array starts at 0.  So we get spell info from level 7 paladin (index 6).
                CastSpellDefinition.KnownSpells.Add(OldKnownSpells[(int)(casterLevel * GetCasterLevelMultiplier()) - 1]);
                CastSpellDefinition.ScribedSpells.Add(OldScribedSpells[(int)(casterLevel * GetCasterLevelMultiplier()) - 1]);
            }

            public void AddEmptySpellcastingLevel(int level)
            {
                CastSpellDefinition.SlotsPerLevels.Add(new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = level, Slots = EmptySpellSlotDefinition });
                CastSpellDefinition.KnownCantrips.Add(0);
                CastSpellDefinition.KnownSpells.Add(0);
                CastSpellDefinition.ScribedSpells.Add(0);
            }

            public int GetCasterLevelMultiplier()
            {
                if (CasterType == eAHCasterType.Full)
                    return 1;
                else if (CasterType == eAHCasterType.Half)
                    return 2;
                else if (CasterType == eAHCasterType.OneThird)
                    return 3;

                return 0;
            }

            public CharacterClassDefinition SpellcastingClass { get; private set; }
            public int LevelToAddSpellcastingFeature { get; set; }
            public bool IsSetup { get; private set; } = false;

            private static readonly List<int> EmptySpellSlotDefinition = new List<int>() { 0, 0, 0, 0, 0 };
        }

        private static readonly CharacterClassDefinition[] FullCasterList = new CharacterClassDefinition[]
        {
            DatabaseHelper.CharacterClassDefinitions.Cleric,
            DatabaseHelper.CharacterClassDefinitions.Wizard,
        };

        private static readonly CharacterClassDefinition[] HalfCasterList = new CharacterClassDefinition[]
        {
            DatabaseHelper.CharacterClassDefinitions.Paladin,
            DatabaseHelper.CharacterClassDefinitions.Ranger,
        };

        private static readonly CharacterSubclassDefinition[] OneThirdCasterList = new CharacterSubclassDefinition[]
        {
            DatabaseHelper.CharacterSubclassDefinitions.MartialSpellblade,
            DatabaseHelper.CharacterSubclassDefinitions.RoguishShadowCaster,
        };


        private static readonly FeatureDefinition[] ExcludedLevel1MulticlassFeatureDefinitions = new FeatureDefinition[]
        {
            DatabaseHelper.FeatureDefinitionSubclassChoices.SubclassChoiceClericDivineDomains,
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellCleric,
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellWizard,
        };

        private static readonly FeatureDefinition[] ExcludedLevel2PlusTotalCharacterLevelMulticlassFeatureDefinitions = new FeatureDefinition[] 
        {  
            DatabaseHelper.FeatureDefinitionPointPools.PointPoolClericSkillPoints,
            DatabaseHelper.FeatureDefinitionPointPools.PointPoolFighterSkillPoints,
            DatabaseHelper.FeatureDefinitionPointPools.PointPoolPaladinSkillPoints,
            DatabaseHelper.FeatureDefinitionPointPools.PointPoolRangerSkillPoints,
            DatabaseHelper.FeatureDefinitionPointPools.PointPoolRogueSkillPoints, //Technically you should get 2 rogue skill points
            DatabaseHelper.FeatureDefinitionPointPools.PointPoolWizardSkillPoints,
            DatabaseHelper.FeatureDefinitionProficiencys.ProficiencyClericSavingThrow,
            DatabaseHelper.FeatureDefinitionProficiencys.ProficiencyFighterSavingThrow,
            DatabaseHelper.FeatureDefinitionProficiencys.ProficiencyPaladinSavingThrow,
            DatabaseHelper.FeatureDefinitionProficiencys.ProficiencyRangerSavingThrow,
            DatabaseHelper.FeatureDefinitionProficiencys.ProficiencyRogueSavingThrow,
            DatabaseHelper.FeatureDefinitionProficiencys.ProficiencyWizardSavingThrow,
            DatabaseHelper.FeatureDefinitionSubclassChoices.SubclassChoiceClericDivineDomains,
            DatabaseHelper.FeatureDefinitionSubclassChoices.SubclassChoiceFighterMartialArchetypes,
            DatabaseHelper.FeatureDefinitionSubclassChoices.SubclassChoicePaladinSacredOaths,
            DatabaseHelper.FeatureDefinitionSubclassChoices.SubclassChoiceRangerArchetypes,
            DatabaseHelper.FeatureDefinitionSubclassChoices.SubclassChoiceRogueRoguishArchetypes,
            DatabaseHelper.FeatureDefinitionSubclassChoices.SubclassChoiceWizardArcaneTraditions,
            DatabaseHelper.FeatureDefinitionAdditionalDamages.AdditionalDamageRogueSneakAttack, //Have to handle this separately since it scales with Hero Level
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellCleric,
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellPaladin,
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellRanger,
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellShadowcaster,
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellMartialSpellBlade,
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellWizard,
        };

        public class MultiClassBuilderContext
        {
            public MultiClassBuilderContext()
            {
                CurrentClassLevel = 1;
                ExistingClassesAndLevels.Clear();
            }

            public void IncrementExistingClassLevel(CharacterClassDefinition characterClass)
            {
                if (ExistingClassesAndLevels.ContainsKey(characterClass.Name))
                    ExistingClassesAndLevels[characterClass.Name]++;
                else
                    ExistingClassesAndLevels.Add(characterClass.Name, 1);
            }

            public int GetExistingClassLevel(CharacterClassDefinition characterClass)
            {
                if (ExistingClassesAndLevels.ContainsKey(characterClass.Name))
                    return ExistingClassesAndLevels[characterClass.Name];
                else
                    return 0;
            }

            public Dictionary<string, int> ExistingClassesAndLevels = new Dictionary<string, int>();

            public int CurrentClassLevel = 1;
        }

        private static List<FeatureUnlockByLevel> RogueMultiClassCustomHandledFeaturesByLevel = new List<FeatureUnlockByLevel>()
        {
            new FeatureUnlockByLevel(RogueMulticlassExtraSkillPointBuilder.RogueMulticlassExtraSkillPoint, 1),
            new FeatureUnlockByLevel(RogueMulticlassSneakAttackBuilder.RogueMulticlassSneakAttackLevel1, 1),
            new FeatureUnlockByLevel(RogueMulticlassSneakAttackBuilder.RogueMulticlassSneakAttackLevel3, 3),
            new FeatureUnlockByLevel(RogueMulticlassSneakAttackBuilder.RogueMulticlassSneakAttackLevel5, 5),
            new FeatureUnlockByLevel(RogueMulticlassSneakAttackBuilder.RogueMulticlassSneakAttackLevel7, 7),
            new FeatureUnlockByLevel(RogueMulticlassSneakAttackBuilder.RogueMulticlassSneakAttackLevel9, 9),
            //new FeatureUnlockByLevel(RogueMulticlassSneakAttackBuilder.RogueMulticlassSneakAttack, 11),
            //new FeatureUnlockByLevel(RogueMulticlassSneakAttackBuilder.RogueMulticlassSneakAttack, 13),
            //new FeatureUnlockByLevel(RogueMulticlassSneakAttackBuilder.RogueMulticlassSneakAttack, 15),
            //new FeatureUnlockByLevel(RogueMulticlassSneakAttackBuilder.RogueMulticlassSneakAttack, 17),
            //new FeatureUnlockByLevel(RogueMulticlassSneakAttackBuilder.RogueMulticlassSneakAttack, 19),
        };

        private static List<FeatureUnlockByLevel> RangerMultiClassCustomHandledFeaturesByLevel = new List<FeatureUnlockByLevel>()
        {
            new FeatureUnlockByLevel(RangerMulticlassExtraSkillPointBuilder.RangerMulticlassExtraSkillPoint, 1),
        };

        /// <summary>
        /// Add this on odd levels of the rogue
        /// </summary>
        internal class RogueMulticlassSneakAttackBuilder : BaseDefinitionBuilder<FeatureDefinitionAdditionalDamage>
        {
            const string RogueMulticlassSneakAttackLevel1Name = "RogueMulticlassSneakAttackLevel1";
            private static readonly string RogueMulticlassSneakAttackLevel1NameGuid = GuidHelper.Create(MultiClassBuilderGuid, RogueMulticlassSneakAttackLevel1Name).ToString();
            const string RogueMulticlassSneakAttackLevel3Name = "RogueMulticlassSneakAttackLevel3";
            private static readonly string RogueMulticlassSneakAttackLevel3NameGuid = GuidHelper.Create(MultiClassBuilderGuid, RogueMulticlassSneakAttackLevel3Name).ToString();
            const string RogueMulticlassSneakAttackLevel5Name = "RogueMulticlassSneakAttackLevel5";
            private static readonly string RogueMulticlassSneakAttackLevel5NameGuid = GuidHelper.Create(MultiClassBuilderGuid, RogueMulticlassSneakAttackLevel5Name).ToString();
            const string RogueMulticlassSneakAttackLevel7Name = "RogueMulticlassSneakAttackLevel7";
            private static readonly string RogueMulticlassSneakAttackLevel7NameGuid = GuidHelper.Create(MultiClassBuilderGuid, RogueMulticlassSneakAttackLevel7Name).ToString();
            const string RogueMulticlassSneakAttackLevel9Name = "RogueMulticlassSneakAttackLevel9";
            private static readonly string RogueMulticlassSneakAttackLevel9NameGuid = GuidHelper.Create(MultiClassBuilderGuid, RogueMulticlassSneakAttackLevel9Name).ToString();

            protected RogueMulticlassSneakAttackBuilder(string name, string guid) : base(DatabaseHelper.FeatureDefinitionAdditionalDamages.AdditionalDamageRogueSneakAttack, name, guid)
            {
                Definition.GuiPresentation.Title = "Feature/&RogueMulticlassSneakAttackTitle";
                Definition.GuiPresentation.Description = "Feature/&RogueMulticlassSneakAttackDescription";
                Definition.SetDamageAdvancement(RuleDefinitions.AdditionalDamageAdvancement.None);
            }

            public static FeatureDefinitionAdditionalDamage CreateAndAddToDB(string name, string guid)
                => new RogueMulticlassSneakAttackBuilder(name, guid).AddToDB();

            public static FeatureDefinitionAdditionalDamage RogueMulticlassSneakAttackLevel1 = CreateAndAddToDB(RogueMulticlassSneakAttackLevel1Name, RogueMulticlassSneakAttackLevel1NameGuid);
            public static FeatureDefinitionAdditionalDamage RogueMulticlassSneakAttackLevel3 = CreateAndAddToDB(RogueMulticlassSneakAttackLevel3Name, RogueMulticlassSneakAttackLevel3NameGuid);
            public static FeatureDefinitionAdditionalDamage RogueMulticlassSneakAttackLevel5 = CreateAndAddToDB(RogueMulticlassSneakAttackLevel5Name, RogueMulticlassSneakAttackLevel5NameGuid);
            public static FeatureDefinitionAdditionalDamage RogueMulticlassSneakAttackLevel7 = CreateAndAddToDB(RogueMulticlassSneakAttackLevel7Name, RogueMulticlassSneakAttackLevel7NameGuid);
            public static FeatureDefinitionAdditionalDamage RogueMulticlassSneakAttackLevel9 = CreateAndAddToDB(RogueMulticlassSneakAttackLevel9Name, RogueMulticlassSneakAttackLevel9NameGuid);

        }

        internal class RogueMulticlassExtraSkillPointBuilder : BaseDefinitionBuilder<FeatureDefinitionPointPool>
        {
            const string RogueMulticlassExtraSkillPointName = "RogueMulticlassExtraSkillPoint";
            const string RogueMulticlassExtraSkillPointNameGuid = "84cdba54-5442-4446-bed2-45d865e11145";

            protected RogueMulticlassExtraSkillPointBuilder(string name, string guid) : base(DatabaseHelper.FeatureDefinitionPointPools.PointPoolRogueSkillPoints, name, guid)
            {
                Definition.GuiPresentation.Title = "Feature/&RogueMulticlassExtraSkillPointTitle";
                Definition.GuiPresentation.Description = "Feature/&RogueMulticlassExtraSkillPointDescription";
                Definition.SetPoolAmount(1);
            }

            public static FeatureDefinitionPointPool CreateAndAddToDB(string name, string guid)
                => new RogueMulticlassExtraSkillPointBuilder(name, guid).AddToDB();

            public static FeatureDefinitionPointPool RogueMulticlassExtraSkillPoint = CreateAndAddToDB(RogueMulticlassExtraSkillPointName, RogueMulticlassExtraSkillPointNameGuid);
        }

        internal class RangerMulticlassExtraSkillPointBuilder : BaseDefinitionBuilder<FeatureDefinitionPointPool>
        {
            const string RangerMulticlassExtraSkillPointName = "RangerMulticlassExtraSkillPoint";
            const string RangerMulticlassExtraSkillPointNameGuid = "3389dbff-0544-4681-9b04-8740aed15898";

            protected RangerMulticlassExtraSkillPointBuilder(string name, string guid) : base(DatabaseHelper.FeatureDefinitionPointPools.PointPoolRogueSkillPoints, name, guid)
            {
                Definition.GuiPresentation.Title = "Feature/&RangerMulticlassExtraSkillPointTitle";
                Definition.GuiPresentation.Description = "Feature/&RangerMulticlassExtraSkillPointDescription";
                Definition.SetPoolAmount(1);
            }

            public static FeatureDefinitionPointPool CreateAndAddToDB(string name, string guid)
                => new RangerMulticlassExtraSkillPointBuilder(name, guid).AddToDB();

            public static FeatureDefinitionPointPool RangerMulticlassExtraSkillPoint = CreateAndAddToDB(RangerMulticlassExtraSkillPointName, RangerMulticlassExtraSkillPointNameGuid);
        }

        internal class CastSpellCopyBuilder : BaseDefinitionBuilder<FeatureDefinitionCastSpell>
        {
            protected CastSpellCopyBuilder(string name, string guid, FeatureDefinitionCastSpell castSpellFeatureToCopy) : base(castSpellFeatureToCopy, name, guid)
            {
                Definition.GuiPresentation.Title = name + castSpellFeatureToCopy.Name;
                Definition.GuiPresentation.Description = name + castSpellFeatureToCopy.FormatDescription();
            }

            public static FeatureDefinitionCastSpell CreateAndAddToDB(string name, string guid, FeatureDefinitionCastSpell castSpellFeatureToCopy) => new CastSpellCopyBuilder(name, guid, castSpellFeatureToCopy).AddToDB();
        }
    }
}
