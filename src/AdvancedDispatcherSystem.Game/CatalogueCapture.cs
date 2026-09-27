using HarmonyLib;
using DV.RenderTextureSystem.BookletRender;

namespace AdvancedDispatcherSystem.Game
{
    // Pages loaded after the world bootstrap report their authored palette here.
    // Vehicle sampling must never search the whole Unity object registry.
    [HarmonyPatch(typeof(VehicleCatalogPageTemplatePaper), "FillInData")]
    internal static class CatalogueCapture
    {
        private static void Postfix(VehicleCatalogPageTemplatePaper __instance) {
            try { VehiclePresentation.ObserveCataloguePage(__instance); }
            catch (System.Exception e) { Main.Log("CATALOGUE_CAPTURE_FAILED", e); }
        }
    }
}
