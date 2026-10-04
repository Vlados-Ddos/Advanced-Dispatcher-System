using System;
using System.IO;
using System.Text;
using AdvancedDispatcherSystem.Core;
using MPAPI.Interfaces.Packets;

namespace AdvancedDispatcherSystem.Multiplayer
{
    // Serializable packets must be classes: the installed MPAPI wrapper boxes
    // its packet before Deserialize, so a struct would lose the decoded fields.
    public sealed class HostSettingsRequest : ISerializablePacket
    {
        public Guid request;
        public void Serialize(BinaryWriter writer) { writer.Write((byte)1); writer.Write(request.ToByteArray()); }
        public void Deserialize(BinaryReader reader)
        {
            request = Guid.Empty;
            try { if (reader.ReadByte() == 1) request = new Guid(HostSettingsPacket.ReadExact(reader, 16)); }
            catch (IOException) { }
        }
    }
    public sealed class HostSettingsPacket : ISerializablePacket
    {
        public Guid request;
        public long revision;
        public HostSettingsState settings;
        public void Serialize(BinaryWriter writer)
        {
            if (!Valid(settings)) throw new InvalidDataException("HOST_SETTINGS_INVALID");
            writer.Write((byte)1); writer.Write(request.ToByteArray()); writer.Write(revision);
            writer.Write((byte)((settings.readOnly ? 1 : 0) | (settings.showUndiscovered ? 2 : 0) | (settings.adminControls ? 4 : 0) | (settings.remoteLanAccess ? 8 : 0)));
            writer.Write(settings.captureBudgetMs); writer.Write((ushort)settings.port);
            byte[] host = Encoding.UTF8.GetBytes(settings.publicHost ?? ""); writer.Write((ushort)host.Length); writer.Write(host);
        }
        public void Deserialize(BinaryReader reader)
        {
            settings = null;
            try
            {
                if (reader.ReadByte() != 1) return;
                request = new Guid(ReadExact(reader, 16)); revision = reader.ReadInt64();
                byte flags = reader.ReadByte(); if ((flags & ~15) != 0) return;
                var next = new HostSettingsState { readOnly = (flags & 1) != 0, showUndiscovered = (flags & 2) != 0,
                    adminControls = (flags & 4) != 0, remoteLanAccess = (flags & 8) != 0,
                    captureBudgetMs = reader.ReadDouble(), port = reader.ReadUInt16() };
                int length = reader.ReadUInt16(); if (length > 1012) return;
                next.publicHost = new UTF8Encoding(false, true).GetString(ReadExact(reader, length));
                if (request != Guid.Empty && revision > 0 && Valid(next)) settings = next;
            }
            catch (IOException) { }
            catch (ArgumentException) { }
        }
        internal static byte[] ReadExact(BinaryReader reader, int count)
        {
            byte[] bytes = reader.ReadBytes(count); if (bytes.Length != count) throw new EndOfStreamException(); return bytes;
        }
        internal static bool Valid(HostSettingsState value)
        {
            if (value == null || double.IsNaN(value.captureBudgetMs) || double.IsInfinity(value.captureBudgetMs) ||
                value.captureBudgetMs < 0.3 || value.captureBudgetMs > 2 || value.port < 1024 || value.port > 65535 || (value.publicHost?.Length ?? 0) > 253) return false;
            foreach (char c in value.publicHost ?? "") if (char.IsControl(c)) return false;
            return true;
        }
        internal static HostSettingsState Copy(HostSettingsState value) => value == null ? null : new HostSettingsState
        {
            readOnly = value.readOnly, showUndiscovered = value.showUndiscovered, adminControls = value.adminControls,
            captureBudgetMs = value.captureBudgetMs, remoteLanAccess = value.remoteLanAccess, port = value.port, publicHost = value.publicHost ?? ""
        };
        internal static bool Same(HostSettingsState a, HostSettingsState b) => a != null && b != null &&
            a.readOnly == b.readOnly && a.showUndiscovered == b.showUndiscovered && a.adminControls == b.adminControls &&
            a.captureBudgetMs == b.captureBudgetMs && a.remoteLanAccess == b.remoteLanAccess && a.port == b.port && (a.publicHost ?? "") == (b.publicHost ?? "");
    }
}
