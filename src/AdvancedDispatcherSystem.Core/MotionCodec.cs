using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AdvancedDispatcherSystem.Core
{
    // Binary motion avoids reflection boxing and per-number JSON strings inside Unity's managed heap.
    // Metadata and infrequent commands keep the readable JSON envelope.
    public static class MotionCodec
    {
        private const int Magic = 0x314D4441; // ADM1 in little endian
        public static bool IsMotion(byte[] bytes) => bytes.Length >= 4 && bytes[0] == 65 && bytes[1] == 68 && bytes[2] == 77 && bytes[3] == 49;
        public static void Write(Stream stream, string epoch, long revision, List<CarMotion> motions)
        {
            using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                w.Write(Magic); w.Write(epoch); w.Write(revision); w.Write(motions.Count);
                for (int i = 0; i < motions.Count; i++)
                {
                    var m = motions[i]; w.Write(m.id); w.Write(m.track1 ?? ""); w.Write(m.track2 ?? "");
                    w.Write(m.x); w.Write(m.z); w.Write(m.yaw); w.Write(m.speed); w.Write(m.span1); w.Write(m.span2);
                    w.Write(m.direction); w.Write(m.derailed); w.Write(m.sampledAt);
                }
            }
        }
        public static GameBatch Read(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var r = new BinaryReader(stream, Encoding.UTF8))
            {
                if (r.ReadInt32() != Magic) throw new InvalidDataException("MOTION_VERSION");
                string epoch = r.ReadString(); if (epoch.Length > 80) throw new InvalidDataException("EPOCH_SIZE");
                long revision = r.ReadInt64(); int count = r.ReadInt32();
                if (count < 0 || count > 100000 || count > bytes.Length / 60) throw new InvalidDataException("MOTION_COUNT");
                var motions = new List<CarMotion>(count);
                for (int i = 0; i < count; i++)
                {
                    var m = new CarMotion { id = r.ReadString(), track1 = r.ReadString(), track2 = r.ReadString(), x = r.ReadDouble(), z = r.ReadDouble(), yaw = r.ReadDouble(), speed = r.ReadDouble(), span1 = r.ReadDouble(), span2 = r.ReadDouble(), direction = r.ReadInt32(), derailed = r.ReadBoolean(), sampledAt = r.ReadInt64() };
                    if (m.id.Length > 100 || m.track1.Length > 100 || m.track2.Length > 100 || !Finite(m.x) || !Finite(m.z) || !Finite(m.yaw) || !Finite(m.speed) || !Finite(m.span1) || !Finite(m.span2) || m.direction < -1 || m.direction > 1) throw new InvalidDataException("INVALID_MOTION");
                    if (m.track1.Length == 0) m.track1 = null; if (m.track2.Length == 0) m.track2 = null;
                    motions.Add(m);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("MOTION_TRAILING_DATA");
                return new GameBatch { epoch = epoch, topologyRevision = revision, motions = motions };
            }
        }
        private static bool Finite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
    }
}
