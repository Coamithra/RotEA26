using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliensWeb
{
    // Stage 3 verification harness: loads real game content through the
    // WebContentManager (textures + the menu SpriteFont + a Curve) and draws it.
    // Proves the unpacked assets load in-browser without a ContentLoadException,
    // independent of the full Game1 boot path (threading/effects come in Stage 4/5).
    public class ContentTestGame : Game
    {
        private readonly GraphicsDeviceManager _graphics;
        private WebContentManager _content;
        private SpriteBatch _spriteBatch;

        private SpriteFont _font;
        private Curve _curve;
        private readonly List<(string name, Texture2D tex)> _textures = new();
        private string _status = "loading...";
        private float _t;

        public ContentTestGame()
        {
            _graphics = new GraphicsDeviceManager(this);
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _content = new WebContentManager(Services, "Content");

            try
            {
                // A spread of the real assets: menu, HUD, sprites, a DXT background,
                // both content-path conventions the game uses (with/without "Content/").
                LoadTex("GFX/Menu/title");
                LoadTex("GFX/Menu/evilskull");
                LoadTex("GFX/HUD/BarLit");
                LoadTex("GFX/Sprites/playersheet");
                LoadTex("GFX/MarsBG/mars1");          // DXT5 -> RGBA
                LoadTex("GFX/Base/756");              // DXT1 -> RGBA
                LoadTex("Content/GFX/Splash/blank");  // base.Content prefix style

                _font = _content.Load<SpriteFont>("GFX/Menu/menufont");
                _curve = _content.Load<Curve>("GFX/Effects/BrainCurve");

                _status = string.Format("OK: {0} textures, font ({1} glyphs), curve ({2} keys)",
                    _textures.Count, _font.Characters.Count, _curve.Keys.Count);
            }
            catch (Exception e)
            {
                _status = "FAIL: " + e.GetType().Name + ": " + e.Message;
                Console.WriteLine("[ContentTest] " + _status);
                Console.WriteLine(e.StackTrace);
            }

            Console.WriteLine("[ContentTest] " + _status);
            base.LoadContent();
        }

        private void LoadTex(string name)
        {
            var t = _content.Load<Texture2D>(name);
            _textures.Add((name, t));
        }

        protected override void Update(GameTime gameTime)
        {
            _t += (float)gameTime.ElapsedGameTime.TotalSeconds;
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(20, 20, 40));
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            // Title across the top.
            if (_textures.Count > 0 && _textures[0].tex != null)
                _spriteBatch.Draw(_textures[0].tex, new Vector2(20, 10), Color.White);

            // Thumbnails of every loaded texture in a row.
            int x = 20, y = 210;
            foreach (var (name, tex) in _textures)
            {
                int w = Math.Min(120, tex.Width);
                int h = tex.Height * w / Math.Max(1, tex.Width);
                _spriteBatch.Draw(tex, new Rectangle(x, y, w, h), Color.White);
                if (_font != null)
                    _spriteBatch.DrawString(_font, name, new Vector2(x, y + h + 2), Color.LightGray, 0f,
                        Vector2.Zero, 0.3f, SpriteEffects.None, 0f);
                x += w + 12;
                if (x > GraphicsDevice.Viewport.Width - 130) { x = 20; y += 180; }
            }

            // Status line + a font render test.
            if (_font != null)
            {
                _spriteBatch.DrawString(_font, _status, new Vector2(20, 165), Color.Lime, 0f,
                    Vector2.Zero, 0.5f, SpriteEffects.None, 0f);
                _spriteBatch.DrawString(_font, "The quick brown fox 0123456789!?",
                    new Vector2(20, GraphicsDevice.Viewport.Height - 50), Color.White, 0f,
                    Vector2.Zero, 0.6f, SpriteEffects.None, 0f);
            }

            _spriteBatch.End();
            base.Draw(gameTime);
        }
    }
}
