using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using System;
using System.Reflection;

namespace LetThemEatPaste {
    [StaticConstructorOnStartup]
    static class Patches {
        static Patches() {
            Harmony harmony = new Harmony("com.unislash.LetThemEatPaste.patches");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public static Thing GetPasteSource(Pawn getter, Pawn eater) {
            Thing chosenFood = null;

            // 1. If the eater is null, nothing to do
            // 2. If the eater isn't a prisoner, use the original behavior
            // 3. If the eater is also the getter, then the prisoner is getting food for themselves,
            //      so we want to use the original behavior
            if (eater == null || !eater.IsPrisoner || eater == getter) {
                return null;
            }

            // Find any nutrient paste meals already made
            List<Thing> possibleThings =
                getter.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);

            int Comparison(Thing t1, Thing t2) {
                float num = (float) (t1.Position - getter.Position).LengthHorizontalSquared;
                float value = (float) (t2.Position - getter.Position).LengthHorizontalSquared;
                return num.CompareTo(value);
            }

            possibleThings.Sort(Comparison);

            TraverseParms traverseParams = TraverseParms.For(getter, Danger.Deadly, TraverseMode.ByPawn, false);

            // Go find the closest nutrient paste meal or dispenser
            foreach (var thing in possibleThings) {
                if (thing.def.defName == "MealNutrientPaste" && !thing.IsForbidden(getter) &&
                    getter.CanReserve(thing, 1, -1, null, false)
                    && getter.Map.reachability.CanReach(getter.Position, thing, PathEndMode.ClosestTouch,
                        traverseParams)) {
                    chosenFood = thing;
                    break;
                }

                if (thing is Building_NutrientPasteDispenser building_NutrientPasteDispenser
                    && building_NutrientPasteDispenser.CanDispenseNow
                    && getter.Map.reachability.CanReach(getter.Position,
                        thing, PathEndMode.ClosestTouch, traverseParams)) {
                    chosenFood = thing;
                    break;
                }
            }

            // Return the chosen food
            return chosenFood;
        }

        [HarmonyPatch(typeof(FoodUtility))]
        [HarmonyPatch("TryFindBestFoodSourceFor_NewTemp")]
        public static class TryFindBestFoodSourceFor_Patch {
            /**
             * Force wardens to not use meals in their inventory when feeding prisoners
             *  in an effort to prevent them from using food they would use themselves.
             */
            static bool Prefix(Pawn getter, Pawn eater, ref bool canUseInventory) {
                // If the eater is null, nothing to do
                if (eater == null || getter == null) {
                    return true;
                }

                // If the eater isn't a prisoner, use the original behavior
                // Or if the eater is also the getter, then the prisoner is getting food for themselves,
                //  so we want to use the original behavior
                if (!eater.IsPrisoner || eater == getter) {
                    return true;
                }

                // Ok, so now that we've identified that we're a colonist feeding a prisoner,
                //  we don't want to use any amazing foods in our inventory,
                //  unless...
                // 1) The best food in the inventory could actually be a nutrient paste meal.
                //      If so, feed that to the prisoner scum!
                // 2) We might not be able to find any paste meals or dispensers.
                //      If so, then I guess we can feed the prisoner what we have in our inventory...
                //      (but next time we're totally going to feed them paste)
                Thing foodInInventory =
                    FoodUtility.BestFoodInInventory_NewTemp(getter, null, FoodPreferability.MealAwful,
                        FoodPreferability.MealLavish, 0f, false);
                Thing canFindPaste = GetPasteSource(getter, eater);
                if (foodInInventory != null
                    && foodInInventory.def.defName != "MealNutrientPaste"
                    && canFindPaste != null) {
                    canUseInventory = false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FoodUtility))]
        [HarmonyPatch("BestFoodSourceOnMap_NewTemp")]
        private static class BestFoodSourceOnMap_Patch {
            /**
             * This prefix will return the nearest nutrient paste meal or dispenser if this is part of a job for a warden or doctor to feed a prisoner.
             */
            public static bool Prefix(ref Thing __result, Pawn getter, Pawn eater, ref ThingDef foodDef) {
                Thing pasteSource = GetPasteSource(getter, eater);

                // If we didn't find a food, then use the original method
                if (pasteSource == null) return true;
                __result = pasteSource;
                foodDef = FoodUtility.GetFinalIngestibleDef(pasteSource, false);
                return false;
            }
        }
    }
}