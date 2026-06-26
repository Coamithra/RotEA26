// ---------------------------------------------------------------------------
// BulletShowcaseScene — a frozen reference tableau for the "redraw the bullets"
// task (Trello card: a screenshot of starfield + UFOs + the player ship + both
// bullet types, to feed to an image model for bullet-sprite inspiration).
//
// It's the sprite harness idea (Compat/HarnessScene.cs) generalised from ONE
// object to a small COMPOSED scene: the player ship at the bottom, a cluster of
// UFOs up top, a rising stream of player bullets (GFX/Sprites/bulletgood) and a
// falling stream of evil bullets (GFX/Sprites/bulletevil), all on the real
// starfield and drawn through the real pipeline (SpriteBatchWrapper / RenderScale
// / blend mapping / bloom / gamma). Like the harness everything is FROZEN
// (Enabled=false) so a screenshot at any moment is pixel-identical.
//
// Opt in with ?bulletshot. Reusable: once the bullets are redrawn, the same scene
// shows the new art in context (relative size next to the ship/UFOs, on the bloom).
//
// Why Enabled=false is enough (no spurious collisions): objects added to
// Game.Components are auto-registered with the CollisionHandler, but its
// DetectCollisions() only pairs collidables when BOTH are Enabled — so freezing
// every object makes them collision-immune as well as motionless.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    internal class BulletShowcaseScene : Scene
    {
        public delegate void ExitEvent();

        // Esc -> Game1 tears the scene down and shows the menu (same contract as
        // HarnessScene.OnExitToMenu).
        public ExitEvent OnExitToMenu;

        private Background background;
        private readonly List<AlienDrawableGameComponent> objects = new List<AlienDrawableGameComponent>();
        private bool error;
        private string errorLine;

        public BulletShowcaseScene(Game game)
            : base(game)
        {
            base.DrawOrder = 2000; // caption on top of everything (incl. bloom at 950)
            background = new Background(game);
        }

        public override void Initialize()
        {
            base.Initialize();

            // A keyboard "player" so the ship's Setup/Draw (which reads the Oracle for
            // its controller + hue) has one. Mirrors HarnessScene / Game1.LaunchLevelDirect.
            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            oracle.ResetPlayers();
            oracle.AddPlayer(ControlDevice.Keyboard);

            background.SetSpace();
            ((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)background);

            try
            {
                BuildTableau();
            }
            catch (Exception ex)
            {
                error = true;
                errorLine = ex.GetType().Name + ": " + ex.Message;
            }
        }

        // Construct + Setup an object exactly as the game does (New* + Setup, then add
        // to the component list so Initialize/LoadContent run), then park + freeze it.
        private void Freeze(AlienDrawableGameComponent obj, Vector2 pos)
        {
            ((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)obj);
            obj.Position = pos;
            obj.Enabled = false; // freeze: no gameplay Update AND collision-immune (see header)
            obj.Visible = true;
            objects.Add(obj);
        }

        private void BuildTableau()
        {
            ComponentBin bin = Collection;
            Game g = base.Game;

            // --- player ship, bottom-centre, facing up ---
            PlayerShip ship = new PlayerShip(g);
            ship.Setup(0, new Vector2(400f, 500f), startup: false, invulnerable: false, -(float)Math.PI / 2f);
            Freeze(ship, new Vector2(400f, 500f));

            // --- UFO cluster across the top third (one big + four small) ---
            (Vector2 pos, bool big)[] ufos =
            {
                (new Vector2(400f, 115f), true),
                (new Vector2(220f, 150f), false),
                (new Vector2(560f, 150f), false),
                (new Vector2(315f, 95f), false),
                (new Vector2(490f, 98f), false),
            };
            foreach (var (pos, big) in ufos)
            {
                UFO u = UFO.NewUFO(bin, g);
                u.Setup(pos, big, EnemyBehaviour.normal);
                Freeze(u, pos);
            }

            // --- player bullets (bulletgood): rising stream from the ship toward the
            //     UFOs, fanned slightly. Kept in the clear band (y ~250..460) between
            //     the ship and the UFOs. ---
            Vector2[] goodBullets =
            {
                new Vector2(400f, 445f), new Vector2(399f, 385f), new Vector2(401f, 325f), new Vector2(400f, 268f),
                new Vector2(362f, 425f), new Vector2(332f, 360f),
                new Vector2(438f, 425f), new Vector2(468f, 360f),
            };
            foreach (Vector2 pos in goodBullets)
            {
                Bullet b = Bullet.NewBullet(bin, g);
                b.Setup(pos, -(float)Math.PI / 2f, 999999f, 0);
                Freeze(b, pos);
            }

            // --- evil bullets (bulletevil): falling stream from the UFOs toward the
            //     ship, on different columns so both streams stay readable. ---
            Vector2[] evilBullets =
            {
                new Vector2(250f, 205f), new Vector2(284f, 295f),
                new Vector2(560f, 205f), new Vector2(524f, 295f),
                new Vector2(620f, 250f), new Vector2(185f, 260f),
                new Vector2(400f, 360f),
            };
            foreach (Vector2 pos in evilBullets)
            {
                EvilBullet b = EvilBullet.NewEvilBullet(bin, g);
                b.Setup(pos, (float)Math.PI / 2f);
                Freeze(b, pos);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            // Re-assert the freeze each frame (defensive; Enabled=false already stops
            // each object's own Update and keeps them out of collision pairing).
            foreach (AlienDrawableGameComponent obj in objects)
            {
                obj.Enabled = false;
            }

            if (base.InputHandler.Pressed(MyKeys.Esc) && OnExitToMenu != null)
            {
                OnExitToMenu();
            }
        }

        // Remove every object + the background. Deferred through the ComponentBin so it
        // is safe to call from within the update loop. Game1 then drops the scene.
        public void Teardown()
        {
            foreach (AlienDrawableGameComponent obj in objects)
            {
                Collection.Remove((GameComponent)(object)obj);
            }
            objects.Clear();
            Collection.Remove((GameComponent)(object)background);
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
            base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
            if (error)
            {
                base.SpriteBatch.DrawString("bulletshot failed to build:", new Vector2(40f, 60f), Color.OrangeRed, 0f, centered: false, 0.6f, (SpriteEffects)0, 0f);
                base.SpriteBatch.DrawString(errorLine ?? "", new Vector2(40f, 86f), Color.OrangeRed, 0f, centered: false, 0.6f, (SpriteEffects)0, 0f);
                return;
            }
            base.SpriteBatch.DrawString("bulletshot   ship + ufos + bulletgood (rising) + bulletevil (falling)",
                new Vector2(16f, 12f), new Color(Color.White, 0.85f), 0f, centered: false, 0.5f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString("Esc: menu", new Vector2(16f, 574f), new Color(Color.White, 0.5f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
        }
    }
}
