using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EvilAliensWeb
{
    // Stage 1 spike: proves the full KNI/Blazor/WebGL pipeline works end to end —
    // WebGL context creation, GraphicsDevice.Clear, SpriteBatch (default shader),
    // runtime Texture2D creation, the Update/Draw loop, and keyboard input.
    // No game content required.
    public class SpikeGame : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private Texture2D _pixel;

        private Vector2 _pos = new Vector2(120, 120);
        private Vector2 _vel = new Vector2(180, 140);
        private float _t;

        public SpikeGame()
        {
            _graphics = new GraphicsDeviceManager(this);
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });
            base.LoadContent();
        }

        protected override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _t += dt;

            var vp = GraphicsDevice.Viewport;
            _pos += _vel * dt;

            // bounce off the edges of the viewport
            if (_pos.X < 0) { _pos.X = 0; _vel.X = Math.Abs(_vel.X); }
            if (_pos.Y < 0) { _pos.Y = 0; _vel.Y = Math.Abs(_vel.Y); }
            if (_pos.X > vp.Width - 80) { _pos.X = vp.Width - 80; _vel.X = -Math.Abs(_vel.X); }
            if (_pos.Y > vp.Height - 80) { _pos.Y = vp.Height - 80; _vel.Y = -Math.Abs(_vel.Y); }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            // pulsing colour so motion is obvious even on a small canvas
            float pulse = 0.5f + 0.5f * (float)Math.Sin(_t * 3.0);
            var color = new Color(1f, pulse, 0.2f);

            _spriteBatch.Begin();
            _spriteBatch.Draw(_pixel, new Rectangle((int)_pos.X, (int)_pos.Y, 80, 80), color);
            _spriteBatch.End();

            base.Draw(gameTime);
        }
    }
}
