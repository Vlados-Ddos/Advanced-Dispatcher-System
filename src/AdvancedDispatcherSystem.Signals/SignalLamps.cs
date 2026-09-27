using System;
using System.Collections.Generic;
using AdvancedDispatcherSystem.Core;
using global::Signals.Game.Lights;
using UnityEngine;
using DVSignal = global::Signals.Game.Signal;

namespace AdvancedDispatcherSystem.Signals
{
    public sealed partial class Adapter
    {
        private static bool UsesLight(global::Signals.Common.Aspects.AspectBaseDefinition aspect, global::Signals.Common.SignalLightDefinition light)
        {
            if (aspect == null) return false;
            return aspect.OnLights != null && Array.IndexOf(aspect.OnLights, light) >= 0 ||
                aspect.BlinkingLights != null && Array.IndexOf(aspect.BlinkingLights, light) >= 0 ||
                aspect.LightSequences != null && Array.Exists(aspect.LightSequences, s => s != null && s.Lights != null && Array.IndexOf(s.Lights, light) >= 0) ||
                aspect.ColourChangers != null && Array.Exists(aspect.ColourChangers, c => c != null && c.Light == light);
        }
        private static SignalLamp[] CaptureLamps(DVSignal signal, SignalLamp[] previous)
        {
            if (signal.AllLights == null) return Array.Empty<SignalLamp>();
            var lamps = new List<SignalLamp>();
            foreach (var light in signal.AllLights)
            {
                // AllLights is a hierarchy query and can contain another head's lamps.
                // Signal ownership is assigned by DV Signals when each lamp is initialized.
                if (light == null || light.Signal != signal || light.Definition == null || light.Lamp == null) continue;
                var definition = light.Definition;
                bool mainLamp = Array.Exists(signal.AllAspects, a => UsesLight(a.GetDefinition(), definition));
                bool indicatorLamp = Array.Exists(signal.AllIndicators, a => UsesLight(a.GetDefinition(), definition));
                // Indicator lamps live under the same SignalDefinition but outside
                // the main head. Including them stretches the housing down the mast.
                if (indicatorLamp && !mainLamp) continue;
                Vector3 center = definition.transform.position;
                if (definition.Glare != null) center = definition.Glare.position;
                else if (definition.Renderers != null && definition.Renderers.Length > 0 && definition.Renderers[0] != null)
                    center = definition.Renderers[0].bounds.center;
                var face = signal.Definition.transform.InverseTransformPoint(center);
                // lampState includes native night/fog suppression; InternalState is only intent.
                var state = light.Lamp.lampState;
                var aspect = signal.CurrentAspect?.GetDefinition();
                // Exact definition references establish ownership; no color/index guesses.
                bool managed = Array.Exists(signal.AllAspects, a => { var d = a.GetDefinition(); return d != null && ((d.OnLights != null && Array.IndexOf(d.OnLights, definition) >= 0) || (d.BlinkingLights != null && Array.IndexOf(d.BlinkingLights, definition) >= 0)); });
                bool known = signal.IsOff || managed && aspect != null && (aspect.LightSequences == null || aspect.LightSequences.Length == 0);
                bool indicated = !signal.IsOff && aspect != null && aspect.OnLights != null && Array.IndexOf(aspect.OnLights, definition) >= 0;
                bool blink = !signal.IsOff && aspect != null && aspect.BlinkingLights != null && Array.IndexOf(aspect.BlinkingLights, definition) >= 0;
                Color color = light.Lamp.lampInd != null ? light.Lamp.lampInd.emissionColor : definition.Colour;
                if (known && aspect != null) {
                    color = definition.Colour;
                    if (aspect.ColourChangers != null) foreach (var changer in aspect.ColourChangers)
                        if (changer != null && changer.Light == definition) color = changer.Colour;
                }
                bool phaseKnown = light.Lamp.lampInd != null && state == LampControl.LampState.Blinking;
                lamps.Add(new SignalLamp { id = definition.GetInstanceID().ToString(), x = Math.Round(face.x, 4), y = Math.Round(face.y, 4),
                    phaseKnown = phaseKnown, phaseOn = phaseKnown && light.Lamp.IsOn,
                    indicationKnown = known, indicated = indicated || blink, indicationBlinking = blink,
                    color = "#" + ColorUtility.ToHtmlStringRGB(color), on = state == LampControl.LampState.On || state == LampControl.LampState.Blinking, blinking = state == LampControl.LampState.Blinking });
            }
            if (lamps.Count > 128) return Array.Empty<SignalLamp>();
            if (previous != null && previous.Length == lamps.Count) {
                bool same = true;
                for (int i = 0; i < lamps.Count; i++) {
                    var a = lamps[i]; var b = previous[i];
                    if (a.phaseKnown != b.phaseKnown || a.phaseOn != b.phaseOn || a.id != b.id || a.x != b.x || a.y != b.y || a.color != b.color || a.on != b.on || a.blinking != b.blinking || a.indicationKnown != b.indicationKnown || a.indicated != b.indicated || a.indicationBlinking != b.indicationBlinking) { same = false; break; }
                }
                if (same) return previous;
            }
            return lamps.ToArray();
        }
    }
}
