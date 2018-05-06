using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Hulk
{
    [StaticConstructorOnStartup]
    static class HarmonyPatches
    {

        static HarmonyPatches()
        {
            HarmonyInstance harmony = HarmonyInstance.Create("rimworld.jecrell.cthulhu.cults");
            harmony.Patch(AccessTools.Method(typeof(Pawn), "get_BodySize"), null, new HarmonyMethod(typeof(HarmonyPatches), 
                nameof(HulkBodySize)), null);
            harmony.Patch(AccessTools.Method(typeof(Pawn), "get_HealthScale"), null, new HarmonyMethod(typeof(HarmonyPatches),
                nameof(HulkHealthScale)), null);
            harmony.Patch(AccessTools.Method(typeof(PawnRenderer), "RenderPawnInternal", 
                new Type[] { typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(Rot4), typeof(Rot4), typeof(RotDrawMode), typeof(bool), typeof(bool) }), new HarmonyMethod(typeof(HarmonyPatches), 
                nameof(RenderHulk)), null);
            harmony.Patch(AccessTools.Method(typeof(Building_Door), "PawnCanOpen"), null, new HarmonyMethod(typeof(HarmonyPatches),
                nameof(HulkCantOpen)), null);
            harmony.Patch(AccessTools.Method(typeof(Verb_MeleeAttack), "SoundHitPawn"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(SoundHitPawnPrefix)), null);
            harmony.Patch(AccessTools.Method(typeof(Verb_MeleeAttack), "SoundMiss"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(SoundMiss_Prefix)), null);
           // harmony.Patch(AccessTools.Method(typeof(FloatMenuMakerMap), "AddHumanlikeOrders"), null, new HarmonyMethod(typeof(HarmonyPatches),
                //nameof(OrderForSilverTreatment)));
            harmony.Patch(AccessTools.Method(typeof(ThingWithComps), "InitializeComps"), null, new HarmonyMethod(typeof(HarmonyPatches),
                nameof(InitializeWWComps)));
            harmony.Patch(AccessTools.Method(typeof(Pawn_PathFollower), "CostToMoveIntoCell"), null, new HarmonyMethod(typeof(HarmonyPatches),
                nameof(PathOfNature)), null);
            harmony.Patch(AccessTools.Method(typeof(LordToil_AssaultColony), "UpdateAllDuties"), null, new HarmonyMethod(typeof(HarmonyPatches),
                nameof(UpdateAllDuties_PostFix)), null);
            harmony.Patch(AccessTools.Method(typeof(Pawn), "Kill"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(HulkKill)), null);
            harmony.Patch(AccessTools.Method(typeof(PawnUtility), "RecruitDifficulty"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(UnrecruitableSworn)), null);
            harmony.Patch(AccessTools.Method(typeof(Pawn), "Destroy"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(HulkDestroy)), null);
            harmony.Patch(AccessTools.Method(typeof(TickManager), "DebugSetTicksGame"), null, new HarmonyMethod(typeof(HarmonyPatches),
                nameof(MoonTicksUpdate)), null);
            harmony.Patch(AccessTools.Method(typeof(Dialog_DebugActionsMenu), "DoListingItems_MapActions"), null, new HarmonyMethod(typeof(HarmonyPatches),
                nameof(DebugMoonActions)), null);
            harmony.Patch(AccessTools.Method(typeof(HealthUtility), "DamageUntilDowned"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(DebugDownHulk)), null);
            harmony.Patch(AccessTools.Method(typeof(JobGiver_OptimizeApparel), "TryGiveJob"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(DontOptimizeHulkApparel)), null);
            harmony.Patch((typeof(DamageWorker_AddInjury).GetMethods(AccessTools.all)
                .Where(mi => mi.GetParameters().Count() >= 4 &&
                mi.GetParameters().ElementAt(1).ParameterType == typeof(Hediff_Injury)).First()),
                new HarmonyMethod(typeof(HarmonyPatches), nameof(HulkDmgFixFinalizeAndAddInjury)), null);
            harmony.Patch(AccessTools.Method(typeof(Scenario), "Notify_PawnGenerated"), null,
                new HarmonyMethod(typeof(HarmonyPatches), nameof(AddRecentWerewolves)));

        }

        // RimWorld.Scenario
        public static void AddRecentHulks(Scenario __instance, Pawn pawn, PawnGenerationContext context)
        {
            if (pawn.IsHulk())
            {
                var recentWerewolves = Find.World.GetComponent<WorldComponent_MoonCycle>().recentWerewolves;
                recentWerewolves?.Add(pawn, 1);
            }
        }
        
        public static bool ShouldModifyDamage(Pawn instigator)
        {
            if (!instigator?.TryGetComp<CompHulk>()?.IsTransformed ?? false)
                return true;
            return false;
        }

        //public class DamageWorker_AddInjury : DamageWorker
        public static void HulkDmgFixFinalizeAndAddInjury(DamageWorker_AddInjury __instance, Pawn pawn, ref Hediff_Injury injury, ref DamageInfo dinfo, ref DamageWorker.DamageResult result)
        {
            if (dinfo.Amount > 0 && pawn.TryGetComp<CompHulk>() is CompHulk ww && ww.IsHulk && ww.CurrentHulkForm != null)
            {
                if (dinfo.Instigator is Pawn a && ShouldModifyDamage(a))
                {
                    if (a?.equipment?.Primary is ThingWithComps b && !b.IsSilverTreated())
                    {
                        int math = (int)(dinfo.Amount) - (int)(dinfo.Amount * (ww.CurrentHulkForm.DmgImmunity)); //10% damage. Decimated damage.
                        dinfo.SetAmount(math);
                        injury.Severity = math;
                        //Log.Message(dinfo.Amount.ToString());
                    }

                }
            }
        }

        // RimWorld.JobGiver_OptimizeApparel
        public static bool DontOptimizeHulkApparel(JobGiver_OptimizeApparel __instance, ref Job __result, Pawn pawn)
        {
            if (pawn?.GetComp<CompHulk>() is CompHulk ww && ww.IsTransformed)
            {
                __result = null;
                return false;
            }
            return true;
        }

        // Verse.HealthUtility
        public static void DebugDownHulk(Pawn p)
        {
            if (p?.GetComp<CompHulk>() is CompHulk w && w.IsHulk && w.IsTransformed)
            {
                w.TransformBack();
            }
        }

        // Verse.Dialog_DebugActionsMenu
        public static void DebugMoonActions(Dialog_DebugActionsMenu __instance)
        {
            AccessTools.Method(typeof(Dialog_DebugActionsMenu), "DoLabel").Invoke(__instance, new object[] { "Tools - Werewolves" });
            
            AccessTools.Method(typeof(Dialog_DebugActionsMenu), "DebugToolMap").Invoke(__instance, new object[] {
                "Give Hulk", new Action(()=>
                {
                    Pawn pawn = Find.VisibleMap.thingGrid.ThingsAt(UI.MouseCell()).Where((Thing t) => t is Pawn).Cast<Pawn>().FirstOrDefault<Pawn>();
                    if (pawn != null)
                    {
                        if (!pawn.IsHulk())
                        {
                            pawn.story.traits.GainTrait(new Trait(WWDefOf.Hulk, 0));
                            //pawn.health.AddHediff(VampDefOf.ROM_Vampirism, null, null);
                            pawn.Drawer.Notify_DebugAffected();
                            MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, pawn.LabelShort + " is now a hulk", -1f);
                        }
                        else
                            Messages.Message(pawn.LabelCap + " is already a hulk.", MessageTypeDefOf.RejectInput);
                    }
                })
            });

            AccessTools.Method(typeof(Dialog_DebugActionsMenu), "DebugToolMap").Invoke(__instance, new object[] {
                "Remove Hulk", new Action(()=>
                {
                    Pawn pawn = Find.VisibleMap.thingGrid.ThingsAt(UI.MouseCell()).Where((Thing t) => t is Pawn).Cast<Pawn>().FirstOrDefault<Pawn>();
                    if (pawn != null)
                    {
                        if (pawn.IsHulk())
                        {
                            if (pawn.CompWW().IsTransformed)
                            {
                                pawn.CompWW().TransformBack();
                            }

                            pawn.story.traits.allTraits.RemoveAll(x => x.def == WWDefOf.Hulk); //GainTrait(new Trait(WWDefOf.Hulk, -1));
                            //pawn.health.AddHediff(VampDefOf.ROM_Vampirism, null, null);
                            pawn.Drawer.Notify_DebugAffected();
                            MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, pawn.LabelShort + " is no longer a hulk", -1f);
                        }
                        else
                            Messages.Message(pawn.LabelCap + " is not a hulk.", MessageTypeDefOf.RejectInput);
                    }
                })
            });

            AccessTools.Method(typeof(Dialog_DebugActionsMenu), "DebugAction").Invoke(__instance, new object[] {
                "Regenerate Moons", new Action(()=>
                {
                    Find.World.GetComponent<WorldComponent_MoonCycle>().DebugRegenerateMoons(Find.World);
                })
            });

            AccessTools.Method(typeof(Dialog_DebugActionsMenu), "DebugAction").Invoke(__instance, new object[] {
                "Next Full Moon", new Action(()=>
                {
                    Find.World.GetComponent<WorldComponent_MoonCycle>().DebugTriggerNextFullMoon();
                })
            });

        }

        // Verse.TickManager
        public static void MoonTicksUpdate(TickManager __instance, int newTicksGame)
        {
            if (newTicksGame <= Find.TickManager.TicksGame + GenDate.TicksPerDay + 1000)
            {
                Find.World.GetComponent<WorldComponent_MoonCycle>().AdvanceOneDay();
            }
            else if (newTicksGame <= Find.TickManager.TicksGame + GenDate.TicksPerQuadrum + 1000)
            {
                Find.World.GetComponent<WorldComponent_MoonCycle>().AdvanceOneQuadrum();
            }
        }


        /// Werewolves must revert before being destroyed.
        public static void HulkDestroy(Pawn __instance, DestroyMode mode = DestroyMode.Vanish)
        {
            if (__instance?.GetComp<CompHulk>() is CompHulk w && w.IsHulk && w.IsTransformed)
            {
                w.TransformBack(true);
            }
        }

        // Verse.Pawn
        public static void HulkKill(Pawn __instance, DamageInfo? dinfo)
        {
            if (__instance?.GetComp<CompHulk>() is CompHulk w && w.IsTransformed && !w.IsReverting)
            {
                w.TransformBack(true);
            }
        }

            // RimWorld.LordToil_AssaultColony
            public static void UpdateAllDuties_PostFix(LordToil_AssaultColony __instance)
        {
            for (int i = 0; i < __instance.lord.ownedPawns.Count; i++)
            {
                if (__instance.lord.ownedPawns[i] is Pawn p && p.GetComp<CompHulk>() is CompHulk w && w.IsHulk)
                    p.mindState.duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("HulkAssault"));
            }

        }

        // Verse.AI.Pawn_PathFollower
        public static void PathOfNature(Pawn_PathFollower __instance, ref int __result, IntVec3 c)
        {
            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn?.GetComp<CompHulk>() is CompHulk compHulk && compHulk?.CurrentHulkForm?.def == WWDefOf.Hulk)
            {
                int num;
                if (c.x == pawn.Position.x || c.z == pawn.Position.z)
                {
                    num = pawn.TicksPerMoveCardinal;
                }
                else
                {
                    num = pawn.TicksPerMoveDiagonal;
                }
                //num += pawn.Map.pathGrid.CalculatedCostAt(c, false, pawn.Position);
                Building edifice = c.GetEdifice(pawn.Map);
                if (edifice != null)
                {
                    num += (int)edifice.PathWalkCostFor(pawn);
                }
                if (num > 450)
                {
                    num = 450;
                }
                if (pawn.jobs.curJob != null)
                {
                    switch (pawn.jobs.curJob.locomotionUrgency)
                    {
                        case LocomotionUrgency.Amble:
                            num *= 3;
                            if (num < 60)
                            {
                                num = 60;
                            }
                            break;
                        case LocomotionUrgency.Walk:
                            num *= 2;
                            if (num < 50)
                            {
                                num = 50;
                            }
                            break;
                        case LocomotionUrgency.Jog:
                            num *= 1;
                            break;
                        case LocomotionUrgency.Sprint:
                            num = Mathf.RoundToInt((float)num * 0.75f);
                            break;
                    }
                }
                __result = Mathf.Max(num, 1);
            }
        }


        // Verse.ThingWithComps
        public static void InitializeWWComps(ThingWithComps __instance)
        {
            if (__instance.def.IsRangedWeapon)
            {
                var comps = (List<ThingComp>)AccessTools.Field(typeof(ThingWithComps), "comps").GetValue(__instance);
                ThingComp thingComp = (ThingComp)Activator.CreateInstance(typeof(CompSilverTreated));
                thingComp.parent = __instance;
                comps.Add(thingComp);
                thingComp.Initialize(null);
            }
        }

        // RimWorld.Verb_MeleeAttack
        public static void SoundMiss_Prefix(ref SoundDef __result, Verb_MeleeAttack __instance)
        {
            if (__instance.caster is Pawn pawn && pawn.GetComp<CompHulk>() is CompHulk w && w.IsTransformed)
            {
                if (w.CurrentHulkForm.def.attackSound is SoundDef soundToPlay)
                {
                    if (Rand.Value < 0.5f)
                        soundToPlay.PlayOneShot(new TargetInfo(pawn));
                }
            }
        }


        public static void SoundHitPawnPrefix(ref SoundDef __result, Verb_MeleeAttack __instance)
        {
            if (__instance.caster is Pawn pawn && pawn.GetComp<CompHulk>() is CompHulk w && w.IsTransformed)
            {
                if (w.CurrentHulkForm.def.attackSound is SoundDef soundToPlay)
                {
                    if (Rand.Value < 0.5f)
                        soundToPlay.PlayOneShot(new TargetInfo(pawn));
                }
            }
        }

            // RimWorld.Building_Door
            public static void HulkCantOpen(Pawn p, ref bool __result)
        {
            __result = __result && (p?.mindState?.mentalStateHandler?.CurState?.def != WWDefOf.HulkFury);
        }


        public static bool RenderHulk(PawnRenderer __instance, Vector3 rootLoc, Quaternion quat, bool renderBody, Rot4 bodyFacing, Rot4 headFacing, RotDrawMode bodyDrawType, bool portrait, bool headStump)
        {
            Pawn p = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (p?.GetComp<CompHulk>() is CompHulk compHulk && compHulk.IsTransformed)
            {
                if (compHulk.CurrentHulkForm.bodyGraphicData == null || __instance.graphics.nakedGraphic == null)
                {
                    compHulk.CurrentHulkForm.bodyGraphicData = compHulk.CurrentHulkForm.def.graphicData;
                    __instance.graphics.nakedGraphic = compHulk.CurrentHulkForm.bodyGraphicData.Graphic;
                }
                Mesh mesh = null;
                if (renderBody)
                {
                    Vector3 loc = rootLoc;
                    loc.y += 0.0046875f;
                    if (bodyDrawType == RotDrawMode.Dessicated && !p.RaceProps.Humanlike && __instance.graphics.dessicatedGraphic != null && !portrait)
                    {
                        __instance.graphics.dessicatedGraphic.Draw(loc, bodyFacing, p);
                    }
                    else
                    {
                        mesh = __instance.graphics.nakedGraphic.MeshAt(bodyFacing);
                        List<Material> list = __instance.graphics.MatsBodyBaseAt(bodyFacing, bodyDrawType);
                        for (int i = 0; i < list.Count; i++)
                        {
                            Material damagedMat = __instance.graphics.flasher.GetDamagedMat(list[i]);
                            Vector3 scaleVector = new Vector3(loc.x, loc.y, loc.z);
                            if (portrait)
                            {
                                scaleVector.x *= 1f + (1f - (portrait ?
                                                            compHulk.CurrentHulkForm.def.CustomPortraitDrawSize :
                                                            compHulk.CurrentHulkForm.bodyGraphicData.drawSize)
                                                        .x);
                                scaleVector.z *= 1f + (1f - (portrait ?
                                                                compHulk.CurrentHulkForm.def.CustomPortraitDrawSize :
                                                                compHulk.CurrentHulkForm.bodyGraphicData.drawSize)
                                                            .y);
                            }
                            else scaleVector = new Vector3(0, 0, 0);
                            GenDraw.DrawMeshNowOrLater(mesh, loc + scaleVector, quat, damagedMat, portrait);
                            loc.y += 0.0046875f;
                        }
                        if (bodyDrawType == RotDrawMode.Fresh)
                        {
                            Vector3 drawLoc = rootLoc;
                            drawLoc.y += 0.01875f;
                            Traverse.Create(__instance).Field("woundOverlays").GetValue<PawnWoundDrawer>().RenderOverBody(drawLoc, mesh, quat, portrait);
                        }
                    }
                }
                return false;
            }
            return true;
        }

        // Verse.Pawn
        public static void HulkBodySize(Pawn __instance, ref float __result)
        {
            if (__instance?.GetComp<CompHulk>() is CompHulk w && w.IsHulk && w.IsTransformed)
            {
                __result = w.CurrentHulkForm.FormBodySize;  //Mathf.Clamp((__result * w.CurrentHulkForm.def.sizeFactor) + (w.CurrentHulkForm.level * 0.1f), __result, __result * (w.CurrentHulkForm.def.sizeFactor * 2));
            }
        }

        // Verse.Pawn
        public static void HulkHealthScale(Pawn __instance, ref float __result)
        {
            if (__instance?.GetComp<CompHulk>() is CompHulk w && w.IsHulk && w.IsTransformed)
            {
                __result = w.CurrentHulkForm.FormHealthScale; //Mathf.Clamp((__result * w.CurrentHulkForm.def.healthFactor) + (w.CurrentHulkForm.level * 0.1f), __result, __result * (w.CurrentHulkForm.def.healthFactor * 2));
            }
        }



    }
}
