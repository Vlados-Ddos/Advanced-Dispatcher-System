using System;
using System.Collections.Generic;
using DV.Localization;
using DV.RenderTextureSystem.BookletRender;
using DV.ThingTypes;
using UnityEngine;
using UnityEngine.UI;

namespace AdvancedDispatcherSystem.Game
{
    internal static class VehiclePresentation
    {
        private static readonly Dictionary<TrainCarLivery, string> colors = new Dictionary<TrainCarLivery, string>();
        private static string language;
        private static readonly Dictionary<string, string> names = new Dictionary<string, string>();
        public static string CatalogColor(TrainCarLivery livery)
        {
            if (livery == null) return null;
            if (colors.TryGetValue(livery, out var color)) return color;
            CataloguePalette.Colors.TryGetValue(livery.id + "|" + livery.localizationKey, out var authored);
            return authored;
        }
        public static void CaptureCatalogue()
        {
            // Verified in the installed catalogue prefab: the header's authored
            // Image color, bound through page.carLivery, is the vehicle palette.
            // No inference from TrainCarType enum, livery ID or body materials.
            foreach (var page in Resources.FindObjectsOfTypeAll<VehicleCatalogPageTemplatePaper>())
                ObserveCataloguePage(page);
        }
        internal static void ObserveCataloguePage(VehicleCatalogPageTemplatePaper page)
        {
            if (page == null || page.carLivery == null) return;
            var header = page.transform.Find("Template Canvas/BackgroundImage/OuterWrapper/VCHeader/LocoColorBg");
            var image = header == null ? null : header.GetComponent<Image>();
            if (image != null && image.color.a > 0)
                colors[page.carLivery] = "#" + ColorUtility.ToHtmlStringRGB(image.color);
        }
        private static readonly Dictionary<TrainCarLivery, KeyValuePair<string,string>> categories = new Dictionary<TrainCarLivery, KeyValuePair<string,string>>();
        public static string Category(TrainCarLivery livery)
        {
            var kind = livery?.parentType?.kind?.id;
            if (string.IsNullOrEmpty(kind)) return "unknown";
            if (categories.TryGetValue(livery,out var known) && known.Key == kind) return known.Value;
            var category = CarTypes.IsLocomotive(livery) ? "locomotive" : CarTypes.IsTender(livery) ? "tender" :
                CarTypes.IsSlug(livery) ? "slug" : CarTypes.IsCaboose(livery) ? "caboose" : CarTypes.IsRegularCar(livery) ? "wagon" : "other";
            categories[livery] = new KeyValuePair<string,string>(kind,category);
            return category;
        }

        public static string Model(TrainCarLivery livery)
        {
            var key = livery?.parentType?.localizationKey;
            if (string.IsNullOrEmpty(key)) return null;
            if (language != Main.LanguageCode) { language = Main.LanguageCode; names.Clear(); }
            if (names.TryGetValue(key, out var known)) return known;
            var value = LocalizationAPI.L(key);
            return names[key] = value != key && DisplayText.Usable(value) ? value : null;
        }
        public static void Reset() { categories.Clear(); colors.Clear(); names.Clear(); }
    }
}
