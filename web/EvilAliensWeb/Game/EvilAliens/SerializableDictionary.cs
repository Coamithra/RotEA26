using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace EvilAliens;

[XmlRoot("dictionary")]
public class SerializableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, IXmlSerializable
{
	public XmlSchema GetSchema()
	{
		return null;
	}

	public void ReadXml(XmlReader reader)
	{
		XmlSerializer keySerializer = new XmlSerializer(typeof(TKey));
		XmlSerializer valueSerializer = new XmlSerializer(typeof(TValue));
		bool isEmptyElement = reader.IsEmptyElement;
		reader.Read();
		if (!isEmptyElement)
		{
			while (reader.NodeType != XmlNodeType.EndElement)
			{
				reader.ReadStartElement("item");
				reader.ReadStartElement("key");
				TKey key = (TKey)keySerializer.Deserialize(reader);
				reader.ReadEndElement();
				reader.ReadStartElement("value");
				TValue value = (TValue)valueSerializer.Deserialize(reader);
				reader.ReadEndElement();
				Add(key, value);
				reader.ReadEndElement();
				reader.MoveToContent();
			}
			reader.ReadEndElement();
		}
	}

	public void WriteXml(XmlWriter writer)
	{
		XmlSerializer keySerializer = new XmlSerializer(typeof(TKey));
		XmlSerializer valueSerializer = new XmlSerializer(typeof(TValue));
		foreach (TKey key in base.Keys)
		{
			writer.WriteStartElement("item");
			writer.WriteStartElement("key");
			keySerializer.Serialize(writer, key);
			writer.WriteEndElement();
			writer.WriteStartElement("value");
			TValue val = base[key];
			valueSerializer.Serialize(writer, val);
			writer.WriteEndElement();
			writer.WriteEndElement();
		}
	}
}
