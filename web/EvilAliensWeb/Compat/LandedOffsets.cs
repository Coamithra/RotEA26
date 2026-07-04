// ---------------------------------------------------------------------------
// LandedOffsets — per-sprite placement tuning for the Mars "landed" UFOs.
//
// The saucers that start parked on the Mars ground (ufometpootjes, Smallship_landed,
// Mediumship_landed) and the drifting Mothership_landed use a DIFFERENT still sprite
// than their flying animation. Two things need hand-tuning per sprite:
//   1. the ground SHADOW sitting under the parked ship (Floor.cs draws it from the
//      ship's collision box; it can read off-centre / wrong-sized for a landed still), and
//   2. the landed->flying handoff: the parked still has "landing feet" that offset its
//      visual centre from the flying frame's centre, so a saucer visibly JUMPS the frame
//      it lifts off.
//
// Rather than bake magic numbers, the values live in Content/data/landed_offsets.json,
// authored visually with wwwroot/landed-editor.html and imported here at runtime. Schema
// (all values DESIGN-space px, 800x600, +x right / +y down):
//   "<stationarySpriteName>": {
//       "landed":     [dx, dy],   // added to Position when drawing the parked still
//       "takeoff":    [dx, dy],   // added to Position ONCE at lift-off (feet compensation)
//       "shadow":     [dx, dy],   // nudges the ground shadow (x, and y along the floor line)
//       "shadowSize": s           // multiplies the ground shadow width
//   }
// Identity (all 0 / shadowSize 1, or a missing entry) reproduces the original untuned
// behaviour, so shipping the file with zeros changes nothing until the user tunes it.
//
// Loaded once, lazily, via TitleContainer.OpenStream (the same static-file read the
// LoadProfiler manifest uses) + JsonDocument (the trim-safe DOM parse SaveInterop uses).
// A missing/broken file just yields identity for every sprite.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
    public static class LandedOffsets
    {
        public struct Entry
        {
            public Vector2 Landed;
            public Vector2 Takeoff;
            public Vector2 Shadow;
            public float ShadowSize;

            public static Entry Identity => new Entry
            {
                Landed = Vector2.Zero,
                Takeoff = Vector2.Zero,
                Shadow = Vector2.Zero,
                ShadowSize = 1f
            };
        }

        private const string DataPath = "Content/data/landed_offsets.json";

        private static Dictionary<string, Entry> _entries;

        public static Entry Get(string spriteName)
        {
            if (_entries == null)
                _entries = Load();
            if (spriteName != null && _entries.TryGetValue(spriteName, out Entry e))
                return e;
            return Entry.Identity;
        }

        private static Dictionary<string, Entry> Load()
        {
            var result = new Dictionary<string, Entry>();
            string json;
            try
            {
                using Stream s = TitleContainer.OpenStream(DataPath);
                using var r = new StreamReader(s);
                json = r.ReadToEnd();
            }
            catch
            {
                // No data file — every sprite falls back to identity. Fine.
                return result;
            }
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object)
                        continue;   // skips "_comment" (a string) and any stray scalar
                    result[prop.Name] = ParseEntry(prop.Value);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[landedoffsets] parse failed, using identity: " + ex.Message);
            }
            return result;
        }

        private static Entry ParseEntry(JsonElement obj)
        {
            Entry e = Entry.Identity;
            if (obj.TryGetProperty("landed", out JsonElement l))
                e.Landed = ParseVec(l);
            if (obj.TryGetProperty("takeoff", out JsonElement t))
                e.Takeoff = ParseVec(t);
            if (obj.TryGetProperty("shadow", out JsonElement s))
                e.Shadow = ParseVec(s);
            if (obj.TryGetProperty("shadowSize", out JsonElement ss)
                && ss.ValueKind == JsonValueKind.Number)
                e.ShadowSize = (float)ss.GetDouble();
            return e;
        }

        private static Vector2 ParseVec(JsonElement arr)
        {
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 2)
                return Vector2.Zero;
            return new Vector2(
                (float)arr[0].GetDouble(),
                (float)arr[1].GetDouble());
        }
    }
}
