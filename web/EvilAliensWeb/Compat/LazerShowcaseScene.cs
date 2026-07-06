// ---------------------------------------------------------------------------
// LazerShowcaseScene — a live tuning stage for the "improve laser animation"
// card (Trello e9228efb). Unlike the FROZEN sprite harness / bullet showcase,
// the laser is all motion (a growing beam, crackling tendrils, a swirling
// chargeup swarm), so this scene lets it ANIMATE in place: the chargeup swarm
// on the left and a full-grown beam firing up on the right, both on the real
// starfield and drawn through the real pipeline (LazerGenerator / Quad).
//
// Opt in with ?lazershot. Pair it with the tuning knobs to A/B by eye:
//   ?lazerchargescale=<f>  chargeup particle scale   ?lazercapscale=<f> beam end-cap size
//   ?lazerarcs=<n>         tendril count             ?lazerarclife=<sec> tendril lifespan
// See Compat/DebugFlags.cs. Esc drops back to the menu (same contract as the
// harness / bullet showcase).
// ---------------------------------------------------------------------------
using System;
using System.Collections.ObjectModel;
using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    internal class LazerShowcaseScene : Scene
    {
        public delegate void ExitEvent();

        public ExitEvent OnExitToMenu;

        private Background background;
        private LazerGenerator charge;
        private Quad beam;
        private bool error;
        private string errorLine;

        // Beam geometry (design space): muzzle near the bottom-right, firing straight up.
        private static readonly Vector2 BeamMuzzle = new Vector2(560f, 545f);
        private const float BeamDir = -(float)Math.PI / 2f; // up on screen
        private const float BeamLength = 470f;
        private const float BeamWidth = 16f;
        // Chargeup swarm centre (left half).
        private static readonly Vector2 ChargePos = new Vector2(250f, 300f);

        public LazerShowcaseScene(Game game)
            : base(game)
        {
            base.DrawOrder = 2000; // beam + caption on top of everything (bloom is at 950)
            background = new Background(game);
        }

        public override void Initialize()
        {
            base.Initialize();

            // A keyboard "player" so anything reading the Oracle has one (mirrors the other showcases).
            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            oracle.ResetPlayers();
            oracle.AddPlayer(ControlDevice.Keyboard);

            background.SetSpace();
            ((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)background);

            try
            {
                // Chargeup swarm: a real LazerGenerator, left RUNNING (Enabled=true) so its
                // particles animate + re-seed forever. Silent so it doesn't loop the charge SFX.
                charge = LazerGenerator.NewLazerGenerator(Collection, base.Game);
                charge.Setup(ChargePos, 2f, 1f, 0f, 0f);
                charge.SetWindup(2.5f, loop: true); // LOOP the 1->peak ramp + energy-well growth so it can be watched
                charge.SetupSilent(); // AFTER Setup (which clears silent) + BEFORE Add (Initialize plays the SFX)
                // Add through the scene's ComponentBin (the in-game path) so it's actually TICKED
                // each frame -- the swarm needs its Update to run (the particle alpha is 0 at birth
                // and only rises as they age), so a non-updated generator draws nothing.
                Collection.Add((GameComponent)(object)charge);
                charge.Position = ChargePos;
                charge.Visible = true; // the generator ctor defaults Visible=false; force-draw it here

                // Full-grown beam: a raw Quad we drive ourselves (the game's Lazer grows/dissipates
                // over its lifetime; here we want a stable beam that just sits and crackles). It's
                // drawn by the SAME Quad.Draw pipeline the card is about, so the look is 1:1.
                beam = new Quad(base.Game, BeamMuzzle, BeamDir, BeamWidth, 0f, 0f);
                beam.LoadContent();
                beam.SetProperties(BeamMuzzle, BeamDir, BeamLength, 0f); // lead 0 => full beam from the muzzle
            }
            catch (Exception ex)
            {
                error = true;
                errorLine = ex.GetType().Name + ": " + ex.Message;
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (base.InputHandler.Pressed(MyKeys.Esc) && OnExitToMenu != null)
            {
                OnExitToMenu();
            }
        }

        public void Teardown()
        {
            if (charge != null)
            {
                Collection.Remove((GameComponent)(object)charge);
                charge = null;
            }
            beam = null;
            Collection.Remove((GameComponent)(object)background);
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
            if (error)
            {
                base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
                base.SpriteBatch.DrawString("lazershot failed to build:", new Vector2(40f, 60f), Color.OrangeRed, 0f, centered: false, 0.6f, (SpriteEffects)0, 0f);
                base.SpriteBatch.DrawString(errorLine ?? "", new Vector2(40f, 86f), Color.OrangeRed, 0f, centered: false, 0.6f, (SpriteEffects)0, 0f);
                return;
            }

            // The beam draws itself through the real Quad pipeline (additive, its own blend push/pop).
            beam?.Draw((float)gameTime.TotalGameTime.TotalSeconds);

            base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
            base.SpriteBatch.DrawString("lazershot   chargeup (left) + full beam (right)",
                new Vector2(16f, 12f), new Color(Color.White, 0.85f), 0f, centered: false, 0.5f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString("drag the top-right panel to tune live (chargeup / cap / tendrils)",
                new Vector2(16f, 40f), new Color(Color.White, 0.55f), 0f, centered: false, 0.42f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString("Esc: menu", new Vector2(16f, 574f), new Color(Color.White, 0.5f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
        }
    }
}
