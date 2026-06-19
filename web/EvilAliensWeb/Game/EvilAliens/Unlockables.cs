using System.IO;
using System.Xml.Serialization;
using Microsoft.Xna.Framework.Storage;

namespace EvilAliens;

public class Unlockables : Savable
{
	public enum Items
	{
		HarderDifficulties,
		InsaneDifficulty,
		Level2,
		Level3,
		ClassicAliens,
		SpaceDodge,
		Braineroids,
		TeamChallenge,
		BossTrain,
		Friends,
		InfiniteLives,
		GalagaMode,
		Connector,
		Turbo,
		PowerUp,
		Cheats,
		CrazyGame,
		OwnLevel,
		Challenges,
		Paratrooper,
		Awardments
	}

	private static Unlockables instance;

	public SerializableDictionary<Items, bool> Collection = new SerializableDictionary<Items, bool>();

	public static Unlockables GetInstance()
	{
		if (instance == null)
		{
			instance = new Unlockables();
		}
		return instance;
	}

	public static void SetInstance(Unlockables instance)
	{
		Unlockables.instance = instance;
	}

	public Unlockables()
	{
		Reset();
	}

	public bool IsUnlocked(Items item)
	{
		return Collection[item];
	}

	public void Unlock(Items item)
	{
		Collection[item] = true;
	}

	public void Reset()
	{
		Collection.Clear();
		for (int i = 0; i < Game1.GetEnumValues<Items>().Count; i++)
		{
			Collection.Add((Items)i, value: false);
		}
	}

	protected override void saveData(StorageContainer c)
	{
		string path = c.Path + "Unlockables.xml";
		XmlSerializer xmlSerializer = new XmlSerializer(typeof(Unlockables));
		using StreamWriter textWriter = new StreamWriter(path, append: false);
		xmlSerializer.Serialize(textWriter, instance);
	}

	protected override void loadData(StorageContainer c)
	{
		string path = c.Path + "Unlockables.xml";
		if (File.Exists(path))
		{
			XmlSerializer xmlSerializer = new XmlSerializer(typeof(Unlockables));
			using StreamReader textReader = new StreamReader(path);
			instance = xmlSerializer.Deserialize(textReader) as Unlockables;
			return;
		}
		instance = new Unlockables();
		instance.Reset();
	}

	protected override void onLoadError()
	{
		instance = new Unlockables();
	}

	protected override bool checkData()
	{
		bool flag = true;
		foreach (Items enumValue in Game1.GetEnumValues<Items>())
		{
			flag &= instance.Collection.ContainsKey(enumValue);
		}
		return flag;
	}
}
