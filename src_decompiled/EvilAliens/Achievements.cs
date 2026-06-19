using System;
using System.IO;
using System.Xml.Serialization;
using Microsoft.Xna.Framework.Storage;

namespace EvilAliens;

public class Achievements : Savable
{
	private static Achievements instance;

	public bool Awardment1Unlocked;

	public bool Awardment2Unlocked;

	public bool Awardment3Unlocked;

	public bool Awardment4Unlocked;

	public bool Awardment5Unlocked;

	public bool Awardment6Unlocked;

	public bool Awardment7Unlocked;

	public bool Awardment8Unlocked;

	public bool Awardment9Unlocked;

	public bool Awardment10Unlocked;

	public SerializableDictionary<Levels, LevelAchievement> Data = new SerializableDictionary<Levels, LevelAchievement>();

	public static void setInstace(Achievements instance)
	{
		Achievements.instance = instance;
	}

	public static Achievements GetInstance()
	{
		if (instance == null)
		{
			instance = new Achievements();
			instance.Reset();
		}
		return instance;
	}

	public bool GetAwardmentIsUnlocked(int index)
	{
		return index switch
		{
			0 => Awardment1Unlocked, 
			1 => Awardment2Unlocked, 
			2 => Awardment3Unlocked, 
			3 => Awardment4Unlocked, 
			4 => Awardment5Unlocked, 
			5 => Awardment6Unlocked, 
			6 => Awardment7Unlocked, 
			7 => Awardment8Unlocked, 
			8 => Awardment9Unlocked, 
			9 => Awardment10Unlocked, 
			_ => throw new Exception("Index out of bounds"), 
		};
	}

	public void SetAwardmentIsUnlocked(int index, bool value)
	{
		switch (index)
		{
		case 0:
			Awardment1Unlocked = value;
			break;
		case 1:
			Awardment2Unlocked = value;
			break;
		case 2:
			Awardment3Unlocked = value;
			break;
		case 3:
			Awardment4Unlocked = value;
			break;
		case 4:
			Awardment5Unlocked = value;
			break;
		case 5:
			Awardment6Unlocked = value;
			break;
		case 6:
			Awardment7Unlocked = value;
			break;
		case 7:
			Awardment8Unlocked = value;
			break;
		case 8:
			Awardment9Unlocked = value;
			break;
		case 9:
			Awardment10Unlocked = value;
			break;
		default:
			throw new Exception("Index out of bounds");
		}
	}

	public void Reset()
	{
		Data.Clear();
		if (Data.Count == 0)
		{
			for (int i = 0; i < Game1.GetEnumValues<Levels>().Count; i++)
			{
				Data[(Levels)i] = new LevelAchievement();
			}
		}
		for (int j = 0; j < 10; j++)
		{
			SetAwardmentIsUnlocked(j, value: false);
		}
		for (int k = 0; k < Game1.GetEnumValues<Levels>().Count; k++)
		{
			Data[(Levels)k].isFinished = false;
			Data[(Levels)k].difficulty = Settings.DifficultyLevel.Easy;
			Data[(Levels)k].hiscore = 0f;
		}
	}

	protected override void saveData(StorageContainer c)
	{
		string path = c.Path + "Achievements.xml";
		XmlSerializer xmlSerializer = new XmlSerializer(typeof(Achievements));
		using StreamWriter textWriter = new StreamWriter(path, append: false);
		xmlSerializer.Serialize(textWriter, instance);
	}

	protected override void loadData(StorageContainer c)
	{
		string path = c.Path + "Achievements.xml";
		if (File.Exists(path))
		{
			XmlSerializer xmlSerializer = new XmlSerializer(typeof(Achievements));
			using StreamReader textReader = new StreamReader(path);
			instance = xmlSerializer.Deserialize(textReader) as Achievements;
			return;
		}
		instance = new Achievements();
		instance.Reset();
	}

	protected override void onLoadError()
	{
		instance = new Achievements();
		instance.Reset();
	}

	protected override bool checkData()
	{
		bool flag = true;
		foreach (Levels enumValue in Game1.GetEnumValues<Levels>())
		{
			flag &= instance.Data.ContainsKey(enumValue);
		}
		return flag;
	}
}
