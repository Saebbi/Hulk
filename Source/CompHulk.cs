using AbilityUser;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace Hulk
{
    /// 
    /// CompHulk
    /// Werewolves in RimWorld
    /// ------------
    /// 
    /// Summary: 
    ///   Adds systems for handling Werewolves in-game.
    ///   
    /// Detailed Summary:
    ///   This is [a thing-attachable object that holds data and has functions that can trigger actively 
    ///   on a per tick basis] (known hereafter as --Component--) while the character (--Pawn--) is in
    ///   the game's playing field (--Spawned--).
    ///   
    ///   This Component handles cases where Spawned Pawns have the Werewolf Trait in their Character Sheet.
    ///   
    ///   There are two CompWerewolf states:
    ///      Unblooded  :   First time WWP (Werewolf Pawn).
    ///      Blooded    :   WWP that has been triggered by a full moon event at least once before.
    /// 
    ///   When =WorldComponent_MoonCycle= ticks through its list of =Moon= class objects and a
    ///   =GameCondition_FullMoon= (--full moon--) is declared, Unblooded WPPs will have their mind state
    ///   forced into MentalStateDef WerewolfFury. They will be given =JobGiver_WerewolfHunt=, which is
    ///   a customized version of the original RimWorld's =JobGiver_Manhunter=, but focused on breaking doors
    ///   and killing targets. Then they will transform randomly into one of the four Werewolf Forms:
    ///   
    ///   Werewolf Forms:
    ///     Glabro  :   The Near Man Form
    ///     Crinos  :   The Battle Form
    ///     Hispo   :   The Near Wolf Form
    ///     Lupus   :   The Wolf Form
    ///     
    ///   =HediffComp_Rage= handles the duration of the transformation, and this class' methods handle most
    ///   cases for transformation issues (e.g. upper body items and weapons are stored away in temporary lists).
    ///   
    ///   Once WWPs become Blooded, they may transform at will. Each transformation from a full moon fury causes
    ///   the Werewolf Form to level up. =WerewolfFormDef= holds the variables for the rates of level progression.
    ///   ==WerewolfForm== and ==HarmonyPatches== handle cases for applying that progression (e.g. WerewolfBodySize).
    ///   
    /// 
    public class CompHulk : CompAbilityUser
    {
        #region Variables and Properties

        public enum State : int
        {
            Unblooded = 0,
            Hulk = 1
        }

        #region Variables
        private bool furyToggled = false;
        private bool factionResolved = false;
        private bool needsGraphicRefresh = true;
        private bool isReverting = false;
        private bool? isBlooded = null;
        private int cooldownTicksLeft = -1;
        private HulkForm currentHulkForm = null;
        private List<HulkForm> hulkForms = null;
        private List<ThingWithComps> storedWeapons = new List<ThingWithComps>();
        private List<Apparel> upperBodyItems = new List<Apparel>();
        #endregion Variables

        #region Properties
        //Simple Properties
        public bool FuryToggled => furyToggled;
        public bool IsReverting { get => isReverting; set => isReverting = value; }
        public int CooldownMaxTicks => GenDate.TicksPerHour * 6;
        public int CooldownTicksLeft { get => cooldownTicksLeft; set => cooldownTicksLeft = value; }
        public HulkForm CurrentHulkForm { get => currentHulkForm; set => currentHulkForm = value; }
        public List<ThingWithComps> StoredWeapons { get => storedWeapons; set => storedWeapons = value; }
        public List<Apparel> UpperBodyItems { get => upperBodyItems; set => upperBodyItems = value; }
        public List<HulkForm> HulkForms
        {
            set { HulkForms = value; }
            get
            {
                if (hulkForms.NullOrEmpty() && IsHulk)
                {
                    hulkForms = new List<HulkForm>
                        {
                        new HulkForm(HulkDefOf.Hulk, 0, Pawn),
                        };
                }
                return hulkForms;
            }
        }
        public bool IsBlooded
        {
            get
            {
                if (isBlooded == null)
                {
                    isBlooded = false;
                    if (this?.Pawn?.story?.traits?.GetTrait(HulkDefOf.ROM_Hulk)?.Degree > 1)
                        isBlooded = true;
                }
                return isBlooded.Value;
            }
            set => isBlooded = value;
        }

        //Utilitarian Properties
        public bool IsSimpleSidearmsLoaded => (ModLister.AllInstalledMods.FirstOrDefault(x => x.Name == "Simple sidearms" && x.Active) != null);
        public bool IsTransformed => CurrentHulkForm != null;
        public bool IsHulk { get => HulkTrait != null; }
        public bool CanTransformNow => IsHulk && !IsTransformed && CooldownTicksLeft <= 0;
        public Vector3 Vec3 => Pawn.PositionHeld.ToVector3();
        public Map Map => Pawn.MapHeld;
        public Trait HulkTrait => Pawn?.story?.traits?.GetTrait(HulkDefOf.ROM_Hulk);
        public HulkForm HighestLevelForm => HulkForms.MaxBy(x => x.level);
        #endregion Properties

        #endregion Variables and Properties

        #region Methods

        #region Transform Methods

        /// Sets the current werewolf form and calls other sub-methods.
        public void TransformInto(HulkForm form, bool moonTransformation = false)
        {
            CurrentHulkForm = form;
            if (Pawn is Pawn p)
            {
                if (p.Faction == Faction.OfPlayer) p.ClearMind();
                if (form != null)
                {
                    //Log.Message("ResolveTransformationEffects");
                    ResolveTransformEffects(p, currentHulkForm, moonTransformation);
                    ResolveEquipmentStorage();
                    return;
                }
                //Log.Message("Restore");
                RestoreEquipment();
                p.Drawer.renderer.graphics.ResolveAllGraphics();
                Messages.Message("ROM_WerewolfRevert".Translate(Pawn), MessageTypeDefOf.SilentInput);//MessageTypeDefOf.SilentInput);
            }
            isReverting = false;
        }

        public override float CombatPoints()
        {
            //Log.Message("Combat points called");
            if (forbiddenHulkhood) return 0;
            if (HulkForms.NullOrEmpty()) return 400;
            var combatPoints = HulkForms.Max(x => x.level) * 400;
            //Log.Message("combatPoints: " + combatPoints);
            return combatPoints;
        }

        private bool forbiddenHulkhood = false;
        public override void DisableAbilityUser()
        {
            Pawn.story.traits.allTraits.Remove(HulkTrait);
            forbiddenHulkhood = true;
        }

        /// Gives a random new transformation from full moon furies.
        public void TransformRandom(bool moonTransformation)
        {
            HulkForm formToTake = ResolveRandomHulkForm();
            TransformInto(formToTake, moonTransformation);
        }

        /// Restores the original form of the Pawn.
        public void TransformBack(bool killed = false)
        {
            //If killed, make one last howl.
            isReverting = true;
            if (killed && currentHulkForm.def.transformSound is SoundDef howl)
                howl.PlayOneShot(new TargetInfo(Pawn.PositionHeld, Pawn.MapHeld, false));

            //Clear the graphic cache.
            if (HulkForms.FirstOrDefault(x => x.bodyGraphicData != null) is HulkForm f)
            {
                f.bodyGraphicData = null;
            }

            //Remove the health differentials.
            ResolveClearHediffs();

            //Heal some of the injuries to avoid instant death when reverting back to humanoid form.
            if (!Pawn.Dead && Pawn?.health?.hediffSet is HediffSet health)
            {
                for (int i = 0; i < (health?.GetInjuredParts().Count() ?? 0); i++)
                {
                    BodyPartRecord rec = health.GetInjuredParts().ElementAt(i);
                    List<Hediff_Injury> injuriesToHeal = health?.GetHediffs<Hediff_Injury>().ToList().FindAll(x => x.Part == rec);
                    if (!injuriesToHeal.NullOrEmpty())
                    {
                        foreach (Hediff_Injury current in injuriesToHeal)
                        {
                            if (current.CanHealNaturally() && !current.IsOld()) // basically check for scars and old wounds
                            {
                                current.Severity -= current.Severity * 0.80f;
                            }
                        }
                    }
                }
                if (health?.hediffs?.FirstOrDefault(x => x.def == HediffDefOf.BloodLoss) is Hediff bloodLoss)
                {
                    bloodLoss.Severity = 0;
                }
            }

            //Trigger a null transformation
            TransformInto(null, false);
        }

        #region Transform Effects

        /// Burst out of any containers (e.g. Cryptosleep pods)
        private void ResolveContainment()
        {
            IntVec3 spawnLoc = Pawn.PositionHeld;
            if (spawnLoc == IntVec3.Invalid) spawnLoc = Pawn.Position;
            if (spawnLoc == IntVec3.Invalid) spawnLoc = DropCellFinder.RandomDropSpot(Map);

            //Hops / Other storage buildings
            if (StoreUtility.StoringBuilding(Pawn) is Building building)
            {
                if (building is Building_Storage buildingS)
                {
                    buildingS.Notify_LostThing(Pawn);
                }

            }
            if (Pawn.holdingOwner?.Owner is Building_Casket casket)
            {
                casket.EjectContents();
                Messages.Message("ROM_WerewolfEscaped".Translate(new object[] {
                        Pawn.LabelShort,
                        casket.Label
                    }), new RimWorld.Planet.GlobalTargetInfo(Pawn), MessageTypeDefOf.SilentInput);
            }
        }

        /// Drop all bionic parts when transforming.
        private void ResolveBionics(HulkForm currentHulkForm)
        {
            if (!Pawn.Dead)
            {

                foreach (BodyPartRecord rec in Pawn.health.hediffSet.GetNotMissingParts())
                {
                    Hediff_AddedPart hediff_AddedPart = (from x in Pawn.health.hediffSet.GetHediffs<Hediff_AddedPart>()
                                                         where x.Part == rec && !x.Part.def.tags.Contains("ConsciousnessSource")
                                                         select x).FirstOrDefault<Hediff_AddedPart>();
                    if (hediff_AddedPart != null)
                    {
                        if (!this.HulkForms.Any(x => x.level >= 5) || PartCanBeHulkPart(hediff_AddedPart.Part) || hediff_AddedPart?.def?.addedPartProps?.partEfficiency < 1f)
                        {
                            bool showMessage = true;
                            /// VAMPIRISM: Hide Lose Fangs Message ////////////////////////
                            try
                            {
                                if (LoadedModManager.RunningMods.FirstOrDefault(x => x.Name.Contains("Rim of Madness - Vampires")) != null)
                                {
                                    AreFangs(hediff_AddedPart);
                                    showMessage = false;
                                }
                            }
                            catch (System.TypeLoadException)
                            {
                                //Log.Message(e.toString());
                            }
                            /// ////////////////////////////////////////////////////////////
                            HulkUtility.SpawnNaturalPartIfClean(Pawn, rec, Pawn.Position, Pawn.Map);
                            HulkUtility.SpawnThingsFromHediffs(Pawn, rec, Pawn.Position, Pawn.Map);
                            Pawn.health.hediffSet.hediffs.Remove(hediff_AddedPart);
                            Pawn.health.RestorePart(rec);
                            if (Pawn.Faction == Faction.OfPlayer && showMessage)
                            {
                                Messages.Message("ROM_WerewolfDecyberize".Translate(new object[]
                                {
                                Pawn.LabelShort,
                                rec.def.label,
                                hediff_AddedPart.Label
                                }), MessageTypeDefOf.NegativeEvent);//MessageTypeDefOf.NegativeEvent);

                                LessonAutoActivator.TeachOpportunity(HulkDefOf.Hulk_ConceptBionics, this.Pawn, OpportunityType.Critical);
                            }
                        }
                    }
                }
                
            }
        }

        private bool AreFangs(Hediff_AddedPart addedPart) => addedPart is Vampire.Hediff_AddedPart_Fangs;


        /// Restores all missing parts when transforming
        private void ResolveMissingParts(Pawn p)
        {
            List<Hediff_MissingPart> missingParts = new List<Hediff_MissingPart>().Concat(p?.health?.hediffSet?.GetMissingPartsCommonAncestors()).ToList();
            if (!missingParts.NullOrEmpty())
            {
                foreach (Hediff_MissingPart part in missingParts)
                {
                    p.health.RestorePart(part.Part);
                    Messages.Message("ROM_WerewolfLimbRegen".Translate(new object[] {
                        p.LabelShort,
                        part.Label
                    }), MessageTypeDefOf.PositiveEvent);//MessageSound.Benefit);
                }
            }
        }

        /// Adds the primary Werewolf stats.
        private void ResolveHulkStatMods(Pawn p, HulkForm currentHulkForm, bool moonTransformation = false)
        {
            //Gives Werewolf full-body stats.
            Hediff formHediff = HediffMaker.MakeHediff(currentHulkForm.def.formHediff, p);
            formHediff.Severity = 1.0f;
            p.health.AddHediff(formHediff, null, null);
        }

        public bool PartCanBeHulkPart(BodyPartRecord part)
            => PartCanBeHulkLeftFist(part) || PartCanBeHulkRightFist(part);

        public bool PartCanBeHulkLeftFist(BodyPartRecord part)
            => part.def == BodyPartDefOf.LeftHand;

        public bool PartCanBeHulkRightFist(BodyPartRecord part)
            => part.def == BodyPartDefOf.RightHand;

        /// Adds the Werewolf jaws and claws to each respective body part.
        private void ResolveTransformedHediffs(Pawn p, HulkForm currentHulkForm, bool moonTransformation = false)
        {

            IEnumerable<BodyPartRecord> recs = p.health.hediffSet.GetNotMissingParts();
            Dictionary<BodyPartRecord, HediffDef> bodyPartRecords = new Dictionary<BodyPartRecord, HediffDef>();
            if (recs.FirstOrDefault(x => PartCanBeHulkLeftFist(x)) is BodyPartRecord leftHand)
                bodyPartRecords.Add(leftHand, currentHulkForm.def.clawHediff);
            if (recs.FirstOrDefault(x => PartCanBeHulkRightFist(x)) is BodyPartRecord rightHand)
                bodyPartRecords.Add(rightHand, currentHulkForm.def.clawHediff);

            if ((bodyPartRecords?.Count() ?? 0) > 0)
            {
                foreach (KeyValuePair<BodyPartRecord, HediffDef> transformableParts in bodyPartRecords)
                {
                    Hediff transformedHediff = HediffMaker.MakeHediff(transformableParts.Value, p, transformableParts.Key);
                    transformedHediff.Severity = 1.0f;
                    p.health.AddHediff(transformedHediff, transformableParts.Key, null);
                }
            }
            HealthUtility.AdjustSeverity(p, HediffDefOf.BloodLoss, -9999);
        }

        /// Adds a fury to the Werewolf. 
        private void ResolveFury(Pawn p, HulkForm currentHulkForm, bool moonTransformation = false)
        {
            //Give Werewolf fury during a full moon event IF
            //1) Werewolf is UNBLOODED
            //2) Fury mode is ENABLED
            bool giveFuryMentalState = false;
            if (moonTransformation)
            {
                if (!IsBlooded) { IsBlooded = true; giveFuryMentalState = true; }
                if (FuryToggled) { giveFuryMentalState = true; }
                currentHulkForm.LevelUp();
            }

            if (giveFuryMentalState)
                p.mindState.mentalStateHandler.TryStartMentalState(HulkDefOf.HulkFury, "ROM_MoonCycle_FullMoonArgless".Translate(), true);


        }

        /// Apply Werewolf stats and gives visual/audio feedback to players when transforming.
        private void ResolveTransformEffects(Pawn p, HulkForm currentHulkForm, bool moonTransformation = false)
        {
            if (moonTransformation) ResolveContainment();
            ResolveBionics(currentHulkForm);
            ResolveMissingParts(p);
            ResolveHulkStatMods(p, currentHulkForm, moonTransformation);
            ResolveTransformedHediffs(p, currentHulkForm, moonTransformation);
            ResolveFury(p, currentHulkForm, moonTransformation);

            //--------------------------------
            //     FEEDBACK (GFX/SFX/MSG)
            //--------------------------------

            //Pre-transformation Effects
            MoteMaker.ThrowAirPuffUp(Vec3, Map);
            MoteMaker.ThrowSmoke(Vec3, Map, 2.0f);

            //ResolveAllGraphics is patched in HarmonyPatches.
            //This changes the graphic of a character into a Werewolf.
            p.Drawer.renderer.graphics.ResolveAllGraphics();

            if (currentHulkForm.def.transformSound is SoundDef howl)
                howl.PlayOneShot(new TargetInfo(p.PositionHeld, p.MapHeld, false));

            Messages.Message("ROM_WerewolfTransformation".Translate(new object[]
            {
                        p.LabelShort,
                        currentHulkForm.def.label
            }), MessageTypeDefOf.SilentInput);


        }

        /// Clears up remaining hediffs
        private void ResolveClearHediffs()
        {
            List<Hediff> hediffTemps = new List<Hediff>();
            hediffTemps.AddRange(Pawn?.health?.hediffSet?.hediffs);
            if ((Pawn?.health?.hediffSet?.hediffs.Count() ?? 0) > 0)
            {
                foreach (Hediff hediff in hediffTemps)
                {
                    if (hediff.def == CurrentHulkForm.def.clawHediff ||
                        hediff.def == CurrentHulkForm.def.formHediff)
                    {
                        Pawn.health.RemoveHediff(hediff);
                    }
                    if (hediff.def == CurrentHulkForm.def.jawHediff)
                    {
                        Pawn.health.RemoveHediff(hediff);
                        /// VAMPIRES: Readd Fangs if available
                        try
                        {
                            if (LoadedModManager.RunningMods.FirstOrDefault(x => x.Name.Contains("Rim of Madness - Vampires")) != null)
                                ReAddVampireFangs();
                        }
                        catch (System.TypeLoadException)
                        {
                            //Log.Message(e.toString());
                        }
                        /// 
                    }
                }
            }
            hediffTemps.Clear();
            hediffTemps = null;
        }

        private void ReAddVampireFangs()
        {
            if (Vampire.VampireUtility.IsVampire(Pawn))
            {
                Vampire.VampireGen.AddFangsHediff(Pawn);
            }
        }

        #region Equipment Handlers

        /// Moves the equipped weapons into the inventory section
        private void ResolveWeaponStorage(Pawn_InventoryTracker inventory)
        {
            //Put away all weapons.
            if (Pawn?.equipment?.AllEquipmentListForReading is List<ThingWithComps> equipment && !equipment.NullOrEmpty())
            {
                List<ThingWithComps> temp = new List<ThingWithComps>();
                temp.AddRange(equipment);
                foreach (ThingWithComps c in temp)
                {
                    ThingWithComps d = null;
                    if (Pawn.equipment.TryDropEquipment(c, out d, Pawn.PositionHeld, false) && d != null)
                    {
                        StoredWeapons.Add(d);
                        inventory.innerContainer.InnerListForReading.Add(d);
                        if (d.Spawned) d.DeSpawn();
                    }
                }
            }
        }

        /// Moves the equipped apparel into the inventory section
        private void ResolveApparelStorage(Pawn_InventoryTracker inventory)
        {
            //Take off upper body items.
            if (Pawn?.apparel?.WornApparel is List<Apparel> apparel && !apparel.NullOrEmpty())
            {
                List<Apparel> temp = new List<Apparel>(apparel);
                foreach (Apparel c in temp)
                {
                    Apparel d = null;
                    if (c?.def?.apparel?.bodyPartGroups is List<BodyPartGroupDef> groups &&
                        !groups.NullOrEmpty() &&
                        (
                        groups.Contains(BodyPartGroupDefOf.Torso) ||
                        groups.Contains(BodyPartGroupDefOf.FullHead) ||
                        groups.Contains(BodyPartGroupDefOf.LeftHand) ||
                        groups.Contains(BodyPartGroupDefOf.RightHand)
                        )
                        )
                    {
                        if (Pawn.apparel.TryDrop(c, out d, Pawn.PositionHeld, false) && d != null)
                        {
                            upperBodyItems.Add(d);
                            Pawn.inventory.innerContainer.InnerListForReading.Add(d);
                            if (d.Spawned) d.DeSpawn();
                        }
                    }
                }
            }
        }

        /// Unequip and store equipment/apparel in a list while transformed into a werewolf.
        private void ResolveEquipmentStorage()
        {
            //Clear previous lists.
            StoredWeapons = new List<ThingWithComps>();
            UpperBodyItems = new List<Apparel>();


            if (Pawn?.inventory is Pawn_InventoryTracker inventory)
            {
                if (!IsSimpleSidearmsLoaded)
                {
                    ResolveWeaponStorage(inventory);
                    ResolveApparelStorage(inventory);
                }
                else if (CurrentHulkForm != null)
                {
                    if (Pawn.apparel.WornApparel is List<Apparel> apps && !apps.NullOrEmpty())
                    {
                        List<Apparel> temp = new List<Apparel>(apps);
                        foreach (Apparel app in temp)
                        {
                            Pawn.apparel.TryDrop(app, out Apparel s, Pawn.PositionHeld);
                        }
                    }
                    if (Pawn.equipment.AllEquipmentListForReading is List<ThingWithComps> weps && !weps.NullOrEmpty())
                    {
                        List<ThingWithComps> temp = new List<ThingWithComps>(weps);
                        foreach (ThingWithComps wep in temp)
                        {
                            Pawn.equipment.TryDropEquipment(wep, out ThingWithComps thing, Pawn.PositionHeld);
                        }
                    }
                }
            }

        }

        /// Equip previously stored equipment after reverting back into the original form.
        private void RestoreEquipment()
        {
            Pawn p = Pawn;

            //Cycle through old weapons and apparel and restore them.
            if (p?.inventory is Pawn_InventoryTracker invTracker)
            {
                //Equip all weapons.
                if (!StoredWeapons.NullOrEmpty())
                {
                    if (p?.equipment is Pawn_EquipmentTracker equipTracker)
                    {

                        foreach (ThingWithComps c in storedWeapons)
                        {
                            if (invTracker.innerContainer.InnerListForReading.Contains(c) &&
                                !equipTracker.Contains(c))
                            {
                                invTracker.innerContainer.InnerListForReading.Remove(c);
                                c.holdingOwner = null;
                                equipTracker.AddEquipment(c);
                                //Log.Message(c.ToString());
                            }
                        }

                        if (!storedWeapons.NullOrEmpty())
                        {
                            foreach (ThingWithComps c in storedWeapons)
                            {
                                if (!equipTracker.Contains(c))
                                {
                                    c.holdingOwner = null;
                                    equipTracker.AddEquipment(c);
                                    //Log.Message(c.ToString());
                                }

                            }
                        }

                    }
                }

                //Wear all pre-transformation apparel
                if (!UpperBodyItems.NullOrEmpty())
                {
                    if (p?.apparel is Pawn_ApparelTracker apparelTracker)
                    {
                        HashSet<Apparel> tempItems = new HashSet<Apparel>(UpperBodyItems);
                        foreach (Apparel a in tempItems)
                        {
                            if (invTracker.innerContainer.InnerListForReading.Contains(a) &&
                                !apparelTracker.Contains(a))
                            {
                                invTracker.innerContainer.InnerListForReading.Remove(a);
                                a.holdingOwner = null;
                                apparelTracker.Wear(a);
                                //Log.Message(a.ToString());
                                UpperBodyItems.Remove(a);
                            }
                        }
                        if (!UpperBodyItems.NullOrEmpty())
                        {
                            foreach (Apparel a in UpperBodyItems)
                            {
                                if (!apparelTracker.Contains(a))
                                {
                                    a.holdingOwner = null;
                                    apparelTracker.Wear(a);
                                    //Log.Message(a.ToString());
                                }

                            }
                        }

                    }
                }

            }

        }

        #endregion Equipment Handlers

        #endregion Transform Effects

        #endregion Transform Methods

        #region AI Methods
        /// Transform AI Werewolves when they are in danger.
        public void ResolveAIHostileReaction()
        {
            if (Find.TickManager.TicksGame % 250 == 0 && Pawn.Faction != Faction.OfPlayer &&
                Pawn.mindState.enemyTarget != null && CanTransformNow)
                TransformInto(HighestLevelForm, false);
        }

        /// Give AI Werewolves levels in different forms.
        public void ResolveAIFactionSpawns()
        {
            if (!factionResolved && Pawn?.Faction?.def?.defName == "HulkClan" && Pawn?.kindDef?.defName != "ROM_HulkStraggler" && !forbiddenHulkhood)
            {
                factionResolved = true;


                if (!Pawn.story.traits.HasTrait(HulkDefOf.ROM_Hulk))
                {
                    Trait newTrait = new Trait(HulkDefOf.ROM_Hulk, 2, true);
                    if (Pawn.kindDef.defName == "Incredible_Hulk")
                    {
                        newTrait = new Trait(HulkDefOf.ROM_Hulk, -1, true);

                    }

                    Trait toRemove = Pawn.story.traits.allTraits.RandomElement();
                    Pawn.story.traits.allTraits.Remove(toRemove);
                    Pawn.story.traits.GainTrait(newTrait);
                }

                //Give random werewolf abilities
                switch (Pawn.kindDef.defName)
                {
                    case "Hulk":
                        LevelUp(HulkDefOf.Hulk, Rand.Range(3, 4));
                        break;
                }

                //Manage the AI sensibly.
                if (Pawn?.mindState?.duty?.def == DutyDefOf.AssaultColony && IsBlooded)
                    Pawn.mindState.duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("HulkAssault"));
            }
        }

        #endregion AI Methods

        /// Sets the level of a specific Werewolf type.
        public void LevelUp(HulkFormDef def, int level)
        {
            HulkForm hulkForm = HulkForms.FirstOrDefault(x => x.def == def);
            hulkForm.level = level;
        }

        #endregion Methods

        #region Pawn Overrides
        //public override void PostPreApplyDamage(DamageInfo dinfo, out bool absorbed)
        //{

        //}

        /// 
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            //foreach (Gizmo x in base.CompGetGizmosExtra())
            //{
            //    yield return x;
            //}

            if (IsHulk && !IsTransformed && IsBlooded)
            {
                if (DebugSettings.godMode)
                {

                    if (Find.Selector.NumSelected == 1)
                    {
                        Command_Action command_Action = new Command_Action()
                        {
                            action = delegate
                            {
                                List<FloatMenuOption> list = new List<FloatMenuOption>();
                                foreach (HulkForm current in HulkForms)
                                {
                                    list.Add(new FloatMenuOption("Level up " + current.def.LabelCap, delegate
                                    {
                                        current.LevelUp();
                                    }, MenuOptionPriority.Default));
                                }
                                Find.WindowStack.Add(new FloatMenu(list));
                            },
                            defaultLabel = "(God Mode) Level Up Forms",
                            defaultDesc = "",
                            hotKey = KeyBindingDefOf.Misc1,
                            icon = TexCommand.ClearPrioritizedWork,
                        };
                        yield return command_Action;
                    }
                }



                if (Find.Selector.NumSelected == 1)
                {
                    foreach (HulkForm form in HulkForms)
                    {
                        if (form.level > 0)
                        {
                            Command_HulkButton hulkFormButton = new Command_HulkButton(this)
                            {
                                defaultLabel = form.def.label + " Lv." + form.level,
                                defaultDesc = form.Desc,
                                icon = form.def.Icon,
                                action = delegate
                                {
                                    TransformInto(form);
                                    CooldownTicksLeft = CooldownMaxTicks;
                                }
                            };
                            if (CooldownTicksLeft > 0)
                            {
                                wolfFormButton.Disable("ROM_NeedsRest".Translate(new object[]
                                {
                                Pawn.LabelShort
                                }));
                            }
                            yield return hulkFormButton;
                        }
                    }
                }
                yield return new Command_Toggle()
                {
                    defaultLabel = "ROM_FullMoonFury".Translate(),
                    defaultDesc = "ROM_FullMoonFuryDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Button/MoonFury", true),
                    isActive = (() => furyToggled),
                    toggleAction = delegate
                    {
                        furyToggled = !furyToggled;
                    }
                };
            }
            else if (IsTransformed && Pawn?.health?.hediffSet?.GetHediffs<Hediff>().FirstOrDefault(x => x.TryGetComp<HediffComp_Rage>() != null) is Hediff rageHediff &&
                rageHediff.TryGetComp<HediffComp_Rage>() is HediffComp_Rage rageComp)
            {
                yield return new Gizmo_HediffRageStatus()
                {
                    rage = rageComp,
                };
            }

        }
        public override void CompTick()
        {
            base.CompTick();
            if (Pawn.Spawned)// && needsGraphicRefresh)
            {
                ResolveAIFactionSpawns();

                if (IsHulk)
                {
                    if (CooldownTicksLeft > 0)
                        CooldownTicksLeft--;

                    ResolveAIHostileReaction();

                    if (needsGraphicRefresh)
                    {
                        if (HulkForms.FirstOrDefault(x => x.bodyGraphicData != null) is HulkForm f)
                        {
                            f.bodyGraphicData = null;
                        }
                        Pawn.Drawer.renderer.graphics.ResolveAllGraphics();
                        needsGraphicRefresh = false;
                    }
                }
            }

        }
        public override bool TryTransformPawn() => IsHulk || Pawn?.Faction?.def?.defName == "HulkClan";
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<bool>(ref factionResolved, "factionResolved", false);
            Scribe_Values.Look<bool>(ref furyToggled, "furyToggled", false);
            Scribe_Values.Look<bool>(ref isReverting, "isReverting", false);
            Scribe_Values.Look<bool?>(ref isBlooded, "isBlooded", null);
            Scribe_Values.Look<int>(ref cooldownTicksLeft, "cooldownTicksLeft", -1);
            Scribe_References.Look<HulkForm>(ref currentHulkForm, "currentHulkForm");
            Scribe_Collections.Look<HulkForm>(ref hulkForms, "hulkForms", LookMode.Deep);
            Scribe_Collections.Look<ThingWithComps>(ref storedWeapons, "storedWeapons", LookMode.Reference);
            Scribe_Collections.Look<Apparel>(ref upperBodyItems, "upperBodyItems", LookMode.Reference);
        }
        #endregion Pawn Overrides
    }
}
