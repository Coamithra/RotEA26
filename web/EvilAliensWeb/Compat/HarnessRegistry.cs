// ---------------------------------------------------------------------------
// HarnessRegistry — name -> factory for the sprite harness (see HarnessScene +
// Compat/DebugFlags.cs "?harness=").
//
// Why this exists: iterating on an object's *drawing* code used to mean booting
// the whole game and trying to screenshot a moving enemy at exactly the right
// instant. The harness instead spawns ONE object on a space background, frozen,
// drawn through the real in-game pipeline — so you can screenshot it any time and
// the pixels are identical. This file just maps a URL-friendly name to "how to
// construct + Setup that object", reusing each type's real NewXxx(bin,game) +
// Setup(...) factory so the object is built exactly as the game builds it.
//
// To ADD an object: one line here — pick a lowercase key and call its real
// New* + Setup with sensible args. The factory returns the AlienDrawableGameComponent;
// HarnessScene takes care of positioning, freezing, frame, scale and rotation.
//
// Notes / limits:
//   * Every entry must derive from AlienDrawableGameComponent (so it draws itself
//     and exposes curframe/Position/scale/rotation). That's all standard enemies,
//     bosses and projectiles.
//   * Objects whose Draw depends on state only their Update reaches (e.g. a boss
//     mid-attack, the spider's airborne sheet) show their spawned/idle look frozen
//     — the harness deliberately does NOT run gameplay Update. Bosses are therefore
//     best-effort; the common per-frame sprite-sheet enemies are exact.
//   * A few objects need a live owner (Ball needs a JunkBoss, Option a PlayerShip,
//     etc.) and are intentionally omitted.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
    internal static class HarnessRegistry
    {
        // Build + Setup an object at `pos` (800x600 design space). Position is also
        // re-applied by HarnessScene after Initialize, so a Setup that ignores it is fine.
        internal delegate AlienDrawableGameComponent Factory(ComponentBin bin, Game game, Vector2 pos);

        private static readonly Dictionary<string, Factory> map =
            new Dictionary<string, Factory>(StringComparer.OrdinalIgnoreCase)
            {
                // --- common enemies ---
                ["spider"] = (bin, g, p) => { var s = Spider.NewSpider(bin, g); s.Setup(); return s; },
                ["flyingspider"] = (bin, g, p) => { var f = FlyingSpider.NewFlyingSpider(bin, g); f.Setup(false); return f; },
                ["ufo"] = (bin, g, p) => { var u = UFO.NewUFO(bin, g); u.Setup(p, true, EnemyBehaviour.normal); return u; },
                ["ufobig"] = (bin, g, p) => { var u = UFO.NewUFO(bin, g); u.Setup(p, true, EnemyBehaviour.normal); return u; },
                ["ufosmall"] = (bin, g, p) => { var u = UFO.NewUFO(bin, g); u.Setup(p, false, EnemyBehaviour.normal); return u; },
                ["sweepufo"] = (bin, g, p) => { var u = SweepUFO.NewSweepUFO(bin, g); u.Setup(false, 0, 1); return u; },
                ["asteroid"] = (bin, g, p) => { var a = Asteroid.NewAsteroid(bin, g); a.Setup(p, (float)Math.PI, 0f, false); return a; },
                ["asteroidbig"] = (bin, g, p) => { var a = Asteroid.NewAsteroid(bin, g); a.Setup(p, (float)Math.PI, 0f, true); return a; },
                ["braineroid"] = (bin, g, p) => { var b = Braineroid.NewBraineroid(bin, g); b.Setup(p, BrainSize.huge, 0f, true); return b; },
                ["braineroidmedium"] = (bin, g, p) => { var b = Braineroid.NewBraineroid(bin, g); b.Setup(p, BrainSize.medium, 0f, true); return b; },
                ["braineroidsmall"] = (bin, g, p) => { var b = Braineroid.NewBraineroid(bin, g); b.Setup(p, BrainSize.small, 0f, true); return b; },
                ["evilskull"] = (bin, g, p) => { var e = EvilSkull.NewEvilSkull(bin, g); e.Setup(p, EnemyBehaviour.normal); return e; },
                ["battleskull"] = (bin, g, p) => { var b = BattleSkull.NewBattleSkull(bin, g); b.Setup(p); return b; },
                ["starmine"] = (bin, g, p) => { var s = StarMine.NewStarMine(bin, g); s.Setup(); return s; },
                ["powerup"] = (bin, g, p) => { var pu = Powerup.NewPowerup(bin, g); pu.Setup(p); return pu; },
                ["plasmaball"] = (bin, g, p) => { var pb = PlasmaBall.NewAlien(bin, g); pb.Setup(p, (float)Math.PI); return pb; },
                ["paratrooper"] = (bin, g, p) => { var a = ParatrooperAlien.NewAlien(bin, g); a.Setup(); return a; },
                ["paratrooperbrain"] = (bin, g, p) => { var b = ParatrooperBrain.NewAlien(bin, g); b.Setup(p); return b; },
                ["wall"] = (bin, g, p) => { var w = Wall.NewWall(bin, g); w.Setup(0); return w; },

                // --- projectiles ---
                ["bullet"] = (bin, g, p) => { var b = Bullet.NewBullet(bin, g); b.Setup(p, (float)Math.PI / 2f, 999999f, 0); return b; },
                ["evilbullet"] = (bin, g, p) => { var b = EvilBullet.NewEvilBullet(bin, g); b.Setup(p, (float)Math.PI); return b; },
                ["blast"] = (bin, g, p) => { var b = Blast.NewBlast(bin, g); b.Setup(p, 3, 0); return b; },

                // --- bosses (best-effort: shown in their spawned/idle pose, not mid-attack) ---
                ["deathstar"] = (bin, g, p) => { var d = DeathStar.NewDeathStar(bin, g); d.Setup(p, EnemyBehaviour.normal); return d; },
                ["brainboss"] = (bin, g, p) => { var b = BrainBoss.NewBrainBoss(bin, g); b.Setup(false); return b; },
                ["classicboss"] = (bin, g, p) => { var c = ClassicBoss.NewClassicBoss(bin, g); c.Setup(); return c; },
                ["marsboss"] = (bin, g, p) => { var m = MarsBoss.NewMarsBoss(bin, g); m.Setup(MarsBoss.BossPosition.left); return m; },
                ["junkboss"] = (bin, g, p) => { var j = JunkBoss.NewJunkBoss(bin, g); j.Setup(false); return j; },
                ["fakeboss"] = (bin, g, p) => { var f = FakeBoss.NewFakeBoss(bin, g); f.Setup(); return f; },
                ["spiderboss"] = (bin, g, p) => { var s = SpiderBoss.NewSpiderBoss(bin, g); s.Setup(false); return s; },
                ["stationaryboss"] = (bin, g, p) => { var s = StationaryBoss.NewAlien(bin, g); s.Setup(); return s; },
            };

        public static bool TryGet(string name, out Factory factory)
        {
            factory = null;
            return !string.IsNullOrEmpty(name) && map.TryGetValue(name, out factory);
        }

        // Sorted list of every registered name — used for the error caption when an
        // unknown name is requested, and mirrored in wwwroot/harness.html's dropdown.
        public static IReadOnlyList<string> Names => map.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    }
}
