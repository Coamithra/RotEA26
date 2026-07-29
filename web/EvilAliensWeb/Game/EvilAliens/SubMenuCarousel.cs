using Microsoft.Xna.Framework;

namespace EvilAliens;

// Shared carousel geometry (card 2001fbd8), extracted verbatim from SubMenuLevelChoice so the
// online-game browser (SubMenuOnlineGames) can reuse the exact flying/scaling entry motion
// without SubMenuLevelChoice's level-keyed data (levels[], unlock gating, the achievement
// overlay). The base owns the scroll animation + the interpolation that positions each entry;
// a subclass supplies only what a single entry looks like (DrawEntryAt) and any header/footer
// chrome (DrawCarouselOverlay). The visible-entry set is the standard unlockable filter, so an
// all-unlocked menu (the browser) simply draws every entry.
internal abstract class SubMenuCarousel : MenuSub1
{
    private int preferredDirection;

    protected float scroller;

    protected Timer swaptimer = new Timer(400f, repeating: false);

    private float prevSelected;

    protected int targetSelected;

    protected SubMenuCarousel(Game game)
        : base(game)
    {
        prevSelected = selectedEntry;
        targetSelected = selectedEntry;
        swaptimer.Stop();
        swaptimer.Reset();
        // Carousel: hovering a flying/scaling entry shouldn't snap the selection —
        // only a click picks (DrawEntryAt records each on-screen entry's box).
        mouseHoverSelects = false;
    }

    private bool Visible(int i)
    {
        return !unLockableDataEntries[i].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[i].item);
    }

    public override void Update(GameTime gameTime)
    {
        swaptimer.Update(gameTime);
        if (swaptimer.Active)
        {
            int forwardSteps = 0;
            int backwardSteps = 0;
            int scanIndex = (int)System.Math.Round(prevSelected);
            while (scanIndex != targetSelected)
            {
                scanIndex = MyMath.Mod(scanIndex + 1, menuEntries.Count);
                if (Visible(scanIndex))
                {
                    forwardSteps++;
                }
            }
            scanIndex = (int)prevSelected;
            while (scanIndex != targetSelected)
            {
                scanIndex = MyMath.Mod(scanIndex - 1, menuEntries.Count);
                if (Visible(scanIndex))
                {
                    backwardSteps++;
                }
            }
            int wrappedTarget = targetSelected;
            if (backwardSteps < forwardSteps)
            {
                if ((float)wrappedTarget > prevSelected)
                {
                    wrappedTarget -= menuEntries.Count;
                }
            }
            else if (backwardSteps > forwardSteps)
            {
                if ((float)wrappedTarget < prevSelected)
                {
                    wrappedTarget += menuEntries.Count;
                }
            }
            else if (preferredDirection == 1)
            {
                if ((float)wrappedTarget < prevSelected)
                {
                    wrappedTarget += menuEntries.Count;
                }
            }
            else if ((float)wrappedTarget > prevSelected)
            {
                wrappedTarget -= menuEntries.Count;
            }
            scroller = MyMath.Mod(prevSelected + MathHelper.SmoothStep(0f, (float)wrappedTarget - prevSelected, 1f - swaptimer.Normalized), menuEntries.Count);
        }
        else if (swaptimer.Finished)
        {
            swaptimer.Reset();
            swaptimer.Stop();
            prevSelected = selectedEntry;
            scroller = selectedEntry;
        }
        base.Update(gameTime);
        if (selectedEntry != targetSelected)
        {
            swaptimer.Reset();
            swaptimer.Start();
            targetSelected = selectedEntry;
            prevSelected = scroller;
        }
    }

    // Re-seat the scroll animation on the current selection after the entry LIST changed
    // out from under it (the online browser rebuilds its entries as the server list
    // refreshes). Clamps the selection into range and parks the carousel (no in-flight swap).
    protected void SyncCarouselToSelection()
    {
        if (menuEntries.Count == 0)
        {
            selectedEntry = 0;
            targetSelected = 0;
            prevSelected = 0f;
            scroller = 0f;
            swaptimer.Stop();
            swaptimer.Reset();
            return;
        }
        if (selectedEntry >= menuEntries.Count)
        {
            selectedEntry = menuEntries.Count - 1;
        }
        if (selectedEntry < 0)
        {
            selectedEntry = 0;
        }
        targetSelected = selectedEntry;
        prevSelected = selectedEntry;
        scroller = selectedEntry;
        swaptimer.Stop();
        swaptimer.Reset();
    }

    protected override void selectNext()
    {
        base.selectNext();
        preferredDirection = 1;
    }

    protected override void selectPrevious()
    {
        base.selectPrevious();
        preferredDirection = -1;
    }

    public override void DrawMenu(GameTime gameTime, float yoffset)
    {
        // Empty carousel (the browser found no games): the final DrawEntryAt(targetSelected)
        // below would index an entry that isn't there, so draw only the overlay.
        if (menuEntries.Count == 0)
        {
            DrawCarouselOverlay(gameTime);
            return;
        }
        int visibleCount = 0;
        int prevVisible = int.MaxValue;
        int firstVisible = int.MaxValue;
        float a = 0f;
        for (int i = 0; i < menuEntries.Count; i++)
        {
            if (Visible(i))
            {
                if ((float)prevVisible <= scroller && scroller < (float)i)
                {
                    a = (float)(visibleCount - 1) + (scroller - (float)prevVisible) / (float)(i - prevVisible);
                }
                if (firstVisible > menuEntries.Count)
                {
                    firstVisible = i;
                }
                prevVisible = i;
                visibleCount++;
            }
        }
        if ((float)prevVisible <= scroller && scroller < (float)menuEntries.Count)
        {
            a = (float)(visibleCount - 1) + (scroller - (float)prevVisible) / (float)(menuEntries.Count + firstVisible - prevVisible);
        }
        if (0f <= scroller && scroller < (float)firstVisible)
        {
            a = (float)(visibleCount - 1) + (scroller + (float)menuEntries.Count - (float)prevVisible) / (float)(menuEntries.Count + firstVisible - prevVisible);
        }
        int visibleIndex = 0;
        int selectedVisibleIndex = 0;
        for (int j = 0; j < menuEntries.Count; j++)
        {
            if (Visible(j))
            {
                float step = 0.5f + 0.333f * MyMath.DifferenceMod(a, visibleIndex, visibleCount);
                if (j != targetSelected)
                {
                    DrawEntryAt(j, step);
                }
                else
                {
                    selectedVisibleIndex = visibleIndex;
                }
                visibleIndex++;
            }
        }
        float step2 = 0.5f + 0.33f * MyMath.DifferenceMod(a, selectedVisibleIndex, visibleCount);
        DrawEntryAt(targetSelected, step2);
        DrawCarouselOverlay(gameTime);
    }

    // Draw one carousel entry (a flying/scaling screenshot etc.), centred per the shared
    // interpolation. `step` in (0,1) maps to on-screen position/scale/alpha exactly as the
    // level-select carousel did; steps outside (0,1) are off-screen and drawn as nothing.
    // Implementers MUST call RecordEntryHit for the entry they draw so it stays clickable.
    protected abstract void DrawEntryAt(int entry, float step);

    // Optional fixed chrome drawn once after all entries (the selected level's name/briefing,
    // the browser's header/instructions). Default: nothing.
    protected virtual void DrawCarouselOverlay(GameTime gameTime)
    {
    }
}
