using SolastaModApi;
using SolastaModApi.Extensions;
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

            MultiClassBuilderContext context = new MultiClassBuilderContext();

            definition.GuiPresentation.SetSpriteReference(startingClass.GuiPresentation.SpriteReference);
            definition.AbilityScoresPriority.AddRange(startingClass.AbilityScoresPriority);
            definition.SetClassAnimationId(startingClass.ClassAnimationId);
            definition.SetClassPictogramReference(startingClass.ClassPictogramReference);
            definition.SetDefaultBattleDecisions(startingClass.DefaultBattleDecisions);
            definition.SetGuid(GuidHelper.Create(MultiClassBuilderGuid, definition.GuiPresentation.Title).ToString()); //Should allow for loading the same multiclass through multiple sessions

            //The multi-class won't be quite correct as certain levels will have the wrong hit die.
            //Best way to likely do this is to use the average over all of the levels, though this does ruin front loaded hit dice multiclasses like Fighter1/Wizard9
            definition.EquipmentRows.AddRange(startingClass.EquipmentRows);
            //Add a spellbook for Wizard Multiclasses
            var list = new List<CharacterClassDefinition.HeroEquipmentOption>();
            list.Add(EquipmentOptionsBuilder.Option(DatabaseHelper.ItemDefinitions.Spellbook, EquipmentDefinitions.OptionGenericItem, 1));
            var equipmentColumn = new CharacterClassDefinition.HeroEquipmentColumn();
            equipmentColumn.EquipmentOptions.AddRange(list);
            var equipmentRow = new CharacterClassDefinition.HeroEquipmentRow();
            equipmentRow.EquipmentColumns.Add(equipmentColumn);
            definition.EquipmentRows.Add(equipmentRow);
            definition.ExpertiseAutolearnPreference.AddRange((subsequentClasses.SelectMany(c => c.Item1.ExpertiseAutolearnPreference)));
            definition.FeatAutolearnPreference.AddRange(startingClass.FeatAutolearnPreference);
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

            
            int levelToAddSpellCastFeature = -1;
            FeatureDefinition firstSpellCastFeature = startingClass.FeatureUnlocks.FirstOrDefault(fu => fu.Level == 1 && fu.FeatureDefinition is FeatureDefinitionCastSpell)?.FeatureDefinition;
            FeatureDefinitionCastSpell firstSpellCastFeature2 = null;
            bool firstTimeSpellSetup = true;
            if (firstSpellCastFeature != null)
            {
                levelToAddSpellCastFeature = 1;
                firstSpellCastFeature2 = firstSpellCastFeature as FeatureDefinitionCastSpell;
                SpellSlotHelper.SetupOldData(firstSpellCastFeature2);
                firstSpellCastFeature2.SlotsPerLevels.Clear();
                firstSpellCastFeature2.KnownCantrips.Clear();
                firstSpellCastFeature2.KnownSpells.Clear();
                firstSpellCastFeature2.ScribedSpells.Clear();

                firstSpellCastFeature2.SlotsPerLevels.Add(SpellSlotHelper.OldSpellSlots[0]);
                firstSpellCastFeature2.KnownCantrips.Add(SpellSlotHelper.OldKnownCantrips[0]);
                firstSpellCastFeature2.KnownSpells.Add(SpellSlotHelper.OldKnownSpells[0]);
                firstSpellCastFeature2.ScribedSpells.Add(SpellSlotHelper.OldScribedSpells[0]);

                firstTimeSpellSetup = false;
            }

            List<int> emptySpellSlotDefinition = new List<int>() { 0, 0, 0, 0, 0 };

            context.CurrentClassLevel = 2;
            context.IncrementExistingClassLevel(startingClass);
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

                    if (firstSpellCastFeature != null)
                    {
                        int casterLevel = GetCasterLevelForGivenLevel(context.CurrentClassLevel, startingClass, startingSubclass, subsequentClasses);

                        if (firstTimeSpellSetup)
                        {
                            firstSpellCastFeature2 = firstSpellCastFeature as FeatureDefinitionCastSpell;
                            SpellSlotHelper.SetupOldData(firstSpellCastFeature2);
                            firstSpellCastFeature2.SlotsPerLevels.Clear();
                            firstSpellCastFeature2.KnownCantrips.Clear();
                            firstSpellCastFeature2.KnownSpells.Clear();
                            firstSpellCastFeature2.ScribedSpells.Clear();

                            //If we already have a spellcasting feature then don't bother adding empty levels
                            if (levelToAddSpellCastFeature < 1)
                            {
                                //Add empty spell slots for all levels that didn't have caster levels
                                for (int j = 1; j < context.CurrentClassLevel; j++)
                                {
                                    firstSpellCastFeature2.SlotsPerLevels.Add(new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = j, Slots = emptySpellSlotDefinition });
                                    firstSpellCastFeature2.KnownCantrips.Add(0);
                                    firstSpellCastFeature2.KnownSpells.Add(0);
                                    firstSpellCastFeature2.ScribedSpells.Add(0);
                                }
                            }

                            firstTimeSpellSetup = false;
                        }

                        firstSpellCastFeature2.SlotsPerLevels.Add(SpellSlotHelper.OldSpellSlots[casterLevel - 1]);
                        firstSpellCastFeature2.KnownCantrips.Add(SpellSlotHelper.OldKnownCantrips[casterLevel - 1]);
                        firstSpellCastFeature2.KnownSpells.Add(SpellSlotHelper.OldKnownSpells[casterLevel - 1]);
                        firstSpellCastFeature2.ScribedSpells.Add(SpellSlotHelper.OldScribedSpells[casterLevel - 1]);


                        if (levelToAddSpellCastFeature < 1)
                            levelToAddSpellCastFeature = context.CurrentClassLevel;
                    }
                    

                    context.CurrentClassLevel++;
                    context.IncrementExistingClassLevel(classAndLevels.Item1);
                }
            }
            int endCasterLevel = GetCasterLevelForGivenLevel(context.CurrentClassLevel, startingClass, startingSubclass, subsequentClasses);

            if (firstSpellCastFeature2 != null)
            {
                while (firstSpellCastFeature2.SlotsPerLevels.Count < 20)
                {
                    firstSpellCastFeature2.SlotsPerLevels.Add(SpellSlotHelper.OldSpellSlots[endCasterLevel - 1]);
                    firstSpellCastFeature2.KnownCantrips.Add(SpellSlotHelper.OldKnownCantrips[endCasterLevel - 1]);
                    firstSpellCastFeature2.KnownSpells.Add(SpellSlotHelper.OldKnownSpells[endCasterLevel - 1]);
                    firstSpellCastFeature2.ScribedSpells.Add(SpellSlotHelper.OldScribedSpells[endCasterLevel - 1]);
                }
                definition.FeatureUnlocks.Add(new FeatureUnlockByLevel(firstSpellCastFeature2, levelToAddSpellCastFeature));
            }
            SpellSlotHelper.ClearOldData();          

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

        private static int GetCasterLevelForGivenLevel(int currentLevel, CharacterClassDefinition startingClass, CharacterSubclassDefinition startingSubclass, IEnumerable<Tuple<CharacterClassDefinition, CharacterSubclassDefinition, int>> subsequentClasses)
        {
            var context = new CasterLevelContext();
            context.IncrementCasterLevel(GetCasterLevelForSingleLevelOfClass(startingClass, startingSubclass));

            int numLevelsRemaining = currentLevel -1;
            foreach(var subseqeuntClass in subsequentClasses)
            {
                int numLevelsToUseFromNextClass = Math.Min(numLevelsRemaining, subseqeuntClass.Item3);
                for (int i = numLevelsToUseFromNextClass; i > 0; i--)
                {
                    context.IncrementCasterLevel(GetCasterLevelForSingleLevelOfClass(subseqeuntClass.Item1, subseqeuntClass.Item2));
                    numLevelsRemaining--;
                }
            }

            return context.GetCasterLevel();
        }

        private static eAHCasterType GetCasterLevelForSingleLevelOfClass(CharacterClassDefinition charClass, CharacterSubclassDefinition subclass)
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

            public int GetCasterLevel()
            {
                int casterLevel = 0;
                if (NumOneThirdLevels >= 3)
                    casterLevel += NumOneThirdLevels/3;
                if (NumHalfLevels >= 2)
                    casterLevel += NumHalfLevels/2;
                casterLevel += NumFullLevels;

                return casterLevel;
            }

            int NumOneThirdLevels = 0;
            int NumHalfLevels = 0;
            int NumFullLevels = 0;
        }

        public enum eAHCasterType
        {
            None,
            OneThird,
            Half,
            Full
        };

        public static class SpellSlotHelper
        {
            public static List<FeatureDefinitionCastSpell.SlotsByLevelDuplet> OldSpellSlots = new List<FeatureDefinitionCastSpell.SlotsByLevelDuplet>();
            public static List<int> OldKnownCantrips = new List<int>();
            public static List<int> OldKnownSpells = new List<int>();
            public static List<int> OldScribedSpells = new List<int>();

            public static void SetupOldData(FeatureDefinitionCastSpell castSpellDefinition)
            {
                OldSpellSlots.AddRange(castSpellDefinition.SlotsPerLevels);
                OldKnownCantrips.AddRange(castSpellDefinition.KnownCantrips);
                OldKnownSpells.AddRange(castSpellDefinition.KnownSpells);
                OldScribedSpells.AddRange(castSpellDefinition.ScribedSpells);
            }

            public static void ClearOldData()
            {
                OldSpellSlots.Clear();
                OldKnownCantrips.Clear();
                OldKnownSpells.Clear();
                OldScribedSpells.Clear();
            }

            //public static List<int> GetSlotsForCasterLevel(int casterLevel)
            //{
            //    return SpellSlotsByCasterLevel[casterLevel - 1].Slots;
            //}

            //public static FeatureDefinitionCastSpell.SlotsByLevelDuplet[] SpellSlotsByCasterLevel = new FeatureDefinitionCastSpell.SlotsByLevelDuplet[]
            //{
            //    new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = 1, Slots = { 2,0,0,0,0 }},
            //    new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = 2, Slots = { 3,0,0,0,0 }},
            //    new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = 3, Slots = { 4,2,0,0,0 }},
            //    new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = 4, Slots = { 4,3,0,0,0 }},
            //    new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = 5, Slots = { 4,3,2,0,0 }},
            //    new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = 6, Slots = { 4,3,3,0,0 }},
            //    new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = 7, Slots = { 4,3,3,1,0 }},
            //    new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = 8, Slots = { 4,3,3,2,0 }},
            //    new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = 9, Slots = { 4,3,3,2,1 }},
            //    new FeatureDefinitionCastSpell.SlotsByLevelDuplet() { Level = 10, Slots = { 4,3,3,2,2 }},
            //    //We could do higher levels but not necessary yet/not done in Solasta either
            //};
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
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellPaladin,
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellRanger,
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellShadowcaster,
            DatabaseHelper.FeatureDefinitionCastSpells.CastSpellMartialSpellBlade,
        };

        public class MultiClassBuilderContext
        {
            public MultiClassBuilderContext()
            {
                NumTotalClassLevels = 0;
                CurrentClassLevel = 1;
                NumExistingClericLevels = 0;
                NumExistingFighterLevels = 0;
                NumExistingPaladinLevels = 0;
                NumExistingRangerLevels = 0;
                NumExistingRogueLevels = 0;
                NumExistingWizardLevels = 0;
            }

            public void IncrementExistingClassLevel(CharacterClassDefinition characterClass)
            {
                if (characterClass == DatabaseHelper.CharacterClassDefinitions.Cleric)
                    NumExistingClericLevels++;
                else if (characterClass == DatabaseHelper.CharacterClassDefinitions.Fighter)
                    NumExistingFighterLevels++;
                else if (characterClass == DatabaseHelper.CharacterClassDefinitions.Paladin)
                    NumExistingPaladinLevels++;
                else if (characterClass == DatabaseHelper.CharacterClassDefinitions.Ranger)
                    NumExistingRangerLevels++;
                else if (characterClass == DatabaseHelper.CharacterClassDefinitions.Rogue)
                    NumExistingRogueLevels++;
                else if (characterClass == DatabaseHelper.CharacterClassDefinitions.Wizard)
                    NumExistingWizardLevels++;
                else if (string.Equals(characterClass.Name, "AHBarbarianClass"))
                    NumExistingBarbarianLevels++;
            }

            public int GetExistingClassLevel(CharacterClassDefinition characterClass)
            {
                if (characterClass == DatabaseHelper.CharacterClassDefinitions.Cleric)
                    return NumExistingClericLevels;
                else if (characterClass == DatabaseHelper.CharacterClassDefinitions.Fighter)
                    return NumExistingFighterLevels;
                else if (characterClass == DatabaseHelper.CharacterClassDefinitions.Paladin)
                    return NumExistingPaladinLevels;
                else if (characterClass == DatabaseHelper.CharacterClassDefinitions.Ranger)
                    return NumExistingRangerLevels;
                else if (characterClass == DatabaseHelper.CharacterClassDefinitions.Rogue)
                    return NumExistingRogueLevels;
                else if (characterClass == DatabaseHelper.CharacterClassDefinitions.Wizard)
                    return NumExistingWizardLevels;
                else if (string.Equals(characterClass.Name, "AHBarbarianClass"))
                    return NumExistingBarbarianLevels;

                return -1;//Error
            }

            public int NumTotalClassLevels = 0;
            public int CurrentClassLevel = 1;
            public int NumExistingClericLevels = 0;
            public int NumExistingFighterLevels = 0;
            public int NumExistingPaladinLevels = 0;
            public int NumExistingRangerLevels = 0;
            public int NumExistingRogueLevels = 0;
            public int NumExistingWizardLevels = 0;
            public int NumExistingBarbarianLevels = 0;
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
    }
}
