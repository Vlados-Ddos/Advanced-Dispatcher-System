using System;
using System.Linq;
using System.Reflection;
using AdvancedDispatcherSystem.Game;
using HarmonyLib;
using LiteNetLib;
using MPAPI;
using MPAPI.Interfaces;
using global::Multiplayer.Components.Networking.World;
using global::Multiplayer.Networking.Managers.Server;
using global::Multiplayer.Networking.Packets.Common;
using global::Multiplayer.Networking.TransportLayers;

namespace AdvancedDispatcherSystem.Multiplayer
{
    public sealed partial class Adapter
    {
        private Harmony protectionHarmony;
        private bool protectionAttempted;
        private static MethodInfo sendJunction;
        private readonly System.Collections.Generic.Dictionary<bool,Tuple<Type,MethodInfo>> reservationPackets = new System.Collections.Generic.Dictionary<bool,Tuple<Type,MethodInfo>>();
        public bool ProtectedSwitches
        {
            get
            {
                if(Mode=="singleplayer")return true;
                if(Mode!="host")return false;
                if(protectionAttempted)return protectionHarmony!=null;
                protectionAttempted=true;
                try
                {
                    var target=AccessTools.DeclaredMethod(typeof(NetworkServer),"OnCommonChangeJunctionPacket");
                    var sender=typeof(NetworkServer).BaseType.GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Single(m=>m.Name=="SendPacket"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==3);
                    if(target==null)throw new MissingMethodException("OnCommonChangeJunctionPacket");
                    sendJunction=sender.MakeGenericMethod(typeof(CommonChangeJunctionPacket));
                    var patch=new Harmony("denis.ads.multiplayer-protection");
                    patch.Patch(target,prefix:new HarmonyMethod(typeof(Adapter),nameof(GuardJunctionPacket)) {priority=Priority.First});
                    protectionHarmony=patch;return true;
                }
                catch(Exception e) {UnityEngine.Debug.LogError("ADS PROTECTION_UNAVAILABLE "+e);return false;}
            }
        }
        private static bool GuardJunctionPacket(NetworkServer __instance, CommonChangeJunctionPacket packet, ITransportPeer peer)
        {
            if(!NetworkedJunction.TryGet(packet.NetId,out var junction)||junction==null)return false;
            int branch=junction.outBranches.Count==0?packet.SelectedBranch:packet.SelectedBranch%junction.outBranches.Count;
            if(Dispatcher.ProtectedSwitchAllowed(junction,branch))return true;
            // The server normally rebroadcasts without applying locally. Reject BEFORE
            // rebroadcast, then correct the sender's client-side predicted switch state.
            sendJunction.Invoke(__instance,new object[] {peer,new CommonChangeJunctionPacket {NetId=packet.NetId,SelectedBranch=junction.selectedBranch,Mode=(byte)Junction.SwitchMode.NO_SOUND},DeliveryMethod.ReliableOrdered});
            return false;
        }
        public bool PublishSignalReservation(int signal, bool reserved)
        {
            try {return PublishReservationCore(signal,reserved);}
            catch(Exception e) {UnityEngine.Debug.LogError("ADS SIGNALS_SYNC_UNAVAILABLE "+e);return false;}
        }
        private bool PublishReservationCore(int signal, bool reserved)
        {
            if(Mode=="singleplayer")return true;
            if(Mode!="host"||MultiplayerAPI.Server==null)return false;
            if(!reservationPackets.TryGetValue(reserved,out var entry))
            {
                var assembly=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="Signals.MP");
                var type=assembly?.GetType(reserved?"Signals.MP.ReservationSuccessPacket":"Signals.MP.ReservationCancelSuccessPacket");
                if(type==null)return false;
                var method=typeof(IServer).GetMethods().Single(m=>m.Name=="SendPacketToAll"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==4).MakeGenericMethod(type);
                entry=Tuple.Create(type,method);reservationPackets.Add(reserved,entry);
            }
            var packet=Activator.CreateInstance(entry.Item1);
            entry.Item1.GetProperty("SignalId").SetValue(packet,signal,null);
            // This host already changed TrackReserver; native clients consume the same
            // packets as DV Signals. Joining clients use its existing full-state replay.
            entry.Item2.Invoke(MultiplayerAPI.Server,new object[] {packet,true,true,null});
            return true;
        }
    }
}
