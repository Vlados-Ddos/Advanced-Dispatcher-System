using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AdvancedDispatcherSystem.Core;
using global::Signals.Common.Aspects;
using global::Signals.Common.Displays;
using global::Signals.Game;
using global::Signals.Game.Controllers;
using global::Signals.Game.Displays;
using UnityEngine;
using DVSignal = global::Signals.Game.Signal;

namespace AdvancedDispatcherSystem.Signals
{
    public sealed partial class Adapter
    {
        private static readonly Dictionary<Type, PropertyInfo> displayDefinitions = new Dictionary<Type, PropertyInfo>();
        private static readonly Dictionary<Type, PropertyInfo> aspectActiveProperties = new Dictionary<Type, PropertyInfo>();
        internal static bool IsRailwaySign(DVSignal signal)
        {
            if (signal.Controller is BufferStopSignalController || signal.Controller is TurntableSignalController) return true;
            var owner = signal;
            while (owner.Parent != null) owner = owner.Parent;
            if (Array.IndexOf(signal.Controller.DisplaySignals, owner) >= 0) return true;
            if (signal.AllAspects.Length == 0) return true;
            // A permanently displayed board has neither a variable aspect nor a lamp/animation.
            return signal.AllAspects.Length == 1 && signal.AllAspects[0].GetDefinition() is AlwaysActiveAspectDefinition &&
                (signal.AllLights == null || signal.AllLights.Length == 0) && signal.Definition.Animator == null;
        }
        private static string SignKind(DVSignal signal) => signal.Controller is BufferStopSignalController ? "bufferStop" :
            signal.Controller is TurntableSignalController ? "turntableIndicator" : BoardKind(signal.Definition.OffStateHUDSprite?.name);
        private static DisplayBaseDefinition DisplayDefinition(IDisplay display)
        {
            var type = display.GetType();
            if (!displayDefinitions.TryGetValue(type, out var property)) {
                property = type.GetProperty("Definition", BindingFlags.Instance | BindingFlags.Public);
                if (property != null && !typeof(DisplayBaseDefinition).IsAssignableFrom(property.PropertyType)) property = null;
                displayDefinitions[type] = property;
            }
            return property?.GetValue(display, null) as DisplayBaseDefinition;
        }
        private static string DisplayKind(DisplayBaseDefinition definition, int depth = 0)
        {
            if (depth > 8) return "auxiliaryDisplay";
            if (definition is AspectConditionalDisplayDefinition conditional) return DisplayKind(conditional.ActualDisplay, depth + 1);
            if (definition is MoveSwapDisplayDefinition move) return DisplayKind(move.ActualDisplay, depth + 1);
            if (definition is TrackInfoDisplayDefinition) return "routeIndicator";
            if (definition is SignalNameDisplayDefinition || definition is SignalIdDisplayDefinition) return "namePlate";
            if (definition is StaticDisplayDefinition) return "fixedBoard";
            return "auxiliaryDisplay";
        }
        private static bool Departure(AspectBaseDefinition definition, int depth = 0) => depth < 8 &&
            (definition is DepartureAspectDefinition || definition is CombinationAspectDefinition combination &&
                combination.Conditions != null && Array.Exists(combination.Conditions, d => Departure(d, depth + 1)));
        private static bool ValidRouteText(DisplayBaseDefinition definition, string text, int depth = 0)
        {
            if (depth > 8 || string.IsNullOrWhiteSpace(text)) return false;
            if (definition is AspectConditionalDisplayDefinition conditional)
                return text != conditional.NoValidResultValue && ValidRouteText(conditional.ActualDisplay,text,depth+1);
            if (definition is MoveSwapDisplayDefinition move) return ValidRouteText(move.ActualDisplay,text,depth+1);
            return !(definition is TrackInfoDisplayDefinition track) || text != track.NoValidResultValue;
        }
        private static bool TryActive(object aspect, out bool active)
        {
            active = false;
            if (aspect == null) return false;
            var type = aspect.GetType();
            if (!aspectActiveProperties.TryGetValue(type, out var property)) {
                property = type.GetProperty("Active", BindingFlags.Instance | BindingFlags.Public);
                if (property != null && property.PropertyType != typeof(bool)) property = null;
                aspectActiveProperties[type] = property;
            }
            if (property == null) return false;
            try { active = (bool)property.GetValue(aspect, null); return true; }
            catch { return false; }
        }
        private SignalPart[] CaptureParts(DVSignal signal, SignalPart[] previous)
        {
            // DV Signals GetAllHudElements: own aspect, own displays, own indicator wrappers,
            // then the distant child's elements. The count is from this exact head's arrays.
            int count = 1 + signal.AllDisplays.Length + signal.AllIndicators.Length;
            if (count > 65) return Array.Empty<SignalPart>();
            var elements = signal.GetAllHudElements().Take(count).ToArray();
            if (elements.Length != count || !ReferenceEquals(elements[0], signal.CurrentAspect ?? (IHudDisplayable)signal))
                throw new NotSupportedException("SIGNAL_HUD_CONTRACT");
            var parts = new List<SignalPart>();
            for (int i = 1; i < count; i++) {
                var element = elements[i];
                bool display = i <= signal.AllDisplays.Length;
                if (element == null) continue;
                bool visible = !display || element.ShouldDisplay && !string.IsNullOrEmpty(element.DisplayText);
                var definition = display ? DisplayDefinition(signal.AllDisplays[i - 1]) : null;
                int indicatorIndex = i - 1 - signal.AllDisplays.Length;
                var indicator = display ? null : signal.AllIndicators[indicatorIndex];
                var component = display ? (Component)definition : indicator.GetDefinition();
                string kind = display ? DisplayKind(definition) : Departure(indicator.GetDefinition()) ? "departureIndicator" : "auxiliaryIndicator";
                // SignalName/SignalId are diagnostic plates, not map indicators.
                if (display && kind == "namePlate") continue;
                if (kind == "routeIndicator" && !ValidRouteText(definition,element.DisplayText)) visible = false;
                bool active = true, stateKnown = display;
                if (!display) {
                    stateKnown = TryActive(indicator, out active);
                    // Presence comes from AllIndicators, state from the actual
                    // aspect. A real inactive Departure has a dark square.
                    if (signal.IsOff) { active = false; stateKnown = true; }
                    if (kind != "departureIndicator" && (!stateKnown || !active)) visible = false;
                }
                if (display && kind != "fixedBoard" && string.IsNullOrWhiteSpace(element.DisplayText)) visible = false;
                var position = component == null ? new Vector3() : signal.Definition.transform.InverseTransformPoint(component.transform.position);
                var world = component == null ? signal.Definition.transform.position - WorldMover.currentMove : component.transform.position - WorldMover.currentMove;
                parts.Add(new SignalPart { id = (display ? "d" : "i") + (component == null ? i : component.GetInstanceID()),
                    kind = kind == "fixedBoard" ? BoardKind(definition?.HUDSprite?.name) : kind, text = visible ? element.DisplayText?.Trim() ?? "" : "", visible = visible, color = "#" + ColorUtility.ToHtmlStringRGB(element.TextColour),
                    order = element.DisplayOrder, x = Math.Round(position.x, 4), y = Math.Round(position.y, 4),
                    worldX = world.x, worldZ = world.z, positioned = component != null, active = active, stateKnown = stateKnown });
            }
            if (previous != null && previous.Length == parts.Count) {
                bool same = true;
                for (int i = 0; i < parts.Count; i++) {var a=parts[i];var b=previous[i];if(a.visible!=b.visible||a.id!=b.id||a.kind!=b.kind||a.text!=b.text||a.color!=b.color||a.order!=b.order||a.x!=b.x||a.y!=b.y||a.worldX!=b.worldX||a.worldZ!=b.worldZ||a.positioned!=b.positioned||a.active!=b.active||a.stateKnown!=b.stateKnown){same=false;break;}}
                if (same) return previous;
            }
            return parts.ToArray();
        }
    }
}
