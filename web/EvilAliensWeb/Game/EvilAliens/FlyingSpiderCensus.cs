using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace EvilAliens;

// Live flying-spider population readout behind the console's eaFlySpiders() (card 9c92962e).
//
// WHY IT EXISTS: the background spider's group-flatten costs whatever it costs PER SPIDER, so a
// frame-time number without the population that produced it says nothing. The first attempt at
// this measurement compared a background run against a foreground one and read the gap as a
// flatten cost -- but background spiders have Collides=false (never killed) and cross ~22% slower
// (BackgroundSpeed x 1.11 vs x 1.35), so the two runs settled at different populations and most of
// the gap was that. This prints the number that was missing, next to the settings that were also
// missing, so a figure pasted onto a card carries its own conditions.
//
// Console-only, polled by hand. It walks Game.Components (the Oracle.GetBaddies shape) rather than
// keeping a maintained mirror -- a stale mirror entry would report a spider that no longer exists,
// which is the one thing a census must never do.
internal static class FlyingSpiderCensus
{
	public static string Report()
	{
		Game game = ServiceHelper.Get<IComponentBinService>().ComponentBin.Game;
		int background = 0;
		int foreground = 0;
		foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
		{
			if (item is FlyingSpider spider)
			{
				if (spider.NetIsBackground)
				{
					background++;
				}
				else
				{
					foreground++;
				}
			}
		}
		return string.Format(CultureInfo.InvariantCulture,
			"[flyspiders] {0} live ({1} background, {2} foreground) | flatten={3} | box half={4} design px"
			+ " | pinned={5}",
			background + foreground, background, foreground,
			EvilAliensWeb.Compat.DebugFlags.FlySpiderFlatten,
			FlyingSpider.FlattenBoxHalfDesign,
			EvilAliensWeb.Compat.DebugFlags.FlySpiderCount.HasValue
				? EvilAliensWeb.Compat.DebugFlags.FlySpiderCount.Value.ToString(CultureInfo.InvariantCulture)
				: "no (streamed)");
	}
}
