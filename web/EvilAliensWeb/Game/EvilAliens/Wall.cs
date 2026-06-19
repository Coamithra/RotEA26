using System;
using System.Collections.Generic;
using System.IO;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Wall : AlienDrawableGameComponent
{
	private bool[,] blocks;

	private Texture2D line;

	private CollisionLevelMap collisionMap;

	private int width => blocks.GetLength(1);

	private int height => blocks.GetLength(0);

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Unknown result type (might be due to invalid IL or missing references)
			if (collisionMap == null)
			{
				collisionMap = new CollisionLevelMap(base.Position, blocks);
			}
			else
			{
				collisionMap.SetOffset(base.Position);
			}
			return collisionMap;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		line = content.Load<Texture2D>("GFX/Base/black line lalalal");
	}

	public Wall(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Base/756-v1"));
		base.DrawOrder = 1;
	}

	public static Wall NewWall(ComponentBin collection, Game game)
	{
		Wall wall = collection.Recycle<Wall>();
		if (wall == null)
		{
			wall = new Wall(game);
		}
		return wall;
	}

	public void Setup(int variation)
	{
		//IL_0262: Unknown result type (might be due to invalid IL or missing references)
		//IL_027e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0283: Unknown result type (might be due to invalid IL or missing references)
		collisionMap = null;
		switch (variation)
		{
		case 0:
			blocks = new bool[122, 12]
			{
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				}
			};
			break;
		case 1:
			blocks = new bool[106, 7]
			{
				{ true, true, true, false, true, true, true },
				{ true, true, true, false, true, true, true },
				{ true, true, true, false, true, true, true },
				{ true, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, true, true, true, false, false },
				{ false, false, true, true, true, false, false },
				{ false, false, true, true, true, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, true },
				{ true, true, false, false, false, true, true },
				{ true, true, false, false, false, true, true },
				{ true, true, false, false, false, true, true },
				{ true, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, false, false, true, false, false, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, true, false, false, true, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, true, false, false, true, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, false, false, true, false, false, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, true, false, false, true, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, true, false, false, true, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, true },
				{ true, false, false, false, false, false, true },
				{ true, true, false, false, false, true, true },
				{ true, true, true, false, true, true, true },
				{ true, true, false, false, false, true, true },
				{ true, true, false, false, false, true, true },
				{ true, true, false, false, false, true, true },
				{ true, true, false, true, false, true, true },
				{ true, true, false, true, false, true, true },
				{ true, false, false, true, false, true, true },
				{ true, false, true, true, false, true, true },
				{ true, false, true, true, false, true, true },
				{ true, false, true, false, false, false, true },
				{ true, false, true, false, false, false, true },
				{ true, false, true, false, true, false, true },
				{ true, false, true, false, true, false, true },
				{ true, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, true, true, true, false, false, false },
				{ true, true, true, true, false, false, false },
				{ true, true, true, true, false, false, false },
				{ true, true, true, true, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, true, true, true, true },
				{ false, false, false, true, true, true, true },
				{ false, false, false, true, true, true, true },
				{ false, false, false, true, true, true, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false }
			};
			break;
		case 2:
			try
			{
				List<string> list = new List<string>();
				int num;
				using (StreamReader streamReader = new StreamReader(General.Path + "Levels/level3.txt"))
				{
					string text = streamReader.ReadLine();
					num = Convert.ToInt32(text.Remove(0, 6));
					while (true)
					{
						text = streamReader.ReadLine();
						if (text != null && !text.Contains("end"))
						{
							list.Add(text);
							continue;
						}
						break;
					}
				}
				blocks = new bool[list.Count, num];
				for (int i = 0; i < list.Count; i++)
				{
					for (int j = 0; j < num; j++)
					{
						if (j >= list[i].Length || list[i][j] == ' ')
						{
							blocks[i, j] = false;
						}
						else
						{
							blocks[i, j] = true;
						}
					}
				}
			}
			catch (Exception)
			{
				blocks = new bool[5, 19]
				{
					{
						true, true, true, false, true, true, false, false, true, true,
						false, false, false, true, false, false, true, true, false
					},
					{
						true, false, false, false, true, false, true, false, true, false,
						true, false, true, false, true, false, true, false, true
					},
					{
						true, true, true, false, true, true, false, false, true, true,
						false, false, true, false, true, false, true, true, false
					},
					{
						true, false, false, false, true, false, true, false, true, false,
						true, false, true, false, true, false, true, false, true
					},
					{
						true, true, true, false, true, false, true, false, true, false,
						true, false, false, true, false, false, true, false, true
					}
				};
			}
			break;
		case 3:
			blocks = new bool[179, 9]
			{
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, true, true, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, true, true, true, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, true, true, true, true, true, true },
				{ false, false, false, false, true, true, true, true, true },
				{ false, false, false, false, false, false, true, true, true },
				{ false, false, false, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, true, true, true, true, true, true, false, false },
				{ true, true, true, true, true, false, false, false, false },
				{ true, true, true, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, true, true, true, true, true, true },
				{ false, false, false, true, true, true, true, true, true },
				{ false, false, false, false, true, true, true, true, true },
				{ false, false, false, false, false, true, true, true, true },
				{ false, false, false, false, false, false, true, true, true },
				{ false, false, false, false, false, false, false, true, true },
				{ false, false, false, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, true, true, true, true, true, false, false, false },
				{ true, true, true, true, true, false, false, false, false },
				{ true, true, true, true, false, false, false, false, false },
				{ true, true, true, false, false, false, false, false, false },
				{ true, true, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, false, true },
				{ true, true, false, false, false, false, false, true, true },
				{ true, false, false, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, false, true },
				{ true, false, false, false, false, false, false, false, true },
				{ true, false, false, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, false, false, false, false, false, true },
				{ false, false, false, false, false, true, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, true, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, true, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, true, false },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, false, true, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, true, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, false, false, false, true, false, false },
				{ false, true, false, false, false, false, false, false, true },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, true, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, true, false, false },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, true },
				{ false, false, false, false, false, true, false, false, false },
				{ false, false, true, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, true, false, false },
				{ false, true, false, false, false, false, false, false, true },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, true, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, true, false },
				{ false, false, false, false, false, true, false, false, false },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, false, false, false, false, false, true },
				{ false, false, false, false, false, true, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, true, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, true, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, true, false },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, true, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, true, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, true, false }
			};
			break;
		case 4:
			blocks = new bool[11, 3]
			{
				{ false, false, true },
				{ false, false, false },
				{ true, false, false },
				{ false, false, false },
				{ false, false, true },
				{ false, false, false },
				{ true, false, false },
				{ false, false, false },
				{ false, false, true },
				{ false, false, false },
				{ true, false, false }
			};
			break;
		default:
			throw new Exception("illegal wall variation specified " + variation);
		}
		if (Settings.GetInstance().CurrentDifficulty <= Settings.DifficultyLevel.Medium)
		{
			int num2 = height / 2;
			bool[,] array = new bool[num2, width];
			for (int k = 0; k < num2; k++)
			{
				for (int l = 0; l < width; l++)
				{
					array[k, l] = blocks[k, l];
				}
			}
			blocks = array;
		}
		scale = 800f / (float)(texture.Width * width);
		float num3 = (float)texture.Height * scale;
		base.Position = new Vector2(0f, (0f - num3) * (float)height);
		base.Direction = (float)Math.PI / 2f;
		Vector2 backgroundSpeed = oracle.BackgroundSpeed;
		base.Speed = (backgroundSpeed).Length() * 1f;
	}

	public override void Initialize()
	{
		base.Initialize();
	}

	private bool isfree(int x, int y)
	{
		if (x < 0 || x >= width)
		{
			return false;
		}
		if (y < 0 || y >= height)
		{
			return true;
		}
		return !blocks[y, x];
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_016e: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_020c: Unknown result type (might be due to invalid IL or missing references)
		//IL_020e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0213: Unknown result type (might be due to invalid IL or missing references)
		//IL_022a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0249: Unknown result type (might be due to invalid IL or missing references)
		//IL_024b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0250: Unknown result type (might be due to invalid IL or missing references)
		//IL_0267: Unknown result type (might be due to invalid IL or missing references)
		//IL_0286: Unknown result type (might be due to invalid IL or missing references)
		//IL_0288: Unknown result type (might be due to invalid IL or missing references)
		//IL_028d: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		Vector2 val2 = default(Vector2);
		Color val3 = default(Color);
		Color val4 = default(Color);
		for (int i = 0; i < height; i++)
		{
			if (!((float)texture.Height * scale * (float)i + base.Position.Y > (float)(-texture.Height) * scale) || !((float)texture.Height * scale * (float)i + base.Position.Y <= 600f))
			{
				continue;
			}
			for (int j = 0; j < width; j++)
			{
				if (blocks[i, j])
				{
					Vector2 val = default(Vector2);
					val.X = (float)texture.Width * scale * (float)j;
					val.Y = (float)texture.Height * scale * (float)i;
					int num = 0;
					int num2 = j % 8;
					num = i % 8;
					spriteBatch.Draw(texture, new Rectangle(num2 * texture.Width / 8, num * texture.Height / 8, texture.Width / 8, texture.Height / 8), val + base.Position, 0f, scale * 8f, center: false);
					(val2) = new Vector2((float)texture.Width * scale / 2f);
					val += val2;
					(val3) = new Color(new Vector4(0f, 0f, 0f, 0.6f));
					(val4) = new Color(new Vector4(1f, 1f, 1f, 0.3f));
					if (isfree(j + 1, i))
					{
						spriteBatch.Draw(line, val + base.Position, 0f, scale * 2f, center: true, val3);
					}
					if (isfree(j - 1, i))
					{
						spriteBatch.Draw(line, val + base.Position, (float)Math.PI, scale * 2f, center: true, val4);
					}
					if (isfree(j, i + 1))
					{
						spriteBatch.Draw(line, val + base.Position, (float)Math.PI / 2f, scale * 2f, center: true, val3);
					}
					if (isfree(j, i - 1))
					{
						spriteBatch.Draw(line, val + base.Position, -(float)Math.PI / 2f, scale * 2f, center: true, val4);
					}
				}
			}
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		Vector2 backgroundSpeed = oracle.BackgroundSpeed;
		base.Speed = (backgroundSpeed).Length() * 1f;
		base.Update(gameTime);
		if (base.Position.Y > 600f)
		{
			Die();
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}
}
