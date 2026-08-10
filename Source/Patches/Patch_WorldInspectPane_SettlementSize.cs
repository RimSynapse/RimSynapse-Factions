using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimSynapse.RegionsAndTerritories.Sizing;
using RimSynapse.RegionsAndTerritories.Integration;
using Verse;

namespace RimSynapse.Factions.Patches
{
    /// <summary>
    /// 0.7 Epic 4: name the size of whatever stands on this tile.
    ///
    /// <para>The tier is derived, not stored, so this is the only place the player ever sees it —
    /// and it has to be legible for any mod's holding, which is why it comes from the same
    /// evaluator the economy reads rather than from a concrete settlement type.</para>
    ///
    /// <para>This was a block inside Regions and Territories' own inspect-pane patch until sizing
    /// moved here. It is a separate Harmony postfix on the same getter rather than a callback into
    /// R&amp;T, because the world layer must not have to know whether this mod is installed.
    /// Harmony composes the two; R&amp;T's territory lines still print whether or not Factions is
    /// loaded.</para>
    ///
    /// <para>Cached on the same 120-tick cadence and for the same reason: the getter runs every GUI
    /// frame, and resolving the largest tiered object walks the world objects on the tile.</para>
    /// </summary>
    [HarmonyPatch(typeof(WorldInspectPane), "TileInspectString", MethodType.Getter)]
    internal static class Patch_WorldInspectPane_SettlementSize
    {
        private const int RefreshIntervalTicks = 120;

        private static int cachedTileId = -1;
        private static int cachedAtTick = -1;
        private static string cachedText = string.Empty;

        [HarmonyPostfix]
        static void Postfix(ref string __result)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;
            if (!WorldObjectIntegrationSettings.SettlementTiersActive) return;

            PlanetTile selectedTile = Find.WorldSelector.SelectedTile;
            if (selectedTile == PlanetTile.Invalid) return;

            string extra = GetTierText(selectedTile.tileId);
            if (string.IsNullOrEmpty(extra)) return;

            if (!string.IsNullOrEmpty(__result)) __result += "\n";
            __result += extra;
        }

        private static string GetTierText(int tileId)
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

            if (tileId == cachedTileId && cachedAtTick >= 0 && now - cachedAtTick < RefreshIntervalTicks)
            {
                return cachedText;
            }

            SettlementTier tier;
            WorldObject obj = SettlementSizeUtility.LargestTieredObjectAt(tileId, out tier);

            cachedTileId = tileId;
            cachedAtTick = now;
            cachedText = (obj == null || tier == SettlementTier.None)
                ? string.Empty
                : "Settlement size: " + tier.LabelCapitalized();

            return cachedText;
        }
    }
}
