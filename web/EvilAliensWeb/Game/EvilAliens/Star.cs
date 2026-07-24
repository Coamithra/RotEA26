using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Star
{
	private const float _accel = 1.02f;

	private Game1 _game;

	private float _size;

	private float _initialSize;

	private TimeSpan _timer;

	private Texture2D _sprite;

	private Vector2 _position;

	private Vector2 _origin;

	private Vector2 _speed;

	private Vector2 _speedoriginal;

	public bool IsOffScreen(int x, int y)
	{
		return (_position.X > (float)(x + 200)) | (_position.X < -200f) | (_position.Y > (float)(y + 200)) | (_position.Y < -200f);
	}

	public void Reset(Vector2 position, float size, double direction, double speed)
	{
		_position = position;
		_origin = position;
		_size = size;
		_initialSize = size;
		_speed = new Vector2(Convert.ToSingle(Math.Cos(direction) * speed), Convert.ToSingle(Math.Sin(direction) * speed));
		_speedoriginal = _speed;
		_timer = TimeSpan.Zero;
	}

	public Star(Game1 game, Texture2D sprite, Vector2 position, float size, double direction, double speed)
	{
		_game = game;
		_sprite = sprite;
		Reset(position, size, direction, speed);
	}

	public void ReloadSprite(Texture2D sprite)
	{
		_sprite = sprite;
	}

	public void Draw(bool red)
	{
		SpriteBatchWrapper spriteBatchWrapper = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		spriteBatchWrapper.BlendMode = (SpriteBlendMode)1;
		if (red)
		{
			spriteBatchWrapper.Draw(_sprite, _position, 0f, _size, center: true, Color.Orange);
		}
		else
		{
			spriteBatchWrapper.Draw(_sprite, _position, 0f, _size, center: true);
		}
	}

	public void Move(bool hyperspace, GameTime gameTime)
	{
		_timer += gameTime.ElapsedGameTime;
		_position += _speed * Convert.ToSingle(gameTime.ElapsedGameTime.TotalMilliseconds / 16.666666666666668);
		_speed = _speedoriginal * Convert.ToSingle(Math.Pow(1.0199999809265137, _timer.TotalMilliseconds / 16.666666666666668));
		if (hyperspace)
		{
			_position += _speed * Convert.ToSingle(gameTime.ElapsedGameTime.TotalMilliseconds / 16.666666666666668);
			_speed = _speedoriginal * Convert.ToSingle(Math.Pow(1.0199999809265137, _timer.TotalMilliseconds / 16.666666666666668));
		}
		Vector2 val = _position - _origin;
		float num = (val).Length() / 7000f;
		_size = _initialSize + num;
	}

	internal void MoveForward(int factor)
	{
		_timer += new TimeSpan(0, 0, 0, 0, factor);
		_speed = _speedoriginal * Convert.ToSingle(Math.Pow(1.0199999809265137, _timer.TotalMilliseconds / 16.666666666666668));
		_position += _speed * Convert.ToSingle((float)factor / 16.666666f);
	}
}
