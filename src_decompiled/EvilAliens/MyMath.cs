using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public static class MyMath
{
	public static float SnapAngle(float angle, int sensitivity)
	{
		float num = angle + (float)Math.PI * 2f / (float)(2 * sensitivity);
		return num - Mod(num, (float)Math.PI * 2f / (float)sensitivity);
	}

	public static float DifferenceMod(float a, float b, float modulo)
	{
		if (Mod(b - a, modulo) > Mod(a - b, modulo))
		{
			return 0f - Mod(a - b, modulo);
		}
		return Mod(b - a, modulo);
	}

	public static float SnapAngle(Vector2 vector, int sensitivity)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		float num = VectorToAngle(vector);
		float num2 = num + (float)Math.PI * 2f / (float)(2 * sensitivity);
		return num2 - Mod(num2, (float)Math.PI * 2f / (float)sensitivity);
	}

	public static float Sin(float a)
	{
		return Convert.ToSingle(Math.Sin(a));
	}

	public static float Cos(float a)
	{
		return Convert.ToSingle(Math.Cos(a));
	}

	public static float ATan(float a)
	{
		return Convert.ToSingle(Math.Atan(a));
	}

	public static float VectorToAngle(Vector2 v)
	{
		return (float)Math.Atan2(v.Y, v.X);
	}

	public static float ACos(float a)
	{
		return Convert.ToSingle(Math.Acos(a));
	}

	public static float ASin(float a)
	{
		return Convert.ToSingle(Math.Asin(a));
	}

	public static Vector2 AngleToVector(float angle)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		Vector2 result = default(Vector2);
		result.X = Cos(angle);
		result.Y = Sin(angle);
		return result;
	}

	public static int Mod(int a, int b)
	{
		int num = a % b;
		if (num < 0)
		{
			return b + num;
		}
		return num;
	}

	public static float Mod(float a, float b)
	{
		float num = a % b;
		if (num < 0f)
		{
			return b + num;
		}
		return num;
	}

	public static float PowerCurve(float value1, float value2, float power, float step)
	{
		return MathHelper.Lerp(value1, value2, (float)Math.Pow(MathHelper.Clamp(step, 0f, 1f), power));
	}
}
